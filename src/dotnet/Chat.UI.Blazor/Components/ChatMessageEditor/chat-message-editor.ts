import './chat-message-editor.css';

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

    static create(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject): ChatMessageEditor {
        return new ChatMessageEditor(editorDiv, blazorRef);
    }

    constructor(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this.editorDiv = editorDiv;
        this.input = this.editorDiv.querySelector('div.message-input');
        this.filesPicker = this.editorDiv.querySelector('input.files-picker');
        this.postButton = this.editorDiv.querySelector('.post-message button');
        this.blazorRef = blazorRef;
        this.recorderButtonDiv = this.editorDiv.querySelector('div.recorder-button');
        this.recordButton = this.recorderButtonDiv.querySelector('button');

        // Wiring up event listeners
        this.input.addEventListener('input', this.inputInputListener);
        this.input.addEventListener('keydown', this.inputKeydownListener);
        this.input.addEventListener('mousedown', this.inputMousedownListener);
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

    private inputInputListener = ((event: Event & { target: Element; }) => {
        void this.updateClientSideState();
        this.changeMode();
    })

    private inputKeydownListener = ((event: KeyboardEvent & { target: Element; }) => {
        if (event.key != 'Enter' || event.shiftKey)
            return;
        event.preventDefault();
        this.blazorRef.invokeMethodAsync("Post", this.getText());
    })

    private inputMousedownListener = ((event: MouseEvent & { target: Element; }) => {
        this.input.focus();
    })

    private inputPasteListener = ((event: ClipboardEvent & { target: Element; }) => {
        // Get pasted data via clipboard API
        const clipboardData = event.clipboardData;
        const pastedData = clipboardData.getData('text/plain');
        if (pastedData.length > 0) {
            this.pasteClipboardData(pastedData);
            event.preventDefault();
            return;
        }
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
        this.input.focus();
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
        const isTextMode = this.input.innerText != "" || this.attachments.size > 0;
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
        return this.input.innerText;
    }

    private setText(text: string) {
        this.input.innerText = text;
        this.changeMode();
        void this.updateClientSideState();
    }

    private updateClientSideState(): Promise<void> {
        console.log(`${LogScope}: UpdateClientSideState`);
        return this.blazorRef.invokeMethodAsync("UpdateClientSideState", this.getText());
    }

    private pasteClipboardData(pastedData: string) {
        // document.execCommand api is deprecated
        // (see https://developer.mozilla.org/ru/docs/Web/API/Document/execCommand)
        // but it gives better experience for undo (ctrl+z) in comparison with
        // insertTextWithSelection function implemented with using Selection API
        // (see https://developer.mozilla.org/en-US/docs/Web/API/Selection)
        if (ChatMessageEditor.insertTextWithExecCommand(pastedData))
            return
        ChatMessageEditor.insertTextWithSelection(pastedData);
        this.changeMode();
        void this.updateClientSideState();
    }

    private static replaceHeadingSpaces(text: string): string {
        let spacesNumber = 0;
        for (let i = 0; i < text.length; i++) {
            if (text.charAt(i) !== " ")
                break;
            spacesNumber++;
        }
        const repeatNumber = Math.ceil(spacesNumber / 2);
        return "&nbsp; ".repeat(repeatNumber) + text.substr(spacesNumber);
    }

    private static insertTextWithExecCommand(text: string): boolean {
        //return document.execCommand("insertText", false, text)
        function escapetext(text: string): string {
            const map = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#039;' };
            return text.replace(/[&<>"']/g, function (m) {
                return map[m];
            });
        }

        let html = "";
        const lines = text.split(/\r\n|\r|\n/);
        let firstLine = true;
        for (let line of lines) {
            if (!firstLine)
                html += "<br />";
            html += ChatMessageEditor.replaceHeadingSpaces(escapetext(line));
            firstLine = false;
        }
        return document.execCommand('insertHtml', false, html);
    }

    private static insertTextWithSelection(text: string) {
        const selection = window.getSelection();
        if (!selection)
            return;
        if (selection.getRangeAt && selection.rangeCount) {
            const range = selection.getRangeAt(0);
            range.deleteContents();
            const lines = text.split(/\r\n|\r|\n/);
            let lastLine = true;
            for (let line of lines.reverse()) {
                if (!lastLine)
                    range.insertNode(document.createElement("br"));
                //line = ChatMessageEditor.replaceHeadingSpaces(line);
                range.insertNode(document.createTextNode(line));
                lastLine = false;
            }
            selection.collapseToEnd();
        }
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

    private postMessage = async (chatId: string): Promise<string> => {
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

        const payload = { "text": this.getText(), "attachments": attachmentsList };
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

    private onPostSucceeded = () => {
        this.setText("");
        for (const attachment of this.attachments.values()) {
            if (attachment.Url)
                URL.revokeObjectURL(attachment.Url);
        }
        this.attachments.clear();
        this.attachmentsIdSeed = 0;
        this.changeMode();
    }

    private showFilesPicker = () => {
        this.filesPicker.click();
    }

    private dispose() {
        this.input.removeEventListener('input', this.inputInputListener);
        this.input.removeEventListener('keydown', this.inputKeydownListener);
        this.input.removeEventListener('mousedown', this.inputMousedownListener);
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
