import './chat-message-editor.css';

export class ChatMessageEditor {

    private blazorRef: DotNet.DotNetObject;
    private editorDiv: HTMLDivElement;
    private input: HTMLDivElement;
    private postButton: HTMLButtonElement;
    private recorderButtonDiv: HTMLDivElement;
    private recordButton: HTMLButtonElement;
    private isTextMode: boolean = false;
    private isRecording: boolean = false;
    private attachments: string[] = [];

    static create(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject): ChatMessageEditor {
        return new ChatMessageEditor(editorDiv, blazorRef);
    }

    constructor(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this.editorDiv = editorDiv;
        this.input = this.editorDiv.querySelector('div.message-input');
        this.postButton = this.editorDiv.querySelector('button.post-message');
        this.blazorRef = blazorRef;
        this.recorderButtonDiv = this.editorDiv.querySelector('div.recorder-button');
        this.recordButton = this.recorderButtonDiv.querySelector('button');

        // Wiring up event listeners
        this.input.addEventListener('input', this.inputInputListener);
        this.input.addEventListener('keydown', this.inputKeydownListener);
        this.input.addEventListener('mousedown', this.inputMousedownListener);
        this.input.addEventListener('paste', this.inputPasteListener);
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
        for(let item of clipboardData.items) {
            //if (item.kind==='file') {
                if (item.type.startsWith('image')) {
                    const blob = item.getAsFile();
                    const objectURL = URL.createObjectURL(blob);
                    const _ = this.addAttachment(objectURL, blob.name, blob.type, blob.size);
                    event.preventDefault();
                }
            //}
        }
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
        const isTextMode = this.input.innerText != "" || this.attachments.length > 0;
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

    private onPostSucceeded()
    {
        this.setText("");
        for (const attachment of this.attachments)
            URL.revokeObjectURL(attachment);
        this.attachments = [];
        this.changeMode();
    }

    private async getAttachmentContent(fileUrl: string) {
        console.log("getting data for " + fileUrl);
        let blob = await fetch(fileUrl)
            .then(r => r.blob());
        let arrayBuffer = await blob.arrayBuffer();
        return new Uint8Array(arrayBuffer);
    }

    private updateClientSideState() : Promise<void> {
        //console.log("message editor: UpdateClientSideState")
        return this.blazorRef.invokeMethodAsync("UpdateClientSideState", this.getText());
    }

    private async addAttachment(url: string, fileName: string, fileType: string, length: Number) : Promise<void> {
        await this.blazorRef.invokeMethodAsync("AddAttachment", url, fileName, fileType, length);
        this.attachments.push(url);
        this.changeMode();
    }

    private async removeAttachment(url: string) : Promise<void> {
        const index = this.attachments.indexOf(url);
        if (index >= -1) {
            URL.revokeObjectURL(url);
            this.attachments.splice(index, 1);
        }
        this.changeMode();
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

    private dispose() {
        this.input.removeEventListener('input', this.inputInputListener);
        this.input.removeEventListener('keydown', this.inputKeydownListener);
        this.input.removeEventListener('mousedown', this.inputMousedownListener);
        this.input.removeEventListener('paste', this.inputPasteListener);
        this.postButton.removeEventListener('click', this.postClickListener);
        this.recordButton.removeEventListener('click', this.recordClickListener);
    }
}
