export class ChatMessageEditor {

    _blazorRef: DotNet.DotNetObject;

    static create(backendRef: DotNet.DotNetObject): ChatMessageEditor {
        return new ChatMessageEditor(backendRef);
    }

    constructor(backendRef: DotNet.DotNetObject) {
        if (backendRef === undefined || backendRef === null) {
            throw new Error("dotnet backend object is undefined");
        }
        this._blazorRef = backendRef;
    }

    public addEventListener(): void {
        const editorId = "chat-message-editor-input";
        let input = document.getElementById(editorId);
        input.addEventListener('input', (event: Event & { target: HTMLDivElement; }) => {
            this._blazorRef.invokeMethodAsync("SetMessage", event.target.innerText);
        });
    }

    public clearMessage(): void {
        const editorId = "chat-message-editor-input";
        let input = document.getElementById(editorId);
        input.innerHTML = "";
    }
}
