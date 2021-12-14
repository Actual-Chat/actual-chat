export class ChatMessageEditor {

    private _blazorRef: DotNet.DotNetObject;
    private _input: HTMLDivElement;
    private _post: HTMLButtonElement;
    private _recorder: HTMLButtonElement;

    static create(input: HTMLDivElement, post: HTMLButtonElement, recorder: HTMLButtonElement, backendRef: DotNet.DotNetObject): ChatMessageEditor {
        return new ChatMessageEditor(input, post, recorder, backendRef);
    }

    constructor(input: HTMLDivElement, post: HTMLButtonElement, recorder: HTMLButtonElement, backendRef: DotNet.DotNetObject) {
        if (input === undefined || input === null ) {
            throw new Error("input element is undefined");
        }
        if (post === undefined || post === null) {
            throw new Error("post element is undefined");
        }
        if (recorder === undefined || recorder === null) {
            throw new Error("audiorecorder element is undefined")
        }
        if (backendRef === undefined || backendRef === null) {
            throw new Error("dotnet backend object is undefined");
        }
        this._input = input;
        this._post = post;
        this._recorder = recorder;
        this._blazorRef = backendRef;

        // Wiring up event listeners
        this._input.addEventListener('input', (event: Event & { target: HTMLDivElement; }) => {
            let _ = this.updateClientSideState();
            this.getPostAndMicButtonsDisplay();
        });
        this._input.addEventListener('keydown', (event: KeyboardEvent & { target: HTMLDivElement; }) => {
            if (event.key != 'Enter' || event.shiftKey)
                return;
            event.preventDefault();
            this._blazorRef.invokeMethodAsync("Post", this.getText());
        });
        this._post.addEventListener("click", (event: Event & { target: HTMLButtonElement; }) => {
            this._input.focus();
            this.getPostAndMicButtonsDisplay();
        })
    }

    public getPostAndMicButtonsDisplay() {
        let post = this._post;
        let rec = this._recorder;
        if (this._input.innerText === "") {
            this.changeButtonVisibility(post, false);
            this.changeButtonVisibility(rec, true);
        } else {
            this.changeButtonVisibility(rec, false);
            this.changeButtonVisibility(post, true);
        }
    }

    public changeButtonVisibility(button: HTMLButtonElement, isVisible: boolean) {
        if (isVisible){
            button.style.display = "block";
        } else {
            button.style.display = "none";
        }
    }

    public getText(): string {
        return this._input.innerText;
    }

    public setText(text: string) {
        this._input.innerText = text;
        this.getPostAndMicButtonsDisplay();
        let _ = this.updateClientSideState();
    }

    public updateClientSideState() : Promise<void> {
        return this._blazorRef.invokeMethodAsync("UpdateClientSideState", this.getText());
    }
}
