import { Tune, TuneUI } from '../../../UI.Blazor/Services/TuneUI/tune-ui';
import { Log } from 'logging';
import { isSupportedImage, isSupportedVideo } from "media-types";
import { fromEvent, Subject, takeUntil } from 'rxjs';
import { WebFileProvider } from './web-file-providers';


const { debugLog, errorLog } = Log.get('Attachments');

interface Attachment {
    chatId: string;
    fileBlob: Blob;
    fileName: string;
    fileHandle: FileSystemFileHandle | null;
    url: string;
    tempUrl: string;
    id: number;
    mediaId: string;
    thumbnailMediaId?: string;
}

function hasShowOpenFilePicker(
    win: Window
): win is Window & { showOpenFilePicker: (options?: OpenFilePickerOptions) => Promise<FileSystemFileHandle[]> } {
    return "showOpenFilePicker" in win;
}

export class AttachmentListView {
    private readonly disposed$: Subject<void> = new Subject<void>();
    private attachments: AttachmentList;
    private chatId: string;
    public changed: () => void = () => { };

    public static create(inputElement: HTMLInputElement) {
        return new AttachmentListView(inputElement);
    }

    public constructor(private readonly filePickerElement: HTMLInputElement) {
        this.attachments = new AttachmentList();
        fromEvent(this.filePickerElement, 'change').pipe(takeUntil(this.disposed$)).subscribe(this.onFilePickerChange);
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    public setChatId(chatId: string) {
        this.chatId = chatId;
    }

    /** Called by Blazor */
    public attachList(blazorRef: DotNet.DotNetObject)
    {
        if (this.attachments.isAttached())
            this.attachments = new AttachmentList();
        this.attachments.attach(blazorRef);
        return this.attachments;
    }

    /** Called by Blazor */
    public showFilePicker = async (acceptTypes: string = "") => {
        TuneUI.play(Tune.ChangeAttachments);
        // NOTE: acceptTypes is not empty on an Android platform only. Let's implement it later.
        if (acceptTypes === "" && hasShowOpenFilePicker(window)) {
            const files = await window.showOpenFilePicker({
                multiple: true,
            });
            await this.onFilesPicked(files);
        }
        else {
            this.filePickerElement.accept = acceptTypes;
            this.filePickerElement.click();
        }
    };

    /** Called by Blazor */
    public async addBlobs(urls: string[], fileNames: string[]): Promise<number> {
        let addedBlobs = 0;
        for (let i = 0; i < urls.length; i++){
            const url = urls[i];
            const fileName = fileNames[i];
            await fetch(url)
                .then(r => r.blob())
                .then(blob => this.attachments.addBlob(this.chatId, url, blob, fileName, null, true))
                .then(isAdded => {
                    if (isAdded) {
                        addedBlobs++;
                        debugLog?.log(`added a blob: ${url}`);
                    }
                })
                .catch(e => errorLog?.log('failed to add a blob', e))
        }
        this.changed();
        return addedBlobs;
    }

    public some() {
        return this.attachments.some();
    }

    public async add(chatId: string, file: File, fileHandle: FileSystemFileHandle | null): Promise<boolean> {
        return this.attachments.addBlob(chatId, '', file, file.name, fileHandle, false);
    }

    private onFilePickerChange = (async (event: Event & { target: Element; }) => {
        for (const file of this.filePickerElement.files ?? []) {
            const isAdded = await this.add(this.chatId, file, null);
            if (!isAdded)
                break;

            this.changed();
        }
        this.filePickerElement.value = '';
    });

    private async onFilesPicked(fileHandles : FileSystemFileHandle[])
    {
        console.log(fileHandles);
        for (const fileHandle of fileHandles) {
            const file = await fileHandle.getFile();
           const isAdded = await this.add(this.chatId, file, fileHandle);
            if (!isAdded)
                break;
        }
    }
}

class AttachmentList {
    private readonly disposed$: Subject<void> = new Subject<void>();
    private attachments: Map<number, Attachment> = new Map<number, Attachment>();
    private uploads: Map<number, WebFileProvider> = new Map<number, WebFileProvider>();
    private attachmentsIdSeed: number = 0;
    private blazorRef: DotNet.DotNetObject | null = null;
    public changed: () => void = () => { };
    private get BlazorRef() {
        if (this.blazorRef == null)
            throw new Error('BlazorRef is not set');
        return this.blazorRef;
    }

    public isAttached() {
        return this.blazorRef != null;
    }

    public constructor() {}

    public attach(blazorRef: DotNet.DotNetObject)
    {
        if (this.blazorRef != null)
            throw new Error('Already attached');

        this.blazorRef = blazorRef;
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    public async addBlob(chatId: string, url: string, blob: Blob, fileName: string, fileHandle : FileSystemFileHandle | null, silent : boolean): Promise<boolean> {
        const attachment: Attachment = {
            id: this.attachmentsIdSeed,
            chatId: chatId,
            fileBlob: blob,
            fileName: fileName,
            fileHandle: fileHandle,
            url : url,
            tempUrl: '',
            mediaId: '',
        };
        if (!url && (isSupportedImage(blob.type) || isSupportedVideo(blob.type)))
            attachment.url = attachment.tempUrl = URL.createObjectURL(blob);
        const isAdded = await this.invokeAttachmentAdded(attachment);
        if (!isAdded) {
            if (attachment.tempUrl)
                URL.revokeObjectURL(attachment.tempUrl);
            return false;
        }

        this.attachmentsIdSeed++;
        this.attachments.set(attachment.id, attachment);
        if (!silent)
            TuneUI.play(Tune.ChangeAttachments);

        try {
            await this.invokeCreateUploaderRequested(attachment);
            return true;
        }
        catch (e) {
            await this.remove(attachment.id);
            return false;
        }
    }

    /** Called by Blazor */
    public createFileProvider(id: number, blazorRef: DotNet.DotNetObject): WebFileProvider {
        debugLog?.log(`createFileProvider: ${id}`);
        const upload1 = this.uploads.get(id);
        if (upload1)
            throw new Error('Already created');
        const attachment = this.attachments.get(id);
        if (!attachment)
            throw new Error('Attachment not found');

        const provider = new WebFileProvider('', attachment.fileHandle, attachment.fileBlob, attachment.fileName, attachment.chatId, blazorRef);
        this.uploads.set(attachment.id, provider);
        return provider;
    }

    /** Called by Blazor */
    public async remove(id: number) {
        TuneUI.play(Tune.ChangeAttachments);
        const upload = this.uploads.get(id);
        if (upload) {
            upload.cancel();
            await upload.removeFileHandleFromDb();
            this.uploads.delete(id);
        }

        const attachment = this.attachments.get(id);
        this.attachments.delete(id);
        if (attachment) {
            if (attachment?.tempUrl)
                URL.revokeObjectURL(attachment.tempUrl);
        }

        this.changed();
    }

    /** Called by Blazor */
    public async clear() {
        if (this.attachments.size != 0)
            TuneUI.play(Tune.ChangeAttachments);
        for (const attachment of this.attachments.values()) {
            if (attachment?.tempUrl)
                URL.revokeObjectURL(attachment.tempUrl);
        }
        this.attachments.clear();
        this.attachmentsIdSeed = 0;
        for (const upload of this.uploads.values()) {
            upload.cancel();
            await upload.removeFileHandleFromDb();
        }
        this.uploads.clear();
        this.changed();
    }

    public some() {
        return this.attachments.size > 0
    }

    private async invokeAttachmentAdded(attachment: Attachment) {
        const blob = attachment.fileBlob;
        return this.BlazorRef.invokeMethodAsync<boolean>(
            'OnAttachmentAdded', attachment.id, attachment.url, attachment.fileName, blob.type, blob.size);
    }

    private async invokeCreateUploaderRequested(attachment: Attachment) {
        return this.BlazorRef.invokeMethodAsync<boolean>(
            'OnCreateUploaderRequested', attachment.id, attachment.chatId);
    }
}
