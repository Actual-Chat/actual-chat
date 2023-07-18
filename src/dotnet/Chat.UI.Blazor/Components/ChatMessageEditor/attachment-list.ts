import { TuneUI } from '../../../UI.Blazor/Services/TuneUI/tune-ui';
import { OperationCancelledError, PromiseSource } from 'promises';
import { Log } from 'logging';
import { fromEvent, Subject, takeUntil } from 'rxjs';
import {BrowserInit} from "../../../UI.Blazor/Services/BrowserInit/browser-init";

const { errorLog } = Log.get('Attachments');

interface Attachment {
    file: File;
    url: string;
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

    public async add(chatId: string, file: File): Promise<boolean> {
        const attachment: Attachment = {
            id: this.attachmentsIdSeed,
            file: file,
            url: '',
            mediaId: '',
        };
        if (file.type.startsWith('image') || file.type.startsWith('video'))
            attachment.url = URL.createObjectURL(file);
        const isAdded = await this.invokeAttachmentAdded(attachment, file);
        if (!isAdded) {
            if (attachment.url)
                URL.revokeObjectURL(attachment.url);
        }
        else {
            this.attachmentsIdSeed++;
            this.attachments.set(attachment.id, attachment);
            TuneUI.play('change-attachments');
            const upload = new FileUpload(chatId, file, pct => this.invokeUploadProgress(attachment.id, pct))
            upload.whenCompleted.then(x => {
                attachment.mediaId = x.mediaId;
                this.invokeUploadSucceed(attachment.id, x.mediaId);
            }).catch(e => {
                if (!(e instanceof OperationCancelledError))
                    errorLog?.log('Failed to upload file', e);
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
        TuneUI.play('change-attachments');
        const upload = this.uploads.get(id);
        if (upload) {
            upload.cancel();
            this.uploads.delete(id);
        }

        const attachment = this.attachments.get(id);
        this.attachments.delete(id);
        if (attachment?.url)
            URL.revokeObjectURL(attachment.url);

        this.changed();
    }

    /** Called by Blazor */
    public showFilePicker = () => {
        TuneUI.play('change-attachments');
        this.filePickerElement.click();
    };

    /** Called by Blazor */
    public clear() {
        if (this.attachments.size != 0)
            TuneUI.play('change-attachments');
        for (const attachment of this.attachments.values()) {
            if (attachment?.url)
                URL.revokeObjectURL(attachment.url);
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

    private async invokeAttachmentAdded(attachment: Attachment, file: File) {
        return this.blazorRef.invokeMethodAsync<boolean>(
            'OnAttachmentAdded', attachment.id, attachment.url, file.name, file.type, file.size);
    }

    private async invokeUploadProgress(id: number, progressPercent: number) {
        return  this.blazorRef.invokeMethodAsync('OnUploadProgress', id, Math.trunc(progressPercent));
    }

    private async invokeUploadSucceed(id: number, mediaId: string) {
        return  this.blazorRef.invokeMethodAsync('OnUploadSucceed', id, mediaId);
    }
}

class FileUpload {
    private readonly xhr: XMLHttpRequest;
    private readonly whenCompletedSource: PromiseSource<MediaContent> = new PromiseSource<MediaContent>();
    private isCancelled = false;

    constructor(
        private readonly chatId: string,
        private readonly file: File,
        private readonly progressReporter: ProgressReporter) {
        this.xhr = new XMLHttpRequest();
    }

    public get whenCompleted(): Promise<MediaContent> {
        return this.whenCompletedSource;
    }

    public start() {
        const formData = new FormData();
        formData.append('file', this.file, this.file.name);
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
                    this.whenCompletedSource.reject(this.xhr.statusText);
            }
        };
        const url = this.getUrl(`api/chat-media/${this.chatId}/upload`);
        this.xhr.open('post', url, true);
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
