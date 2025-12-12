import { deleteFileHandle, getFileHandle, saveFileHandle } from './file-handle-storage';
import { grantFileUploadPermissionsInvoker, requestFileHandlePermission } from './file-handle-permissions';
import { Log } from 'logging';
import { delayAsync, OperationCancelledError, PromiseSource } from 'promises';
import { BrowserInit } from '../../../UI.Blazor/Services/BrowserInit/browser-init';
import { SessionTokens } from '../../../UI.Blazor/Services/Security/session-tokens';
import { v4 as uuidv4 } from 'uuid';
import { NullableJSObjectReference } from 'UI.Blazor/JSRuntime/nullable-js-object-reference';
import { AttachmentWebFilePickerRegistry } from '../../Components/ChatMessageEditor/attachment-web-file-picker';

const { debugLog, warnLog, errorLog } = Log.get('WebFileProvider');

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
            const provider = new WebFileProvider('', fileResult.fileHandle, file, file.name, null);
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

        const grantedReadPermissionPromise = requestFileHandlePermission(fileHandle, 'read');
        const provider = new WebFileProvider(fileHandleDbKey, fileHandle, null, fileHandle.name, grantedReadPermissionPromise);
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
    private userConsentGranted : boolean | null = null;
    private resolvedFile: Blob | null;
    private readonly whenUserConsentGrantedSource: PromiseSource<boolean>;

    constructor(
        private fileHandleDbKey: string,
        private readonly fileHandle: FileSystemFileHandle | null,
        private readonly file: Blob | null,
        fileName: string,
        whenUserConsentGrantedPromise: Promise<boolean> | null,
    )
    {
        if (!this.fileHandle && !this.file)
            throw new Error('No file or file handle provided');
        this.whenUserConsentGrantedSource = new PromiseSource<boolean>();
        if (this.file) {
            this.resolvedFile = this.file;
            this.userConsentGranted = true;
            this.whenUserConsentGrantedSource.resolve(true);
        }
        else if (whenUserConsentGrantedPromise) {
            whenUserConsentGrantedPromise
                .then(async x => {
                    if (x)
                        this.resolvedFile = this.file ?? await this.fileHandle!.getFile();
                    this.userConsentGranted = x;
                    this.whenUserConsentGrantedSource.resolve(x);
                })
                .catch(e => {
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
        if (!this.previewUrl)
            this.previewUrl = URL.createObjectURL(this.GetFile());
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
        this.fileUpload = new ChunkedFileUpload(uploadId, chunkSize, this.GetFile(), pct => reporter.reportProgress(pct));
        this.fileUpload.whenCompleted.then(x => {
            void reporter.reportUploadSucceed();
            this.fileUpload = null;
        }).catch(e => {
            if (e instanceof OperationCancelledError) {
                debugLog?.log(`File upload '${uploadId}' cancelled`);
                void reporter.reportUploadCancelled();
            }
            else if (e instanceof UploadNotFoundError) {
                errorLog?.log(`File upload '${uploadId}' not found`);
                void reporter.reportUploadNotFound();
            }
            else {
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

    public dispose()
    {
        this.cancel();
        this.revokePreviewUrl();
    }

    private GetFile() : Blob
    {
        if (!this.userConsentGranted)
            throw new Error('User consent not granted yet');
        return this.resolvedFile!;
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

    public async reportUploadCancelled() {
        return this.blazorRef.invokeMethodAsync('OnUploadCancelled');
    }

    public async reportUploadNotFound() {
        return this.blazorRef.invokeMethodAsync('OnUploadNotFound');
    }
}

class ChunkedFileUpload {
    private readonly whenCompletedSource: PromiseSource<void> = new PromiseSource<void>();
    private readonly uploadUrl: string;
    private readonly abortController: AbortController = new AbortController();

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
        this.abortController.abort();
    }

    private async startInternal() {
        let retryIndex = 0;
        const maxRetries = 3;
        let run = true;
        while (run) {
            run = false;
            try {
                let offset = await this.getOffset();
                debugLog?.log(`Starting upload of ${this.uploadId} at offset ${offset}`);
                const fileSize = this.blob.size;
                this.progressReporter((offset / fileSize) * 100);
                // Upload chunks
                while (offset < fileSize) {
                    const remainingBytes = fileSize - offset;
                    const currentChunkSize = Math.min(this.chunkSize, remainingBytes);
                    const newOffset = await this.uploadChunk(offset, currentChunkSize);
                    const expectedNewOffset = offset + currentChunkSize;
                    if (newOffset !== expectedNewOffset)
                        warnLog?.log(`Offset mismatch detected: ${expectedNewOffset} != ${newOffset}`);
                    offset = newOffset;
                    // Reset retry counter on successful chunk upload
                    retryIndex = 0;
                    this.progressReporter((offset / fileSize) * 100);
                }
                this.whenCompletedSource.resolve();
                return;
            } catch (error) {
                if (error instanceof OffsetConflictError) {
                    if (retryIndex >= maxRetries) {
                        this.whenCompletedSource.reject(error);
                        return;
                    }
                    warnLog?.log('Offset conflict detected. Retrying...');
                    retryIndex++;
                    run = true;
                    continue;
                }
                if (error instanceof UploadTransientFailure) {
                    if (retryIndex >= maxRetries) {
                        this.whenCompletedSource.reject(error);
                        return;
                    }
                    warnLog?.log('Upload transient failure. Retrying...');
                    await delayAsync(500);
                    retryIndex++;
                    run = true;
                    continue;
                }
                if (this.isCancelled()) {
                    this.whenCompletedSource.reject(new OperationCancelledError('File upload cancelled'));
                } else {
                    this.whenCompletedSource.reject(error);
                }
                return;
            }
        }
    }

    private async getOffset(): Promise<number>
    {
        const response = await fetch(this.uploadUrl, {
            method: 'HEAD',
            headers: {
                [SessionTokens.headerName]: SessionTokens.current,
                'Tus-Resumable' : '1.0.0',
            },
            signal: this.abortController.signal
        });
        if (!response.ok) {
            if (response.status == 404)
                throw new UploadNotFoundError(`Upload ${this.uploadId} not found`);
            if (response.status == 503)
                throw new UploadTransientFailure(`Upload transient failure`);
            throw new Error(`Failed to get upload status: ${response.statusText}`);
        }
        const header = response.headers.get('Upload-Offset');
        if (!header)
            throw new Error('Upload-Offset header not found in response');
        return parseInt(header, 10);
    }

    private async uploadChunk(offset: number, chunkSize: number): Promise<number>
    {
        const contentType = 'application/offset+octet-stream';
        const expectedNewOffset = offset + chunkSize;
        const chunk = this.blob.slice(offset, expectedNewOffset, contentType);
        const response = await fetch(this.uploadUrl, {
            method: 'PATCH',
            headers: {
                [SessionTokens.headerName]: SessionTokens.current,
                'Content-Type': contentType,
                'Upload-Offset': offset.toString(),
                'Tus-Resumable' : '1.0.0',
            },
            body: chunk,
            signal: this.abortController.signal
        });

        if (!response.ok) {
            if (response.status == 404)
                throw new UploadNotFoundError(`Upload ${this.uploadId} not found`);
            if (response.status == 503)
                throw new UploadTransientFailure(`Upload transient failure`);
            if (response.status == 409)
                throw new OffsetConflictError('Upload offset conflict');
            throw new Error(`Failed to upload chunk: ${response.statusText}`);
        }

        const newOffsetHeader = response.headers.get('Upload-Offset');
        if (!newOffsetHeader)
            throw new Error('Upload-Offset header not found in response');
        return parseInt(newOffsetHeader, 10);
    }

    private isCancelled() : boolean {
        return this.abortController.signal.aborted;
    }
}

class UploadNotFoundError extends Error {
    constructor(message: string) {
        super(message);
    }
}

class UploadTransientFailure extends Error {
    constructor(message: string) {
        super(message);
    }
}

class OffsetConflictError extends Error {
    constructor(message?: string) {
        super(message);
    }
}
