export class ChatMessageEditor {

    private _blazorRef: DotNet.DotNetObject;
    private _input: HTMLDivElement;
    private _postButton: HTMLButtonElement;
    private _audioRecorder: HTMLSpanElement;
    private _recordButton: HTMLButtonElement;
    private _languageButton: HTMLButtonElement;
    private _isTextMode: boolean = false;

    static create(input: HTMLDivElement, post: HTMLButtonElement, audioRecorder: HTMLSpanElement, backendRef: DotNet.DotNetObject): ChatMessageEditor {
        return new ChatMessageEditor(input, post, audioRecorder, backendRef);
    }

    constructor(input: HTMLDivElement, post: HTMLButtonElement, audioRecorder: HTMLSpanElement, blazorRef: DotNet.DotNetObject) {
        if (input == null )
            throw new Error("input element is undefined");
        if (post == null)
            throw new Error("post element is undefined");
        if (audioRecorder == null)
            throw new Error("audioRecorder element is undefined")
        if (blazorRef == null)
            throw new Error("dotnet backend object is undefined");
        this._input = input;
        this._postButton = post;
        this._audioRecorder = audioRecorder;
        this._blazorRef = blazorRef;
        this._recordButton = this._audioRecorder.querySelector("button.audio-recorder");
        this._languageButton = this._audioRecorder.querySelector("button.chat-language-toggle");

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
        let recordButton = this._recordButton;
        let languageButton = this._languageButton;
        postButton.style.transform = "translateX(-0.5rem) scale(.05)";
        recordButton.style.transform = "translateX(-0.5rem) scale(.05)";
        setTimeout(() => {
            if (isTextMode) {
                postButton.classList.remove('hidden');
                recordButton.classList.add("hidden");
                languageButton.classList.add("hidden");
            } else {
                postButton.classList.add("hidden");
                recordButton.classList.remove('hidden');
                languageButton.classList.remove("hidden");
            }
        }, 25);
        setTimeout(() => {
            postButton.style.transform = 'translateX(0px) scale(1)';
            recordButton.style.transform = 'translateX(0px) scale(1)';
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
