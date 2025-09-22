import { deleteFileHandle, getFileHandle, saveFileHandle } from './file-handle-storage';
import { grantFileUploadPermissionsInvoker, requestFileHandlePermission } from './file-handle-permissions';
import { Log } from 'logging';
import { OperationCancelledError, PromiseSource } from 'promises';
import { BrowserInit } from '../../../UI.Blazor/Services/BrowserInit/browser-init';
import { SessionTokens } from '../../../UI.Blazor/Services/Security/session-tokens';
import { v4 as uuidv4 } from 'uuid';
import { NullableJSObjectReference } from 'UI.Blazor/JSRuntime/nullable-js-object-reference';
import { AttachmentWebFilePickerRegistry } from '../../Components/ChatMessageEditor/attachment-web-file-picker';

const { errorLog } = Log.get('WebFileProvider');

interface MediaContent {
    mediaId: string;
    contentId: string;
    thumbnailMediaId?: string;
    thumbnailContentId?: string;
}

type ProgressReporter = (progressPercent: number) => void;

interface CreateWebFileProviderResult {
    previewUrl: string;
    fileProvider : any;
}

export class WebFileProviders
{
    public static createFromFileId(fileId : number) : CreateWebFileProviderResult | null
    {
        const fileResult = AttachmentWebFilePickerRegistry.Get(fileId);
        if (!fileResult)
            return null;

        let previewUrl = "";
        try {
            const file = fileResult.file;
            const provider = new WebFileProvider('', fileResult.fileHandle, file, file.name);
            previewUrl = provider.createPreviewUrl();
            // @ts-ignore
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

        const granted = await requestFileHandlePermission(fileHandle, 'read');
        if (!granted) {
            return NullableJSObjectReference.create(null);
        }
        const file = await fileHandle.getFile();
        const provider = new WebFileProvider(fileHandleDbKey, fileHandle, file, file.name);
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


export class WebFileProvider {
    private previewUrl: string | null = null;
    private fileUpload: FileUpload | null;

    constructor(
        private fileHandleDbKey: string,
        private readonly fileHandle: FileSystemFileHandle | null,
        private readonly file: Blob,
        private readonly fileName: string,
    )
    {
    }

    public createPreviewUrl() : string
    {
        if (!this.previewUrl)
            this.previewUrl = URL.createObjectURL(this.file);
        return this.previewUrl;
    }

    public revokePreviewUrl() : void
    {
        if (!this.previewUrl)
            return;
        URL.revokeObjectURL(this.previewUrl);
        this.previewUrl = null;
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

    public async removeFileHandleFromDb(): Promise<boolean>
    {
        if (this.fileHandleDbKey.length == 0)
            return false;

        await deleteFileHandle(this.fileHandleDbKey);
        this.fileHandleDbKey = '';
        return true;
    }

    public start(chatId: string, blazorRef: DotNet.DotNetObject)
    {
        if (this.fileUpload) {
            throw new Error('File upload already started');
        }
        const reporter = new FileUploadProgressReporter(blazorRef);
        this.fileUpload = new FileUpload(chatId, this.file, this.fileName, pct => reporter.reportProgress(pct));
        this.fileUpload.whenCompleted.then(x => {
            void reporter.reportUploadSucceed(x.mediaId, x.thumbnailMediaId);
        }).catch(e => {
            if (!(e instanceof OperationCancelledError)) {
                errorLog?.log('Failed to upload file', e);
                void reporter.reportUploadFailed();
            }
        });
        this.fileUpload.start();
    }

    public cancel()
    {
        if (!this.fileUpload)
            return;

        this.fileUpload.cancel();
    }
}

export class FileUploadProgressReporter {
    constructor(private blazorRef: DotNet.DotNetObject)
    {
    }

    public async reportProgress(progressPercent: number) {
        return this.blazorRef.invokeMethodAsync('OnUploadProgress', Math.trunc(progressPercent));
    }

    public async reportUploadSucceed(mediaId: string, thumbnailMediaId?: string) {
        return this.blazorRef.invokeMethodAsync('OnUploadSucceed', mediaId, thumbnailMediaId);
    }

    public async reportUploadFailed() {
        return this.blazorRef.invokeMethodAsync('OnUploadFailed');
    }
}

class FileUpload {
    private readonly xhr: XMLHttpRequest;
    private readonly whenCompletedSource: PromiseSource<MediaContent> = new PromiseSource<MediaContent>();
    private isCancelled = false;

    constructor(
        private readonly chatId: string,
        private readonly blob: Blob,
        private readonly fileName: string,
        private readonly progressReporter: ProgressReporter) {
        this.xhr = new XMLHttpRequest();
        if (!this.fileName)
            this.fileName = 'upload';
    }

    public get whenCompleted(): Promise<MediaContent> {
        return this.whenCompletedSource;
    }

    public start() {
        const formData = new FormData();
        formData.append('file', this.blob, this.fileName);
        this.xhr.upload.onprogress = (e) => {
            const progress = Math.floor(e.loaded / e.total * 1000) / 10;
            this.progressReporter(progress);
        };
        this.xhr.onreadystatechange = () => {
            if (this.xhr.readyState === XMLHttpRequest.DONE) {
                if (this.xhr.status === 200) {
                    this.whenCompletedSource.resolve(JSON.parse(this.xhr.response));
                } else if (this.isCancelled)
                    this.whenCompletedSource.reject(new OperationCancelledError('File upload cancelled: ' + this.xhr.statusText));
                else
                    this.whenCompletedSource.reject(this.xhr.responseText);
            }
        };
        const url = BrowserInit.getUrl(`api/chat-media/${this.chatId}/upload`);
        this.xhr.open('post', url, true);
        this.xhr.setRequestHeader(SessionTokens.headerName, SessionTokens.current);
        this.xhr.send(formData);
    }

    public cancel() {
        this.isCancelled = true;
        this.xhr.abort();
    }
}
