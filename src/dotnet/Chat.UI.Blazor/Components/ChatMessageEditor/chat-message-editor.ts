export class ChatMessageEditor {

    private _blazorRef: DotNet.DotNetObject;
    private _inputDiv: HTMLDivElement;
    private _attachButton: HTMLButtonElement;
    private _input: HTMLDivElement;
    private _postButton: HTMLButtonElement;
    private _controlDiv: HTMLDivElement;
    private _langButtonDiv: HTMLDivElement;
    private _recordButtonDiv: HTMLDivElement;
    private _playerButtonDiv: HTMLDivElement;
    private _isTextMode: boolean = false;

    static create(inputDiv: HTMLDivElement, controlDiv: HTMLDivElement, backendRef: DotNet.DotNetObject): ChatMessageEditor {
        return new ChatMessageEditor(inputDiv, controlDiv, backendRef);
    }

    constructor(inputDiv: HTMLDivElement, controlDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this._inputDiv = inputDiv;
        this._attachButton = this._inputDiv.querySelector('button.attach-button');
        this._input = this._inputDiv.querySelector('div.message-input');
        this._postButton = this._inputDiv.querySelector('button.post-message');
        this._blazorRef = blazorRef;
        this._controlDiv = controlDiv;
        this._langButtonDiv = this._controlDiv.querySelector('div.language-button');
        this._recordButtonDiv = this._controlDiv.querySelector('div.recorder-button');
        this._playerButtonDiv = this._controlDiv.querySelector('div.player-button');

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
        this.changeMode();
    }

    public changeMode() {
        let isTextMode = this._input.innerText != "";
        if (this._isTextMode == isTextMode)
            return;
        this._isTextMode = isTextMode;
        let postButton = this._postButton;
        let recordButton = this._recordButtonDiv;
        let playerButton = this._playerButtonDiv;
        postButton.style.transform = "translateX(-0.5rem) scale(.05)";
        setTimeout(() => {
            if (isTextMode) {
                postButton.classList.replace('hidden', 'md:hidden');
                recordButton.classList.add('hidden');
                recordButton.classList.replace('flex', 'md:flex');
                playerButton.classList.replace('w-1/2', 'w-full');
                console.log('record button styles text mode: ' + recordButton.classList);
            } else {
                postButton.classList.replace("md:hidden", 'hidden');
                recordButton.classList.remove('hidden');
                recordButton.classList.replace('md:flex', 'flex');
                playerButton.classList.replace('w-full', 'w-1/2');
                console.log('record button styles record mode: ' + recordButton.classList);
            }
        }, 25);
        setTimeout(() => {
            postButton.style.transform = 'translateX(0px) scale(1)';
        }, 50)
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
