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
    private uploads: Uploads = new Uploads();

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
        for(const item of clipboardData.items) {
            if (item.kind==='file') {
                const file = item.getAsFile();
                const _ = this.addAttachment(file);
                event.preventDefault();
            }
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
        const isTextMode = this.input.innerText != "" || this.uploads.length() > 0;
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

    private onPostSucceeded() {
        this.setText("");
        this.uploads.forEach(upload => {
            if (upload.Url)
                URL.revokeObjectURL(upload.Url);
        });
        this.uploads = new Uploads();
        this.changeMode();
    }

    private async getAttachmentBlob(id: string) {
        console.log("getting data with id=" + id);
        const upload = this.uploads.get(id);
        return upload.File;
    }

    private async getAttachmentContent(id: string) {
        const blob = await this.getAttachmentBlob(id);
        const arrayBuffer = await blob.arrayBuffer();
        return new Uint8Array(arrayBuffer);
    }

    private updateClientSideState() : Promise<void> {
        //console.log("message editor: UpdateClientSideState")
        return this.blazorRef.invokeMethodAsync("UpdateClientSideState", this.getText());
    }

    private async addAttachment(file: File) : Promise<void> {
        const upload = new Upload(file);
        if (file.type.startsWith('image')) {
            upload.Url = URL.createObjectURL(file);
        }

        this.uploads.add(upload);
        await this.blazorRef.invokeMethodAsync("AddAttachment", upload.Id, upload.Url, file.name, file.type, file.size);
        this.changeMode();
    }

    public async removeAttachment(id: string) : Promise<void> {
        const upload = this.uploads.remove(id);
        if (upload && upload.Url) {
            URL.revokeObjectURL(upload.Url);
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

    private postMessage(chatId : string)
    {
        const self = this;
        const formData = new FormData();
        const attachmentsList = [];
        const payload =  { "text": self.getText(), "attachments" : attachmentsList };

        if (self.uploads.length()>0) {
            let i = 0;
            self.uploads.forEach(upload => {
                formData.append("files[" + i + "]", upload.File);
                attachmentsList.push({ "id" : i, "filename" : upload.File.name, "filetype" : upload.File.type });
                i++;
            })
        }

        const payload_json = JSON.stringify(payload);
        formData.append("payload_json", payload_json);

        const request = new XMLHttpRequest();
        const url = "api/chats/" + chatId + "/message";
        request.open("POST", url);
        request.onload = function() {
            if (request.status === 200) {
                // If successful, resolve the promise by passing back the request response
                const responseText = request.responseText;
                self.onPostMessageCompleted(true, responseText);
            } else {
                // If it fails, reject the promise with a error message
                self.onPostMessageCompleted(false, 'Image didn\'t load successfully; error code:' + request.statusText);
                //reject(Error('Image didn\'t load successfully; error code:' + request.statusText));
            }
        };
        request.onerror = function() {
            // Also deal with the case when the entire request fails to begin with
            // This is probably a network error, so reject the promise with an appropriate message
            //reject(Error('There was a network error.'));
            self.onPostMessageCompleted(false, 'There was a network error.');
        };
        request.send(formData);
    }

    private onPostMessageCompleted(succeeded : boolean, result : string) : Promise<void> {
        return this.blazorRef.invokeMethodAsync("OnPostMessageCompleted", succeeded, result);
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

class Upload
{
    File: File;
    Url: string;
    Id : string;

    constructor(File: File) {
        this.File = File;
    }
}

class Uploads
{
    private uploads: Upload[] = [];
    private idSeed:number = 0;

    public add(upload : Upload)
    {
        upload.Id = this.idSeed.toString();
        this.idSeed++;
        this.uploads.push(upload);
    }

    public forEach(action : (upload: Upload) => void)
    {
        for (const upload of this.uploads)
            action(upload);
    }

    public remove(id : string) : Upload
    {
        const index = this.uploads.findIndex(element => {
            return element.Id===id;
        })
        if (index >= -1) {
            const upload = this.uploads[index];
            this.uploads.splice(index, 1);
            return upload;
        }
    }

    public get(id : string) : Upload
    {
        return this.uploads.find(element => element.Id===id);
    }

    public length()
    {
        return this.uploads.length;
    }
}
