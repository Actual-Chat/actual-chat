import { deleteFileHandle, getFileHandle, saveFileHandle } from './file-handle-storage';
import { grantFileUploadPermissionsInvoker, requestFileHandlePermission } from './file-handle-permissions';
import { Log } from 'logging';
import { OperationCancelledError, PromiseSource } from 'promises';
import { BrowserInit } from '../../../UI.Blazor/Services/BrowserInit/browser-init';
import { SessionTokens } from '../../../UI.Blazor/Services/Security/session-tokens';
import { v4 as uuidv4 } from 'uuid';
import { NullableJSObjectReference } from 'UI.Blazor/JSRuntime/nullable-js-object-reference';
import { AttachmentWebFilePickerRegistry } from '../../Components/ChatMessageEditor/attachment-web-file-picker';

const { debugLog, errorLog } = Log.get('WebFileProvider');

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
    private fileUpload: ChunkedFileUpload | null;

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

    public start(uploadId: string, chunkSize: number, blazorRef: DotNet.DotNetObject)
    {
        if (this.fileUpload)
            throw new Error('File upload already started');

        const reporter = new FileUploadProgressReporter(blazorRef);
        this.fileUpload = new ChunkedFileUpload(uploadId, chunkSize, this.file, pct => reporter.reportProgress(pct));
        this.fileUpload.whenCompleted.then(x => {
            void reporter.reportUploadSucceed();
            this.fileUpload = null;
        }).catch(e => {
            if (!(e instanceof OperationCancelledError)) {
                errorLog?.log('Failed to upload file', e);
                void reporter.reportUploadFailed();
            }
            this.fileUpload = null;
        });
        this.fileUpload.start();
    }

    public cancel()
    {
        if (!this.fileUpload)
            return;

        this.fileUpload.cancel();
        this.fileUpload = null;
    }
}

export class FileUploadProgressReporter {
    constructor(private blazorRef: DotNet.DotNetObject)
    {
    }

    public async reportProgress(progressPercent: number) {
        return this.blazorRef.invokeMethodAsync('OnUploadProgress', Math.trunc(progressPercent));
    }

    public async reportUploadSucceed() {
        return this.blazorRef.invokeMethodAsync('OnUploadSucceed');
    }

    public async reportUploadFailed() {
        return this.blazorRef.invokeMethodAsync('OnUploadFailed');
    }
}

class ChunkedFileUpload {
    private readonly whenCompletedSource: PromiseSource<void> = new PromiseSource<void>();
    private readonly uploadUrl: string;
    private readonly abortController: AbortController = new AbortController();
    private isCancelled = false;

    constructor(
        private readonly uploadId: string,
        private readonly chunkSize: number,
        private readonly blob: Blob,
        private readonly progressReporter: ProgressReporter)
    {
        this.uploadUrl = BrowserInit.getUrl(`api/uploads/${this.uploadId}`);
    }

    public get whenCompleted(): Promise<void> {
        return this.whenCompletedSource;
    }

    public start()
    {
        void this.startInternal();
    }

    public cancel() {
        this.isCancelled = true;
        this.abortController.abort();
    }

    private async startInternal() {
        try {
            let offset = await this.getOffset();
            debugLog?.log(`Starting upload of ${this.uploadId} at offset ${offset}`);
            const fileSize = this.blob.size;

            // Upload chunks
            while (offset < fileSize) {
                if (this.isCancelled) {
                    this.whenCompletedSource.reject(new OperationCancelledError('File upload cancelled'));
                    return;
                }

                const remainingBytes = fileSize - offset;
                const currentChunkSize = Math.min(this.chunkSize, remainingBytes);

                offset = await this.uploadChunk(offset, currentChunkSize);

                const uploadProgress = (offset / fileSize) * 100;
                this.progressReporter(uploadProgress);
            }

            this.whenCompletedSource.resolve();
        } catch (error) {
            if (!this.isCancelled) {
                this.whenCompletedSource.reject(error);
            }
        }
    }

    private async getOffset(): Promise<number>
    {
        const response = await fetch(this.uploadUrl, {
            method: 'HEAD',
            headers: {
                [SessionTokens.headerName]: SessionTokens.current
            },
            signal: this.abortController.signal
        });
        if (!response.ok)
            throw new Error(`Failed to get upload status: ${response.statusText}`);
        const header = response.headers.get('Upload-Offset');
        if (!header)
            throw new Error('Upload-Offset header not found in response');
        return parseInt(header, 10);
    }

    private async uploadChunk(offset: number, chunkSize: number): Promise<number>
    {
        const contentType = 'application/offset+octet-stream';
        const chunk = this.blob.slice(offset, offset + chunkSize, contentType);
        const response = await fetch(this.uploadUrl, {
            method: 'PATCH',
            headers: {
                [SessionTokens.headerName]: SessionTokens.current,
                'Content-Type': contentType,
                'Upload-Offset': offset.toString(),
            },
            body: chunk,
            signal: this.abortController.signal
        });

        if (!response.ok)
            throw new Error(`Failed to upload chunk: ${response.statusText}`);

        const newOffsetHeader = response.headers.get('Upload-Offset');
        if (!newOffsetHeader)
            throw new Error('Upload-Offset header not found in response');

        return parseInt(newOffsetHeader, 10);
    }
}
