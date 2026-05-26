import { deleteFileHandle, getFileHandle, saveFileHandle } from './file-handle-storage';
import { grantFileUploadPermissionsInvoker, requestFileHandlePermission, GetFilePermissionsRequest } from './file-handle-permissions';
import { getLogs } from 'logging';
import { PromiseSource } from 'actuallab-core';
import { v4 as uuidv4 } from 'uuid';
import { NullableJSObjectReference } from 'UI.Blazor/JSRuntime/nullable-js-object-reference';
import { AttachmentWebFilePickerRegistry } from '../../Components/ChatMessageEditor/attachment-web-file-picker';
import type { IUploadStreamSource } from '../../../UI.Blazor/Services/FileUploads/web-uploads';

const { errorLog } = getLogs('WebFileProvider');

interface CreateWebFileProviderResult {
    previewUrl: string;
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    fileProvider : any;
}

export class WebFileProviders
{
    public static createFromFileId(fileId : number) : CreateWebFileProviderResult | null
    {
        const fileResult = AttachmentWebFilePickerRegistry.Get(fileId);
        if (!fileResult)
            return null;

        let previewUrl = '';
        try {
            const file = fileResult.file;
            const provider = new WebFileProvider('', fileResult.fileHandle, file, null);
            previewUrl = provider.createPreviewUrl();
            const jsObjectReference = DotNet.createJSObjectReference(provider);
            return {
                previewUrl: previewUrl,
                fileProvider: jsObjectReference,
            };
        }
        catch (e) {
            errorLog?.log('Failed to create a web file attachment', e);
            if (previewUrl)
                URL.revokeObjectURL(previewUrl);
            return null;
        }
    }

    public static async tryCreateFromFileHandleDbKey(fileHandleDbKey : string) : Promise<NullableJSObjectReference> {
        const fileHandle = await getFileHandle(fileHandleDbKey);
        if (fileHandle == null) {
            await deleteFileHandle(fileHandleDbKey);
            return NullableJSObjectReference.create(null);
        }

        const grantedReadPermissionRequest = await requestFileHandlePermission(fileHandle, 'read');
        const provider = new WebFileProvider(fileHandleDbKey, fileHandle, null, grantedReadPermissionRequest);
        return NullableJSObjectReference.create(provider);
    }

    public static async deleteFileHandleFromDb(fileHandleDbKey : string)
    {
        try {
            await deleteFileHandle(fileHandleDbKey);
        }
        catch (e) {
            errorLog?.log('Failed to delete file handle from db', e);
        }
    }

    public static grantFileUploadPermissions() {
        void grantFileUploadPermissionsInvoker.invoke();
    }

    public static startMonitorGrantPermissionsRequests(blazorRef: DotNet.DotNetObject)
    {
        grantFileUploadPermissionsInvoker.hasCallbacksChanged.add(() => {
            const hasCallbacks = grantFileUploadPermissionsInvoker.hasCallbacks();
            void blazorRef.invokeMethodAsync('OnPendingRequestsHaveChanged', hasCallbacks);
        });
    }
}

export class WebFileProvider implements IUploadStreamSource {
    private readonly userConsentRequest: GetFilePermissionsRequest | null;
    private readonly whenUserConsentGrantedSource: PromiseSource<boolean>;
    private previewUrl: string | null = null;
    private userConsentGranted : boolean | null = null;
    private resolvedFile: Blob | null;

    constructor(
        private fileHandleDbKey: string,
        private readonly fileHandle: FileSystemFileHandle | null,
        private readonly file: Blob | null,
        userConsentRequest: GetFilePermissionsRequest | null,
    )
    {
        if (!this.fileHandle && !this.file)
            throw new Error('No file or file handle provided');
        this.userConsentRequest = userConsentRequest;
        this.whenUserConsentGrantedSource = new PromiseSource<boolean>();
        if (this.file) {
            this.resolvedFile = this.file;
            this.userConsentGranted = true;
            this.whenUserConsentGrantedSource.resolve(true);
        }
        else if (userConsentRequest) {
            userConsentRequest.granted
                .then(async x => {
                    if (x)
                        this.resolvedFile = this.file ?? await this.fileHandle!.getFile();
                    this.userConsentGranted = x;
                    this.whenUserConsentGrantedSource.resolve(x);
                })
                .catch(() => {
                    this.userConsentGranted = false;
                    this.whenUserConsentGrantedSource.resolve(false);
                });
        }
        else
            throw new Error('No file and no whenUserConsentGrantedPromise');
    }

    public whenUserConsentGranted() : Promise<boolean>
    {
        return this.whenUserConsentGrantedSource;
    }

    public createPreviewUrl() : string
    {
        this.previewUrl ??= URL.createObjectURL(this.getBlob());
        return this.previewUrl;
    }

    public async saveFileHandleToDb() : Promise<string>
    {
        if (!this.fileHandle)
            return '';

        if (this.fileHandleDbKey.length > 0)
            return this.fileHandleDbKey;

        const fileHandleDbKey = uuidv4();
        await saveFileHandle(fileHandleDbKey, this.fileHandle);
        this.fileHandleDbKey = fileHandleDbKey;
        return fileHandleDbKey;
    }

    public getBlob(): Blob
    {
        if (!this.userConsentGranted)
            throw new Error('User consent not granted yet');
        return this.resolvedFile!;
    }

    public async clearForRemoving() : Promise<void>
    {
        this.revokePreviewUrl();
        if (this.userConsentRequest)
            this.userConsentRequest.cancel();
        await this.removeFileHandleFromDb();
    }

    private async removeFileHandleFromDb(): Promise<boolean>
    {
        if (this.fileHandleDbKey.length == 0)
            return false;

        await deleteFileHandle(this.fileHandleDbKey);
        this.fileHandleDbKey = '';
        return true;
    }

    private revokePreviewUrl() : void
    {
        if (!this.previewUrl)
            return;
        URL.revokeObjectURL(this.previewUrl);
        this.previewUrl = null;
    }
}
