import './markup-editor.css';

const LogScope = 'MarkupEditor';
const MentionListId = '@';

export class MarkupEditor {
    static create(
        editorDiv: HTMLDivElement,
        blazorRef: DotNet.DotNetObject,
        autofocus: boolean,
        debug: boolean) {
        return new MarkupEditor(editorDiv, blazorRef, autofocus, debug);
    }

    private readonly contentDiv: HTMLDivElement

    constructor(
        private readonly editorDiv: HTMLDivElement,
        private readonly blazorRef: DotNet.DotNetObject,
        private readonly autofocus: boolean,
        private readonly debug: boolean)
    {
        this.contentDiv = editorDiv.querySelector(".editor-content");
        if (debug)
            console.debug(`${LogScope}.ctor`);
        if (autofocus)
            this.focus();
    }

    public focus() {
        this.contentDiv.focus();
    }

    public getText() {
        return this.contentDiv.innerText;
    }

    public setHtml(html: string) {
        this.contentDiv.innerHTML = html;
    }

    public insertMention(id: string, name: string) {
        throw "Not implemented yet."
    }

    public moveCursorToTheEnd() {
        const range = document.createRange();
        range.selectNodeContents(this.contentDiv);
        range.collapse(false); // false means collapse to end rather than the start
        const selection = window.getSelection();
        selection.removeAllRanges();
        selection.addRange(range);
    }

    private onPost() {
        this.blazorRef.invokeMethodAsync("OnPost", this.getText());
    }

    private onCancel() {
        this.blazorRef.invokeMethodAsync("OnCancel");
    }

    private onOpenPrevious() {
        this.blazorRef.invokeMethodAsync("OnOpenPrevious");
    }

    private onListCommand(listId: string, command: ListCommand) {
        this.blazorRef.invokeMethodAsync("OnListCommand", listId, command);
    }
}

class ListCommand {
    constructor(
        public readonly kind: ListCommandKind,
        public readonly filter?: string) {
    }
}

enum ListCommandKind {
    Show,
    Hide,
    GoToNextItem,
    GoToPreviousItem,
    InsertItem,
}
