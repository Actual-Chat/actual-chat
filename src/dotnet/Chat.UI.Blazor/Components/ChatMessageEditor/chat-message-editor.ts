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
<<<<<<< Updated upstream
        this._controlDiv = controlDiv;
        this._langButtonDiv = this._controlDiv.querySelector('div.language-button');
        this._recorderButtonDiv = this._controlDiv.querySelector('div.recorder-button');
        this._playerButtonDiv = this._controlDiv.querySelector('div.player-button');
        this._recordButton = this._recorderButtonDiv.querySelector('button');
=======
        this._langButtonDiv = this._editorDiv.querySelector('div.language-button');
        this._recordButtonDiv = this._editorDiv.querySelector('div.recorder-button');
        this._playerButtonDiv = this._editorDiv.querySelector('div.player-button');
        this._recordButton = this._recordButtonDiv.querySelector('button');
>>>>>>> Stashed changes

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
<<<<<<< Updated upstream
        this._isRecording = isRecording;
        let languageButton = this._langButtonDiv;
        let input = this._input;
        let wave = this._audioWave;
        languageButton.style.transform = "translateX(1rem) scale(.05)";
        setTimeout(() => {
            if (isRecording){
                languageButton.classList.replace('hidden', 'flex')
                input.classList.replace('block', 'md:block')
                input.classList.replace('pr-2', 'md:pr-2');
                input.classList.add('sm:hidden');
                wave.classList.replace('hidden', 'md:hidden')
                wave.classList.add('sm:block');
            } else {
                languageButton.classList.replace('flex', 'hidden');
                input.classList.remove('sm:hidden');
                input.classList.replace('md:block', 'block');
                input.classList.replace('md:pr-2', 'pr-2');
                wave.classList.remove('sm:block');
                wave.classList.replace('md:hidden', 'hidden');
            }
        }, 25);
        setTimeout(() => {
            languageButton.style.transform = 'translateX(0px) scale(1)';
        }, 50);
=======
        this._isRecordOn = isRecordOn;
        if (isRecordOn){
            this._editorDiv.classList.replace('initial', 'record-mode');
        } else {
            this._editorDiv.classList.replace('record-mode', 'initial');
        }
>>>>>>> Stashed changes
    }

    public changeMode() {
        let isTextMode = this._input.innerText != "";
        if (this._isTextMode == isTextMode)
            return;
        this._isTextMode = isTextMode;
<<<<<<< Updated upstream
        let postButton = this._postButton;
        let recordButton = this._recorderButtonDiv;
        let playerButton = this._playerButtonDiv;
        postButton.style.transform = "translateX(-0.5rem) scale(.05)";
        setTimeout(() => {
            if (isTextMode) {
                postButton.classList.replace('hidden', 'md:hidden');
                recordButton.classList.add('hidden');
                recordButton.classList.replace('flex', 'md:flex');
                playerButton.classList.replace('w-1/2', 'w-full');
            } else {
                postButton.classList.replace("md:hidden", 'hidden');
                recordButton.classList.remove('hidden');
                recordButton.classList.replace('md:flex', 'flex');
                playerButton.classList.replace('w-full', 'w-1/2');
            }
        }, 25);
        setTimeout(() => {
            postButton.style.transform = 'translateX(0px) scale(1)';
        }, 50)
=======
        if (isTextMode) {
            this._editorDiv.classList.replace('initial', 'text-mode');
        } else {
            this._editorDiv.classList.replace('text-mode', 'initial');
        }
>>>>>>> Stashed changes
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
