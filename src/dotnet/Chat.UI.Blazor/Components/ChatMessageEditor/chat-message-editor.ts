import './chat-message-editor.css';
import { SlateEditorHandle } from '../SlateEditor/slate-editor-handle';

const LogScope: string = 'MessageEditor';

export class ChatMessageEditor {
    private blazorRef: DotNet.DotNetObject;
    private editorDiv: HTMLDivElement;
    private input: HTMLDivElement;
    private filesPicker: HTMLInputElement;
    private postButton: HTMLButtonElement;
    private recorderButtonDiv: HTMLDivElement;
    private recordButton: HTMLButtonElement;
    private recordButtonObserver : MutationObserver;
    private isTextMode: boolean = false;
    private isRecording: boolean = false;
    private attachmentsIdSeed: number = 0;
    private attachments: Map<number, Attachment> = new Map<number, Attachment>();
    private audioButtons: HTMLDivElement;

    static create(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject): ChatMessageEditor {
        return new ChatMessageEditor(editorDiv, blazorRef);
    }

    constructor(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this.editorDiv = editorDiv;
        this.input = this.editorDiv.querySelector('div.message-input');
        this.filesPicker = this.editorDiv.querySelector('input.files-picker');
        this.postButton = this.editorDiv.querySelector('.post-message');
        this.blazorRef = blazorRef;
        this.recorderButtonDiv = this.editorDiv.querySelector('div.recorder-button');
        this.recordButton = this.recorderButtonDiv.querySelector('button');
        this.audioButtons = this.editorDiv.querySelector('.recorder-buttons');

        // Wiring up event listeners
        this.input.addEventListener('paste', this.inputPasteListener);
        this.filesPicker.addEventListener("change", this.filesPickerChangeListener);
        this.postButton.addEventListener('click', this.postClickListener);
        this.recordButtonObserver = new MutationObserver(this.syncLanguageButtonVisibility);
        const recordButtonObserverConfig = {
            attributes: true,
            childList: false,
            subtree: false
        };
        this.recordButtonObserver.observe(this.recordButton, recordButtonObserverConfig);
        this.changeMode();
    }

    private inputPasteListener = ((event: ClipboardEvent & { target: Element; }) => {
        // Get pasted data via clipboard API
        // We need to handle only files pasting.
        // Text pasting is controlled by slate editor.
        const clipboardData = event.clipboardData;
        for (const item of clipboardData.items) {
            if (item.kind === 'file') {
                const file = item.getAsFile();
                void this.addAttachment(file);
                event.preventDefault();
            }
        }
    })

    private filesPickerChangeListener = (async (event: Event & { target: Element; }) => {
        for (const file of this.filesPicker.files)
            await this.addAttachment(file);
        this.filesPicker.value = '';
    })

    private postClickListener = ((event: MouseEvent & { target: Element; }) => {
        const input = this.input.querySelector('[role="textbox"]') as HTMLDivElement;
        input.focus();
        this.changeMode();
    })

    private syncLanguageButtonVisibility = () => {
        const isRecording = this.recordButton.classList.contains('on');
        if (this.isRecording === isRecording)
            return;
        this.isRecording = isRecording;
        if (isRecording) {
            this.editorDiv.classList.add('record-mode');
        } else {
            this.editorDiv.classList.remove('record-mode');
        }
    }

    private changeMode() {
        const text = this.getText();
        const isTextMode = text != "" || this.attachments.size > 0;
        if (this.isTextMode === isTextMode)
            return;
        this.isTextMode = isTextMode;
        if (isTextMode) {
            this.editorDiv.classList.add('text-mode');
        } else {
            this.editorDiv.classList.remove('text-mode');
        }
    }

    private getText(): string {
        const editorHandle = this.editorHandle();
        if (!editorHandle)
            return '';
        return editorHandle.getText();
    }

    private async addAttachment(file: File): Promise<void> {
        const attachment: Attachment = { Id: this.attachmentsIdSeed, File: file, Url: '' };
        if (file.type.startsWith('image'))
            attachment.Url = URL.createObjectURL(file);
        const added = await this.blazorRef.invokeMethodAsync("AddAttachment", attachment.Id, attachment.Url, file.name, file.type, file.size);
        if (!added) {
            if (attachment.Url)
                URL.revokeObjectURL(attachment.Url);
        }
        else {
            this.attachmentsIdSeed++;
            this.attachments.set(attachment.Id, attachment);
            this.changeMode();
        }
    }

    public removeAttachment(id: number) {
        const attachment = this.attachments.get(id);
        this.attachments.delete(id);
        if (attachment && attachment.Url)
            URL.revokeObjectURL(attachment.Url);
        this.changeMode();
    }

    public postMessage = async (chatId: string, text : string): Promise<string> => {
        const formData = new FormData();
        const attachmentsList = [];
        if (this.attachments.size > 0) {
            let i = 0;
            this.attachments.forEach(attachment => {
                formData.append("files[" + i + "]", attachment.File);
                attachmentsList.push({ "id": i, "filename": attachment.File.name, "description": '' });
                i++;
            })
        }

        const payload = { "text": text, "attachments": attachmentsList };
        const payloadJson = JSON.stringify(payload);
        formData.append("payload_json", payloadJson);

        console.log(`${LogScope}: Sending post message request with ${attachmentsList.length} attachment(s)`);
        const url = "api/chats/" + chatId + "/message";
        const response = await fetch(url, { method: 'POST', body: formData });

        if (!response.ok) {
            let reason = response.statusText;
            if (!reason)
                reason = "unknown";
            throw new Error('Failed to send message. Reason: ' + reason);
        }
        return response.statusText;
    }

    public onPostSucceeded = () => {
        for (const attachment of this.attachments.values()) {
            if (attachment.Url)
                URL.revokeObjectURL(attachment.Url);
        }
        this.attachments.clear();
        this.attachmentsIdSeed = 0;
        this.changeMode();
    }

    public showFilesPicker = () => {
        this.filesPicker.click();
    }

    public onSlateEditorRendered()
    {
        const editorHandle = this.editorHandle();
        if (!editorHandle) {
            console.error('SlateEditorHandle is undefined');
            return
        }
        this.changeMode();
        editorHandle.onHasContentChanged = () => this.changeMode();
    }

    private editorHandle = () : SlateEditorHandle => {
        // @ts-ignore
        return this.input.editorHandle as SlateEditorHandle;
    }

    public dispose() {
        this.input.removeEventListener('paste', this.inputPasteListener);
        this.filesPicker.removeEventListener("change", this.filesPickerChangeListener);
        this.postButton.removeEventListener('click', this.postClickListener);
        this.recordButtonObserver.disconnect();
    }
}

interface Attachment {
    File: File;
    Url: string;
    Id: number;
}
