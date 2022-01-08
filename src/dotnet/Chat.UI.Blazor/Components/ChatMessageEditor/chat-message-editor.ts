export class ChatMessageEditor {

    private _blazorRef: DotNet.DotNetObject;
    private _input: HTMLDivElement;
    private _buttonDiv: HTMLDivElement;
    private _postButton: HTMLButtonElement;
    private _isTextMode: boolean = false;

    static create(input: HTMLDivElement, buttonDiv: HTMLDivElement, backendRef: DotNet.DotNetObject): ChatMessageEditor {
        return new ChatMessageEditor(input, buttonDiv, backendRef);
    }

    constructor(input: HTMLDivElement, buttonDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this._input = input;
        this._buttonDiv = buttonDiv;
        this._blazorRef = blazorRef;
        this._postButton = buttonDiv.querySelector("button.post-message");

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
        let buttonDiv = this._buttonDiv;
        postButton.style.transform = "translateX(-0.5rem) scale(.05)";
        setTimeout(() => {
            if (isTextMode) {
                buttonDiv.classList.replace('hidden', 'md:hidden');
            } else {
                buttonDiv.classList.replace("md:hidden", 'hidden');
            }
        }, 25);
        setTimeout(() => {
            this.changeInputBorder(buttonDiv);
        }, 25);
        setTimeout(() => {
            postButton.style.transform = 'translateX(0px) scale(1)';
        }, 50)
    }

    public changeInputBorder(buttonDiv : HTMLDivElement) {
        let input = this._input;
        let postButtonDisplay = window.getComputedStyle(buttonDiv).display;
        if (postButtonDisplay != 'none' && buttonDiv.classList.contains('md:hidden'))
            input.classList.replace('rounded-r-2xl', 'rounded-none');
        else
            input.classList.replace('rounded-none', 'rounded-r-2xl');
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
