export class ChatMessageEditor {

    private _blazorRef: DotNet.DotNetObject;
    private _input: HTMLDivElement;
    private _post: HTMLButtonElement;
    private _audioRecorder: HTMLSpanElement;
    private _recorder: HTMLButtonElement;

    static create(input: HTMLDivElement, post: HTMLButtonElement, audioRecorder: HTMLSpanElement, backendRef: DotNet.DotNetObject): ChatMessageEditor {
        return new ChatMessageEditor(input, post, audioRecorder, backendRef);
    }

    constructor(input: HTMLDivElement, post: HTMLButtonElement, audioRecorder: HTMLSpanElement, backendRef: DotNet.DotNetObject) {
        if (input === undefined || input === null ) {
            throw new Error("input element is undefined");
        }
        if (post === undefined || post === null) {
            throw new Error("post element is undefined");
        }
        if (audioRecorder === undefined || audioRecorder === null) {
            throw new Error("audioRecorder element is undefined")
        }
        if (backendRef === undefined || backendRef === null) {
            throw new Error("dotnet backend object is undefined");
        }
        this._input = input;
        this._post = post;
        this._audioRecorder = audioRecorder;
        this._blazorRef = backendRef;
        this._recorder = this._audioRecorder.querySelector("button");

        // Wiring up event listeners
        this._input.addEventListener('input', (event: Event & { target: HTMLDivElement; }) => {
            let _ = this.updateClientSideState();
            this.buttonHandler();
        });
        this._input.addEventListener('keydown', (event: KeyboardEvent & { target: HTMLDivElement; }) => {
            if (event.key != 'Enter' || event.shiftKey)
                return;
            event.preventDefault();
            this._blazorRef.invokeMethodAsync("Post", this.getText());
        });
        this._post.addEventListener('click', (event: Event & { target: HTMLButtonElement; }) => {
            this._input.focus();
            this.buttonHandler();
        })
    }

    public buttonHandler() {
        let input = this._input;
        let post = this._post;
        let rec = this._recorder;
        if (input.innerText != "") {
            if (rec.classList.contains('hidden'))
                return;
            rec.style.transform = "translateX(-1.5rem) scale(.05)";
            post.style.transform = "translateX(-1.5rem) scale(.05)";
            setTimeout(() => {
                rec.classList.add("hidden");
                post.classList.remove('hidden');
                post.style.opacity = "1";
            }, 25);
            setTimeout(() => {
                post.style.transform = 'translateX(0px) scale(1)';
            }, 50)

        } else {
            if (post.classList.contains('hidden'))
                return;
            rec.style.transform = "translateX(-1.5rem) scale(.05)";
            post.style.transform = "translateX(-1.5rem) scale(.05)";
            setTimeout(() => {
                post.classList.add("hidden");
                rec.classList.remove('hidden');
                rec.style.opacity = "1";
            }, 25);
            setTimeout(() => {
                rec.style.transform = 'translateX(0px) scale(1)';
            }, 50)
        }
    }

    public getText(): string {
        return this._input.innerText;
    }

    public setText(text: string) {
        this._input.innerText = text;
        this.buttonHandler();
        let _ = this.updateClientSideState();
    }

    public updateClientSideState() : Promise<void> {
        return this._blazorRef.invokeMethodAsync("UpdateClientSideState", this.getText());
    }
}
