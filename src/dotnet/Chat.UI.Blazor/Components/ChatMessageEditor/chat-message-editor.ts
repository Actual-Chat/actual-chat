export class ChatMessageEditor {

    private _blazorRef: DotNet.DotNetObject;
    private _input: HTMLDivElement;
    private _post: HTMLButtonElement;

    static create(input: HTMLDivElement, post: HTMLButtonElement, backendRef: DotNet.DotNetObject): ChatMessageEditor {
        return new ChatMessageEditor(input, post, backendRef);
    }

    constructor(input: HTMLDivElement, post: HTMLButtonElement, backendRef: DotNet.DotNetObject) {
        if (input === undefined || input === null ) {
            throw new Error("input element is undefined");
        }
        if (post === undefined || post === null) {
            throw new Error("post element is undefined");
        }
        if (backendRef === undefined || backendRef === null) {
            throw new Error("dotnet backend object is undefined");
        }
        this._input = input;
        this._post = post;
        this._blazorRef = backendRef;

        // Wiring up event listeners
        this._input.addEventListener('input', (event: Event & { target: HTMLDivElement; }) => {
            let _ = this.updateClientSideState();
        });
        this._input.addEventListener('keydown', (event: KeyboardEvent & { target: HTMLDivElement; }) => {
            if (event.key != 'Enter' || event.shiftKey)
                return;
            event.preventDefault();
            this._blazorRef.invokeMethodAsync("Post", this.getText());
        });
        this._post.addEventListener("click", (event: Event & { target: HTMLButtonElement; }) => {
            this._input.focus();
        })
    }

    public getText(): string {
        return this._input.innerText;
    }

    public setText(text: string) {
        this._input.innerText = text;
        let _ = this.updateClientSideState();
    }

    public updateClientSideState() : Promise<void> {
        return this._blazorRef.invokeMethodAsync("UpdateClientSideState", this.getText());
    }
}
