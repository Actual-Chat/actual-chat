import './markup-editor.css';
import { KeepAwakeUI } from '../../../UI.Blazor/Services/KeepAwakeUI/keep-awake-ui';
import { delayAsync } from '../../../../nodejs/src/delay';
import { nextTickAsync } from '../../../../nodejs/src/next-tick';

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
        this.contentDiv = editorDiv.querySelector(":scope > .editor-content");
        if (debug)
            console.debug(`${LogScope}.ctor`);
        this.contentDiv.addEventListener("keydown", this.onKeyDown);
        this.contentDiv.addEventListener("keypress", this.onKeyPress);
        this.contentDiv.addEventListener("paste", this.onPaste);
        if (autofocus)
            this.focus();
    }

    public dispose() {
        this.contentDiv.removeEventListener("keydown", this.onKeyDown);
        this.contentDiv.removeEventListener("keypress", this.onKeyPress);
        this.contentDiv.removeEventListener("paste", this.onPaste);
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

    private onKeyDown = async (e: KeyboardEvent) => {
        // console.debug(`${LogScope}.onKeyDown: code = "${e.code}"`)

        // Cancel on escape
        if (e.code === 'Escape') {
            e.stopPropagation();
            e.preventDefault();
            this.onCancel();
            return;
        }
    }

    private onKeyPress = async (e: KeyboardEvent) => {
        // console.debug(`${LogScope}.onKeyPress: code = "${e.code}"`)

        let lowerCaseCode = e.code.toLowerCase();

        // Suppress bold, italic, and underline shortcuts
        if ((e.ctrlKey || e.metaKey)) {
            if (lowerCaseCode === 'b' || lowerCaseCode === "i" || lowerCaseCode == "u") {
                e.stopPropagation();
                e.preventDefault();
                return;
            }
        }

        // Fix new line insertion when cursor is in the end of the document
        if (e.code === 'Enter') {
            e.stopPropagation();
            e.preventDefault();
            if (e.ctrlKey || e.altKey || e.shiftKey || e.metaKey)
                this.onPost()
            else {
                const text1 = this.getText();
                document.execCommand('insertHTML', false, '\n');
                const text2 = this.getText();
                const isFailed = !text2.endsWith('\n') || (text2.startsWith(text1) && !text1.endsWith('\n'));
                if (isFailed) {
                    // Workaround against "Enter does nothing if cursor is in the end of the document" bug
                    document.execCommand('insertHTML', false, '\n');
                }
            }
        }
    }

    private onPaste = async (e: ClipboardEvent) => {
        e.stopPropagation();
        e.preventDefault();

        // @ts-ignore
        const data = e.clipboardData || window.clipboardData;
        const text = data.getData('Text');
        document.execCommand('insertText', false, text);
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
