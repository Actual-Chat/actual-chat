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
        const isTextMode = this.input.innerText != "";
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
        return this.blazorRef.invokeMethodAsync("UpdateClientSideState", this.getText());
    }

    private dispose() {
        this.input.removeEventListener('input', this.inputInputListener);
        this.input.removeEventListener('keydown', this.inputKeydownListener);
        this.input.removeEventListener('mousedown', this.inputMousedownListener);
        this.postButton.removeEventListener('click', this.postClickListener);
        this.recordButton.removeEventListener('click', this.recordClickListener);
    }
}
