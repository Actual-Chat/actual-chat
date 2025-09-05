import { deleteFileHandle, getFileHandle } from './file-handle-storage';
import { grantFileUploadPermissionsInvoker, requestFileHandlePermission } from './file-handle-permissions';
import { OperationCancelledError } from 'promises';
import { ChatMediaFileUpload } from './attachment-list';
import { Log } from 'logging';

const { debugLog, errorLog } = Log.get('WebFileProvider');

export class WebFileProviders
{
    public static async tryCreateFromFileHandleDbKey(fileHandleDbKey : string, chatId : string, blazorRef: DotNet.DotNetObject) : Promise<(WebFileProvider | undefined)> {
        const fileHandle = await getFileHandle(fileHandleDbKey);
        if (fileHandle == null) {
            await deleteFileHandle(fileHandleDbKey);
            return undefined;
        }

        const granted = await requestFileHandlePermission(fileHandle, "read");
        if (!granted) {
            return undefined;
        }
        const reporter = new FileUploadProgressReporter(blazorRef, 0);
        const file = await fileHandle.getFile();
        return new WebFileProvider(fileHandleDbKey, fileHandle, file, chatId, reporter);
    }

    public static grantFileUploadPermissions() {
        void grantFileUploadPermissionsInvoker.invoke();
    }

    public static startMonitorGrantPermissionsRequests(blazorRef: DotNet.DotNetObject)
    {
        grantFileUploadPermissionsInvoker.hasCallbacksChanged.add(() => {
            const hasCallbacks = grantFileUploadPermissionsInvoker.hasCallbacks();
            void blazorRef.invokeMethodAsync("OnPendingRequestsHaveChanged", hasCallbacks);
        })
    }
}

class WebFileProvider {
    private readonly fileUpload: ChatMediaFileUpload;

    constructor(
        private readonly fileHandleDbKey: string,
        private readonly fileHandle: FileSystemFileHandle,
        private readonly file: File,
        private readonly chatId: string,
        private readonly reporter: FileUploadProgressReporter
    )
    {
        this.fileUpload = new ChatMediaFileUpload(chatId, file, file.name, pct => reporter.reportProgress(pct))
        this.fileUpload.whenCompleted.then(x => {
            void reporter.reportUploadSucceed(x.mediaId, x.thumbnailMediaId);
        }).catch(e => {
            if (!(e instanceof OperationCancelledError)) {
                errorLog?.log('Failed to upload file', e);
                void reporter.reportUploadFailed();
            }
        });
    }

    public start()
    {
        this.fileUpload.start();
    }

    public cancel()
    {
        this.fileUpload.cancel();
    }
}

class FileUploadProgressReporter {
    constructor(private blazorRef: DotNet.DotNetObject, private id: number)
    {
    }

    public async reportProgress(progressPercent: number) {
        return this.blazorRef.invokeMethodAsync('OnUploadProgress', this.id, Math.trunc(progressPercent));
    }

    public async reportUploadSucceed(mediaId: string, thumbnailMediaId?: string) {
        return this.blazorRef.invokeMethodAsync('OnUploadSucceed', this.id, mediaId, thumbnailMediaId);
    }

    public async reportUploadFailed() {
        return this.blazorRef.invokeMethodAsync('OnUploadFailed', this.id);
    }
}
