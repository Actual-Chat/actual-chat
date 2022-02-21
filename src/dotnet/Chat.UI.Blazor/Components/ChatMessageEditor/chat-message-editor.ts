import './chat-message-editor.css';

export class ChatMessageEditor {
    private blazorRef: DotNet.DotNetObject;
    private editorDiv: HTMLDivElement;
    private input: HTMLDivElement;
    private filesPicker: HTMLInputElement;
    private postButton: HTMLButtonElement;
    private recorderButtonDiv: HTMLDivElement;
    private recordButton: HTMLButtonElement;
    private isTextMode: boolean = false;
    private isRecording: boolean = false;
    private attachments: Attachments = new Attachments();

    static create(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject): ChatMessageEditor {
        return new ChatMessageEditor(editorDiv, blazorRef);
    }

    constructor(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this.editorDiv = editorDiv;
        this.input = this.editorDiv.querySelector('div.message-input');
        this.filesPicker = this.editorDiv.querySelector('input.files-picker');
        this.postButton = this.editorDiv.querySelector('button.post-message');
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
        this.recordButton.addEventListener('click', this.recordClickListener);
        this.changeMode();
    }

    private inputInputListener = ((event: Event & {target: Element; }) => {
        const _ = this.updateClientSideState();
        this.changeMode();
    })

    private inputKeydownListener = ((event: KeyboardEvent & {target: Element; }) => {
        if (event.key != 'Enter' || event.shiftKey)
            return;
        event.preventDefault();
        this.blazorRef.invokeMethodAsync("Post", this.getText());
    })

    private inputMousedownListener = ((event: MouseEvent & {target: Element; }) => {
        this.input.focus();
    })

    private inputPasteListener = ((event: ClipboardEvent & {target: Element; }) => {
        // Get pasted data via clipboard API
        const clipboardData = event.clipboardData;
        const pastedData = clipboardData.getData('text/plain');
        if (pastedData.length>0) {
            this.pasteClipboardData(pastedData);
            event.preventDefault();
            return;
        }
        for(const item of clipboardData.items) {
            if (item.kind==='file') {
                const file = item.getAsFile();
                const _ = this.addAttachment(file);
                event.preventDefault();
            }
        }
    })

    private filesPickerChangeListener = (async (event: Event & { target: Element; }) => {
        for (const file of this.filesPicker.files)
            await this.addAttachment(file);
        this.filesPicker.value = '';
    })

    private postClickListener = ((event: MouseEvent & {target: Element; }) => {
        this.input.focus();
        this.changeMode();
    })

    private recordClickListener = ((event: Event & {target: Element; }) => {
        this.syncLanguageButtonVisibility();
    })

    private syncLanguageButtonVisibility() {
        const recordIcon = this.recordButton.querySelector('svg');
        const isRecording = recordIcon.classList.contains('not-recording');
        if (this.isRecording === isRecording)
            return;
        this.isRecording = isRecording;
        if (isRecording){
            this.editorDiv.classList.add('record-mode');
        } else {
            this.editorDiv.classList.remove('record-mode');
        }
    }

    private changeMode() {
        const isTextMode = this.input.innerText != "" || this.attachments.length() > 0;
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
        const _ = this.updateClientSideState();
    }

    private updateClientSideState() : Promise<void> {
        //console.log("message editor: UpdateClientSideState")
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
        const _ = this.updateClientSideState();
    }

    private static replaceHeadingSpaces(text : string) : string {
        let spacesNumber = 0;
        for (let i = 0; i < text.length; i++) {
            if (text.charAt(i) !== " ")
                break;
            spacesNumber++;
        }
        const repeatNumber = Math.ceil(spacesNumber / 2);
        return "&nbsp; ".repeat(repeatNumber) + text.substr(spacesNumber);
    }

    private static insertTextWithExecCommand(text : string) : boolean {
        //return document.execCommand("insertText", false, text)
        function escapetext(text : string) : string {
            const map = {'&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#039;'};
            return text.replace(/[&<>"']/g, function(m) {
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

    private static insertTextWithSelection(text : string) {
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

    private async addAttachment(file: File) : Promise<void> {
        const attachment = new Attachment(file);
        if (file.type.startsWith('image'))
            attachment.Url = URL.createObjectURL(file);

        this.attachments.add(attachment);
        await this.blazorRef.invokeMethodAsync("AddAttachment", attachment.Id, attachment.Url, file.name, file.type, file.size);
        this.changeMode();
    }

    public removeAttachment(id: string) {
        const attachment = this.attachments.remove(id);
        if (attachment && attachment.Url)
            URL.revokeObjectURL(attachment.Url);
        this.changeMode();
    }

    private postMessage(chatId : string) : Promise<string> {
        const self = this;
        return new Promise(function(resolve, reject) {
            const formData = new FormData();
            const attachmentsList = [];
            const payload = {"text": self.getText(), "attachments": attachmentsList};

            if (self.attachments.length() > 0) {
                let i = 0;
                self.attachments.forEach(attachment => {
                    formData.append("files[" + i + "]", attachment.File);
                    attachmentsList.push({"id": i, "filename": attachment.File.name, "filetype": attachment.File.type});
                    i++;
                })
            }

            const payload_json = JSON.stringify(payload);
            formData.append("payload_json", payload_json);

            const request = new XMLHttpRequest();
            const url = "api/chats/" + chatId + "/message";
            request.open("POST", url);
            request.onload = function () {
                if (request.status === 200) {
                    resolve(request.responseText);
                } else {
                    let reason = request.statusText;
                    if (!reason)
                        reason = "unknown";
                    reject(Error('Failed to send message. Reason: ' + reason));
                }
            };
            request.onerror = function () {
                // Also deal with the case when the entire request fails to begin with
                // This is probably a network error, so reject the promise with an appropriate message
                reject(Error('Failed to send message. There was a network error.'));
            };
            request.send(formData);
        });
    }

    private onPostSucceeded() {
        this.setText("");
        this.attachments.forEach(attachment => {
            if (attachment.Url)
                URL.revokeObjectURL(attachment.Url);
        });
        this.attachments = new Attachments();
        this.changeMode();
    }

    private showFilesPicker = () => {
        this.filesPicker.click();
    };

    private dispose() {
        this.input.removeEventListener('input', this.inputInputListener);
        this.input.removeEventListener('keydown', this.inputKeydownListener);
        this.input.removeEventListener('mousedown', this.inputMousedownListener);
        this.input.removeEventListener('paste', this.inputPasteListener);
        this.filesPicker.addEventListener("change", this.filesPickerChangeListener);
        this.postButton.removeEventListener('click', this.postClickListener);
        this.recordButton.removeEventListener('click', this.recordClickListener);
    }
}

class Attachment {
    File: File;
    Url: string;
    Id : string;

    constructor(File: File) {
        this.File = File;
    }
}

class Attachments {
    private attachments: Attachment[] = [];
    private idSeed:number = 0;

    public add(attachment : Attachment) {
        attachment.Id = this.idSeed.toString();
        this.idSeed++;
        this.attachments.push(attachment);
    }

    public forEach(action : (attachment: Attachment) => void) {
        for (const attachment of this.attachments)
            action(attachment);
    }

    public remove(id : string) : Attachment {
        const index = this.attachments.findIndex(element => element.Id===id);
        if (index >= -1) {
            const attachment = this.attachments[index];
            this.attachments.splice(index, 1);
            return attachment;
        }
    }

    public get(id : string) : Attachment {
        return this.attachments.find(element => element.Id===id);
    }

    public length() {
        return this.attachments.length;
    }
}
