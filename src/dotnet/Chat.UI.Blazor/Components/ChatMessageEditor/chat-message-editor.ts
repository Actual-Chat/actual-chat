export class ChatMessageEditor {

    private _blazorRef: DotNet.DotNetObject;
    private _input: HTMLDivElement;

    static create(input: HTMLDivElement, backendRef: DotNet.DotNetObject): ChatMessageEditor {
        return new ChatMessageEditor(input, backendRef);
    }

    constructor(input: HTMLDivElement, backendRef: DotNet.DotNetObject) {
        if (input === undefined || input === null ) {
            throw new Error("input element is undefined");
        }
        if (backendRef === undefined || backendRef === null) {
            throw new Error("dotnet backend object is undefined");
        }
        this._input = input;
        this._blazorRef = backendRef;
    }

    public addEventListener(): void {
        this._input.addEventListener('input', (event: Event & { target: HTMLDivElement; }) => {
            this._blazorRef.invokeMethodAsync("SetMessage", event.target.innerText);
        });
    }

    public clearMessage(): void {
        this._input.innerHTML = "";
    }
}
