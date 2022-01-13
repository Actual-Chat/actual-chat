import './chat-message-editor.css';

export class ChatMessageEditor {

    private _blazorRef: DotNet.DotNetObject;
    private _editorDiv: HTMLDivElement;
    private _attachButton: HTMLButtonElement;
    private _input: HTMLDivElement;
    private _audioWave: HTMLDivElement;
    private _postButton: HTMLButtonElement;
    private _langButtonDiv: HTMLDivElement;
    private _recorderButtonDiv: HTMLDivElement;
    private _recordButton: HTMLButtonElement;
    private _playerButtonDiv: HTMLDivElement;
    private _isTextMode: boolean = false;
    private _isRecording: boolean = false;

    static create(editorDiv: HTMLDivElement, backendRef: DotNet.DotNetObject): ChatMessageEditor {
        return new ChatMessageEditor(editorDiv, backendRef);
    }

    constructor(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this._editorDiv = editorDiv;
        this._attachButton = this._editorDiv.querySelector('button.attach-button');
        this._input = this._editorDiv.querySelector('div.message-input');
        this._audioWave = this._editorDiv.querySelector('div.audio-wave');
        this._postButton = this._editorDiv.querySelector('button.post-message');
        this._blazorRef = blazorRef;
        this._langButtonDiv = this._editorDiv.querySelector('div.language-button');
        this._recorderButtonDiv = this._editorDiv.querySelector('div.recorder-button');
        this._playerButtonDiv = this._editorDiv.querySelector('div.player-button');
        this._recordButton = this._recorderButtonDiv.querySelector('button');

        // Wiring up event listeners
        this._input.addEventListener('input', (event: Event & { target: HTMLDivElement; }) => {
            let _ = this.updateClientSideState();
            this.changeMode();
        });
        this._input.addEventListener('keydown', (event: KeyboardEvent & { target: HTMLDivElement; }) => {
            if (event.key != 'Enter' || event.shiftKey)
                return;
            event.preventDefault();
            this._blazorRef.invokeMethodAsync("Post", this.getText());
        });
        this._postButton.addEventListener('click', (event: Event & { target: HTMLButtonElement; }) => {
            this._input.focus();
            this.changeMode();
        })
        this._recordButton.addEventListener('click', (event: Event & {target: HTMLButtonElement; }) => {
            this.syncLanguageButtonVisibility();
        })
        this.changeMode();
    }

    public syncLanguageButtonVisibility() {
        let recordIcon = this._recordButton.querySelector('svg');
        let isRecording = recordIcon.classList.contains('not-recording');
        if (this._isRecording == isRecording)
            return;
        this._isRecording = isRecording;
        if (isRecording){
            this._editorDiv.classList.add('record-mode');
        } else {
            this._editorDiv.classList.remove('record-mode');
        }
    }

    public changeMode() {
        let isTextMode = this._input.innerText != "";
        if (this._isTextMode == isTextMode)
            return;
        this._isTextMode = isTextMode;
        if (isTextMode) {
            this._editorDiv.classList.add( 'text-mode');
        } else {
            this._editorDiv.classList.remove('text-mode');
        }
    }

    public getText(): string {
        return this._input.innerText;
    }

    public setText(text: string) {
        this._input.innerText = text;
        this.changeMode();
        let _ = this.updateClientSideState();
    }

    public updateClientSideState() : Promise<void> {
        return this._blazorRef.invokeMethodAsync("UpdateClientSideState", this.getText());
    }
}
