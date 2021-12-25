export class ChatMessageEditor {

    private _blazorRef: DotNet.DotNetObject;
    private _input: HTMLDivElement;
    private _buttonSpan: HTMLSpanElement;
    private _postButton: HTMLButtonElement;
    private _isTextMode: boolean = false;

    static create(input: HTMLDivElement, buttonSpan: HTMLSpanElement, backendRef: DotNet.DotNetObject): ChatMessageEditor {
        return new ChatMessageEditor(input, buttonSpan, backendRef);
    }

    constructor(input: HTMLDivElement, buttonSpan: HTMLSpanElement, blazorRef: DotNet.DotNetObject) {
        this._input = input;
        this._buttonSpan = buttonSpan;
        this._blazorRef = blazorRef;
        this._postButton = buttonSpan.querySelector("button.post-message");

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
        let recordButton = this._buttonSpan.querySelector("button.audio-recorder") as HTMLButtonElement;
        let languageButton = this._buttonSpan.querySelector("button.chat-language-toggle") as HTMLButtonElement;
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
