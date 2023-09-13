import { Tune, TuneUI } from '../../../UI.Blazor/Services/TuneUI/tune-ui';
import { OperationCancelledError, PromiseSource } from 'promises';
import { Log } from 'logging';
import { fromEvent, Subject, takeUntil } from 'rxjs';
import { BrowserInit } from '../../../UI.Blazor/Services/BrowserInit/browser-init';
import { SessionTokens } from '../../../UI.Blazor/Services/Security/session-tokens';

const { debugLog, errorLog } = Log.get('Attachments');

interface Attachment {
    fileBlob: Blob;
    fileName: string;
    url: string;
    tempUrl: string;
    id: number;
    mediaId: string;
}

interface MediaContent {
    mediaId: string;
    contentId: string;
}

type ProgressReporter = (progressPercent: number) => void;

export class AttachmentList {
    private readonly disposed$: Subject<void> = new Subject<void>();
    private attachments: Map<number, Attachment> = new Map<number, Attachment>();
    private uploads: Map<number, FileUpload> = new Map<number, FileUpload>();
    private attachmentsIdSeed: number = 0;
    private chatId: string;
    public changed: () => void = () => { };

    public static create(blazorRef: DotNet.DotNetObject, inputElement: HTMLInputElement) {
        return new AttachmentList(blazorRef, inputElement);
    }

    public constructor(private readonly blazorRef: DotNet.DotNetObject, private readonly filePickerElement: HTMLInputElement) {
        fromEvent(this.filePickerElement, 'change').pipe(takeUntil(this.disposed$)).subscribe(this.onFilePickerChange);
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    public async addBlobs(urls: string[], fileNames: string[]): Promise<number> {
        let addedBlobs = 0;
        for (let i = 0; i < urls.length; i++){
            const url = urls[i];
            const fileName = fileNames[i];
            await fetch(url)
                .then(r => r.blob())
                .then(blob => this.addBlob(this.chatId, url, blob, fileName, true))
                .then(isAdded => {
                    if (isAdded) {
                        addedBlobs++;
                        debugLog.log(`added a blob: ${url}`);
                    }
                })
                .catch(e => errorLog.log('failed to add a blob', e))
        }
        this.changed();
        return addedBlobs;
    }

    public async add(chatId: string, file: File): Promise<boolean> {
        return this.addBlob(chatId, '', file, file.name, false);
    }

    public async addBlob(chatId: string, url: string, blob: Blob, fileName: string, silent : boolean): Promise<boolean> {
        const attachment: Attachment = {
            id: this.attachmentsIdSeed,
            fileBlob: blob,
            fileName: fileName,
            url : url,
            tempUrl: '',
            mediaId: '',
        };
        if (!url && (blob.type.startsWith('image') || blob.type.startsWith('video')))
            attachment.url = attachment.tempUrl = URL.createObjectURL(blob);
        const isAdded = await this.invokeAttachmentAdded(attachment, blob, fileName);
        if (!isAdded) {
            if (attachment.tempUrl)
                URL.revokeObjectURL(attachment.tempUrl);
        }
        else {
            this.attachmentsIdSeed++;
            this.attachments.set(attachment.id, attachment);
            if (!silent)
                TuneUI.play(Tune.ChangeAttachments);
            const upload = new FileUpload(chatId, blob, fileName, pct => this.invokeUploadProgress(attachment.id, pct))
            upload.whenCompleted.then(x => {
                attachment.mediaId = x.mediaId;
                this.invokeUploadSucceed(attachment.id, x.mediaId);
            }).catch(e => {
                if (!(e instanceof OperationCancelledError)) {
                    errorLog?.log('Failed to upload file', e);
                    this.invokeUploadFailed(attachment.id);
                }
            });
            upload.start();
            this.uploads.set(attachment.id, upload);
        }
        return isAdded;
    }

    public setChatId(chatId: string) {
        this.chatId = chatId;
    }

    /** Called by Blazor */
    public remove(id: number) {
        TuneUI.play(Tune.ChangeAttachments);
        const upload = this.uploads.get(id);
        if (upload) {
            upload.cancel();
            this.uploads.delete(id);
        }

        const attachment = this.attachments.get(id);
        this.attachments.delete(id);
        if (attachment?.tempUrl)
            URL.revokeObjectURL(attachment.tempUrl);

        this.changed();
    }

    /** Called by Blazor */
    public showFilePicker = () => {
        TuneUI.play(Tune.ChangeAttachments);
        this.filePickerElement.click();
    };

    /** Called by Blazor */
    public clear() {
        if (this.attachments.size != 0)
            TuneUI.play(Tune.ChangeAttachments);
        for (const attachment of this.attachments.values()) {
            if (attachment?.tempUrl)
                URL.revokeObjectURL(attachment.tempUrl);
        }
        this.attachments.clear();
        this.attachmentsIdSeed = 0;
        this.uploads.forEach((upload, key) => upload.cancel());
        this.uploads.clear();
        this.changed();
    }

    public some() {
        return this.attachments.size > 0
    }

    private onFilePickerChange = (async (event: Event & { target: Element; }) => {
        for (const file of this.filePickerElement.files) {
            const isAdded = await this.add(this.chatId, file);
            if (!isAdded)
                break;

            this.changed();
        }
        this.filePickerElement.value = '';
    });

    private async invokeAttachmentAdded(attachment: Attachment, blob: Blob, fileName: string) {
        return this.blazorRef.invokeMethodAsync<boolean>(
            'OnAttachmentAdded', attachment.id, attachment.url, fileName, blob.type, blob.size);
    }

    private async invokeUploadProgress(id: number, progressPercent: number) {
        return  this.blazorRef.invokeMethodAsync('OnUploadProgress', id, Math.trunc(progressPercent));
    }

    private async invokeUploadSucceed(id: number, mediaId: string) {
        return  this.blazorRef.invokeMethodAsync('OnUploadSucceed', id, mediaId);
    }

    private async invokeUploadFailed(id: number) {
        return  this.blazorRef.invokeMethodAsync('OnUploadFailed', id);
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
            this.fileName = "upload";
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
        const url = this.getUrl(`api/chat-media/${this.chatId}/upload`);
        this.xhr.open('post', url, true);
        this.xhr.setRequestHeader(SessionTokens.headerName, SessionTokens.current);
        this.xhr.send(formData);
    }

    public cancel() {
        this.isCancelled = true;
        this.xhr.abort();
    }

    private getUrl(url: string) {
        // @ts-ignore
        const baseUri = BrowserInit.baseUri;
        return baseUri ? new URL(url, baseUri).toString() : url;
    }
}
