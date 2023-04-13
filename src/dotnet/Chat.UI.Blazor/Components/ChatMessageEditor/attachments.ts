import { TuneUI } from '../../../UI.Blazor/Services/TuneUI/tune-ui';
import { OperationCancelledError, PromiseSource } from 'promises';
import { Log } from 'logging';

const { errorLog } = Log.get('Attachments');

interface Attachment {
    File: File;
    Url: string;
    Id: number;
    MediaId: string;
}

interface MediaContent {
    mediaId: string;
    contentId: string;
}

type ProgressReporter = (progressPercent: number) => void;

export class Attachments {
    private attachments: Map<number, Attachment> = new Map<number, Attachment>();
    private uploads: Map<number, FileUpload> = new Map<number, FileUpload>();
    private attachmentsIdSeed: number = 0;

    // TODO: maybe attachments-ui.ts and singleton and initialize with own blazorRef
    public constructor(private readonly blazorRef: DotNet.DotNetObject) {
    }

    public async add(chatId: string, file: File): Promise<boolean> {
        const attachment: Attachment = {
            Id: this.attachmentsIdSeed,
            File: file,
            Url: '',
            MediaId: '',
        };
        if (file.type.startsWith('image'))
            attachment.Url = URL.createObjectURL(file);
        const isAdded = await this.invokeAttachmentAdded(attachment, file);
        if (!isAdded) {
            if (attachment.Url)
                URL.revokeObjectURL(attachment.Url);
        }
        else {
            this.attachmentsIdSeed++;
            this.attachments.set(attachment.Id, attachment);
            TuneUI.play('change-attachments');
            const upload = new FileUpload(chatId, file, pct => this.invokeUploadProgress(attachment.Id, pct))
            upload.whenCompleted.then(x => {
                attachment.MediaId = x.mediaId;
            }).catch(e => {
                if (!(e instanceof OperationCancelledError))
                    // TODO: handle failed upload
                    errorLog?.log('Failed to upload file', e);
            });
            upload.start();
            this.uploads.set(attachment.Id, upload);
        }
        return isAdded;
    }

    public getMediaIds() {
        return [...this.attachments.values()].filter(x => !!x.MediaId).map(x => x.MediaId);
    }

    public remove(id: number) {
        TuneUI.play('change-attachments');
        const upload = this.uploads.get(id);
        if (upload) {
            upload.cancel();
            this.uploads.delete(id);
        }

        const attachment = this.attachments.get(id);
        this.attachments.delete(id);
        if (attachment?.Url)
            URL.revokeObjectURL(attachment.Url);
    }

    public clear() {
        if (this.attachments.size != 0)
            TuneUI.play('change-attachments');
        for (const attachment of this.attachments.values()) {
            if (attachment?.Url)
                URL.revokeObjectURL(attachment.Url);
        }
        this.attachments.clear();
        this.attachmentsIdSeed = 0;
        this.uploads.forEach((upload, key) => upload.cancel());
        this.uploads.clear();
    }

    public any() {
        return this.attachments.size > 0
    }

    private async invokeAttachmentAdded(attachment: Attachment, file: File) {
        return this.blazorRef.invokeMethodAsync<boolean>(
            'OnAttachmentAdded', attachment.Id, attachment.Url, file.name, file.type, file.size);
    }

    private async invokeUploadProgress(id: number, progressPercent: number) {
        return  this.blazorRef.invokeMethodAsync('OnUploadProgress', id, Math.trunc(progressPercent));
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
        const url = this.getUrl(`api/chats/${this.chatId}/upload-picture`);
        this.xhr.open('post', url, true);
        this.xhr.send(formData);
    }

    public cancel() {
        this.isCancelled = true;
        this.xhr.abort();
    }

    private getUrl(url: string) {
        // @ts-ignore
        const baseUri = window.App.baseUri; // Web API base URI when running in MAUI
        return baseUri ? new URL(url, baseUri).toString() : url;
    }
}
