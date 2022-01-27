import './chat-message-editor.css';

export class ChatMessageEditor {

    private _blazorRef: DotNet.DotNetObject;
    private _editorDiv: HTMLDivElement;
    private _input: HTMLDivElement;
    private _postButton: HTMLButtonElement;
    private _recorderButtonDiv: HTMLDivElement;
    private _recordButton: HTMLButtonElement;
    private _isTextMode: boolean = false;
    private _isRecording: boolean = false;
    private _attachButtonDiv: HTMLDivElement;
    private _attachButton: HTMLButtonElement;
    private _attachMenu: HTMLDivElement;

    static create(editorDiv: HTMLDivElement, backendRef: DotNet.DotNetObject): ChatMessageEditor {
        return new ChatMessageEditor(editorDiv, backendRef);
    }

    constructor(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this._editorDiv = editorDiv;
        this._input = this._editorDiv.querySelector('div.message-input');
        this._postButton = this._editorDiv.querySelector('button.post-message');
        this._blazorRef = blazorRef;
        this._recorderButtonDiv = this._editorDiv.querySelector('div.recorder-button');
        this._recordButton = this._recorderButtonDiv.querySelector('button');
        this._attachButtonDiv = this._editorDiv.querySelector('.attach-button-div');
        this._attachButton = this._attachButtonDiv.querySelector('.attach-button');
        this._attachMenu = this._attachButtonDiv.querySelector('.attach-menu');

        let target = this._attachMenu;
        const config = {
            attributes: true,
            childList: false,
            subtree: false,
            attributeOldValue: false
        };
        function callback(mutationsList, observer){
            for (let m of mutationsList){
                if (m.attributeName === 'class'){
                    if (m.target.classList.contains('menu-closed') && m.target.querySelector('.menu-body').classList.contains('hidden')) {
                        closeMenu(blazorRef);
                    }
                }
            }
        }
        function closeMenu(ref: DotNet.DotNetObject) {
            ref.invokeMethodAsync('HideMenu');
        }
        const observer = new MutationObserver(callback);
        observer.observe(target, config);

        window.addEventListener('mouseup', (event: MouseEvent & {target: Element; }) => {
            this.listenerHandler(event);
        });

        document.addEventListener('keydown', (event: KeyboardEvent & {target: Element; }) => {
            if (event.keyCode == 27 || event.key == "Escape" || event.key == "Esc") {
                this.listenerHandler(event);
            }
        });

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
        this._input.addEventListener('mousedown', (event: MouseEvent & {target: HTMLDivElement; }) => {
            this._input.focus();
        })
        this._postButton.addEventListener('click', (event: Event & { target: HTMLButtonElement; }) => {
            this._input.focus();
            this.changeMode();
        })
        this._recordButton.addEventListener('click', (event: Event & {target: HTMLButtonElement; }) => {
            this.syncLanguageButtonVisibility();
        })
        this.changeMode();
    }

    public listenerHandler(event: Event & {target: Element}){
        let menu = this._attachMenu;
        let attachBtn = this._attachButtonDiv;
        switch (event.type) {
            case "mouseup":
                if (!attachBtn.contains(event.target) && menu.classList.contains('menu-opened'))
                    this.closeMenu(menu);
                    break;
            case "keydown":
                if (menu.classList.contains('menu-opened'))
                    this.closeMenu(menu);
                    break;
            default:
                return;
        }
    }

    public closeMenu(menu: HTMLDivElement) {
        menu.querySelector('.menu-body').classList.add('hidden');
        menu.classList.replace('menu-opened', 'menu-closed');
    }

    public syncLanguageButtonVisibility() {
        let recordIcon = this._recordButton.querySelector('svg');
        let isRecording = recordIcon.classList.contains('not-recording');
        if (this._isRecording === isRecording)
            return;
        this._isRecording = isRecording;
        if (isRecording){
            this._editorDiv.classList.add('record-mode');
        } else {
            this._editorDiv.classList.remove('record-mode');
        }
    }

    public changeMode() {
        let isTextMode = this._input.innerText != "";
        if (this._isTextMode === isTextMode)
            return;
        this._isTextMode = isTextMode;
        if (isTextMode) {
            this._editorDiv.classList.add('text-mode');
        } else {
            this._editorDiv.classList.remove('text-mode');
        }
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
