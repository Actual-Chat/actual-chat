import './markup-editor.css';
import { debounce } from '../../../../nodejs/src/debounce';
import { UndoStack } from './undo-stack';

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

    public readonly contentDiv: HTMLDivElement;
    // private readonly contentChangeObserver: MutationObserver;
    private readonly listHandlers: Array<ListHandler>;
    private listHandler?: ListHandler = null;
    private listFilter: string = "";
    private lastSelectedRange?: Range = null;
    private undoStack: UndoStack<string>;

    constructor(
        public readonly editorDiv: HTMLDivElement,
        public readonly blazorRef: DotNet.DotNetObject,
        private readonly autofocus: boolean,
        private readonly debug: boolean)
    {
        if (debug)
            console.debug(`${LogScope}.ctor`);

        this.contentDiv = editorDiv.querySelector(":scope > .editor-content") as HTMLDivElement;
        this.listHandlers = [new MentionListHandler(this)]
        this.undoStack = new UndoStack<string>(
            () => normalize(this.contentDiv.innerHTML),
            value => {
                // To make sure both undo and redo stacks have something
                document.execCommand("insertHTML", false, "&#8203")
                document.execCommand("insertHTML", false, "&#8203")
                document.execCommand("undo", false);
                this.contentDiv.innerHTML = value;
                this.moveCursorToTheEnd();
            },
            (x: string, y: string) => x === y,
            333, debug);

        // Attach listeners & observers
        this.editorDiv.addEventListener("focusin", this.onEditorFocusIn)
        this.contentDiv.addEventListener("focusin", this.onFocusIn)
        this.contentDiv.addEventListener("keydown", this.onKeyDown);
        this.contentDiv.addEventListener("keypress", this.onKeyPress);
        this.contentDiv.addEventListener("paste", this.onPaste);
        this.contentDiv.addEventListener("beforeinput", this.onBeforeInput);
        this.contentDiv.addEventListener("input", this.onInput);
        document.addEventListener("selectionchange", this.onSelectionChange);
        // this.contentChangeObserver = new MutationObserver(this.onContentChange);
        // this.contentChangeObserver.observe(this.contentDiv, { attributes: true, childList: true, subtree : true });

        if (autofocus)
            this.focus();
    }

    public dispose() {
        this.editorDiv.removeEventListener("focusin", this.onEditorFocusIn)
        this.contentDiv.removeEventListener("focusin", this.onFocusIn)
        this.contentDiv.removeEventListener("keydown", this.onKeyDown);
        this.contentDiv.removeEventListener("keypress", this.onKeyPress);
        this.contentDiv.removeEventListener("paste", this.onPaste);
        this.contentDiv.removeEventListener("beforeinput", this.onBeforeInput);
        this.contentDiv.removeEventListener("input", this.onInput);
        document.removeEventListener("selectionchange", this.onSelectionChange);
        // this.contentChangeObserver.disconnect();
    }

    public focus() {
        this.contentDiv.focus();
    }

    public getText() {
        return this.contentDiv.innerText;
    }

    public setHtml(html: string) {
        this.contentDiv.innerHTML = html;
        this.undoStack.clear();
    }

    public insertHtml(html: string, listId?: string) {
        const isFocused = document.activeElement === this.contentDiv;
        if (!isFocused) {
            this.focus();
            this.restoreSelection();
        }
        if (!listId) {
            document.execCommand('insertHTML', false, html);
            this.fixSelection();
            return;
        }

        const listHandler = this.listHandlers.find(h => h.listId == listId);
        if (!listHandler)
            return;
        if (!this.expandSelection(listHandler))
            return;

        document.execCommand('insertHTML', true, html);
        this.fixSelection();
        this.closeListUI();
    }

    public moveCursorToTheEnd() {
        const range = document.createRange();
        range.selectNodeContents(this.contentDiv);
        range.collapse(false); // false means collapse to end rather than the start
        const selection = document.getSelection();
        selection.removeAllRanges();
        selection.addRange(range);
    }

    // Backend method invokers

    private async onPost() : Promise<void> {
        await this.blazorRef.invokeMethodAsync("OnPost", this.getText());
    }

    private async onCancel() : Promise<void> {
        await this.blazorRef.invokeMethodAsync("OnCancel");
    }

    private async onOpenPrevious() : Promise<void> {
        await this.blazorRef.invokeMethodAsync("OnOpenPrevious");
    }

    private async onListCommand(listId: string, command: ListCommand) : Promise<void> {
        // console.debug(`${LogScope}.onListCommand(): listId:`, listId, ', command:', command.kind, ', filter:', command.filter);
        await this.blazorRef.invokeMethodAsync("OnListCommand", listId, command);
    }

    // Event handlers

    private onEditorFocusIn = () => {
        this.focus();
    }

    private onFocusIn = () => {
        document.execCommand("insertBrOnReturn", false, "true");
        document.execCommand("styleWithCSS", false, "false");
    }

    private onKeyDown = (e: KeyboardEvent) => {
        // console.debug(`${LogScope}.onKeyDown: code = "${e.code}"`)

        const ok = () => e.preventDefault();

        // When list handler is active...
        const listHandler = this.listHandler;
        if (listHandler) {
            if (e.code === 'Up' || e.code === 'ArrowUp') {
                void this.onListCommand(listHandler.listId, new ListCommand(ListCommandKind.GoToPreviousItem));
                return ok();
            }
            if (e.code === 'Down' || e.code === 'ArrowDown') {
                void this.onListCommand(listHandler.listId, new ListCommand(ListCommandKind.GoToNextItem));
                return ok();
            }
            if (e.code === 'Enter') {
                e.preventDefault();
                void this.onListCommand(listHandler.listId, new ListCommand(ListCommandKind.InsertItem));
                return ok();
            }
            if (e.code === 'Escape') {
                e.preventDefault();
                if (!this.expandSelection(listHandler))
                    return ok();
                document.execCommand('insertHTML', false, "");
                this.closeListUI();
                return ok();
            }
        }

        // Cancel on escape
        if (e.code === 'Escape') {
            void this.onCancel();
            return ok();
        }

        // Up key & empty content = open previous
        if (e.code === 'Up' || e.code === 'ArrowUp' && this.contentDiv.childNodes.length == 0) {
            void this.onOpenPrevious();
            return ok();
        }
    }

    private onKeyPress = (e: KeyboardEvent) => {
        // console.debug(`${LogScope}.onKeyPress: code = "${e.code}"`)

        const ok = () => e.preventDefault();

        // Suppress bold, italic, and underline shortcuts
        if ((e.ctrlKey || e.metaKey)) {
            let lowerCaseCode = e.code.toLowerCase();
            if (lowerCaseCode === 'b' || lowerCaseCode === "i" || lowerCaseCode == "u")
                return ok();
        }

        // Post + fix the new line insertion when cursor is in the end of the document
        if (e.code === 'Enter') {
            if (e.ctrlKey || e.altKey || e.shiftKey || e.metaKey) {
                void this.onPost()
                return ok();
            }

            const text1 = this.getText();
            document.execCommand('insertHTML', false, '\n');
            const text2 = this.getText();
            const isFailed = !text2.endsWith('\n') || (text2.startsWith(text1) && !text1.endsWith('\n'));
            if (isFailed) {
                // Workaround for "Enter does nothing if cursor is in the end of the document" issue
                document.execCommand('insertHTML', false, '\n');
            }
            return ok();
        }
    }

    private onPaste = (e: ClipboardEvent) => {
        const ok = () => e.preventDefault();

        const data = e.clipboardData;
        const html = data.getData("text/html");
        let text = '';
        if (html) {
            // console.debug(`${LogScope}.onPaste: html:`, html)
            const div = document.createElement("div");
            div.innerHTML = html;
            text = div.innerText;
        }
        else {
            text = data.getData('text');
            // console.debug(`${LogScope}.onPaste: text:`, text)
        }
        text = trimText(text);
        document.execCommand('insertText', false, text);
        return ok();
    }

    private onSelectionChange = () => {
        this.fixSelection();
        this.updateListUIDebounced();
    };

    private onBeforeInput = (e: InputEvent) => {
        const ok = () => e.preventDefault();

        switch (e.inputType) {
            case "historyUndo":
                console.debug(this.contentDiv.innerHTML);
                this.undoStack.undo();
                return ok();
            case "historyRedo": {
                console.debug(this.contentDiv.innerHTML);
                this.undoStack.redo();
                return ok();
            }
            default:
                break;
        }
    }

    private onInput = (e: InputEvent) => {
        this.undoStack.pushDebounced();
        this.updateListUIDebounced();
    }

    // List UI (lists, etc.) support

    private updateListUIDebounced = debounce(() => this.updateListUI(), 100);

    private updateListUI() {
        // console.debug(`${LogScope}.updateListUI()`)
        const cursorRange = this.getCursorRange();
        if (!cursorRange) {
            void this.closeListUI();
            return;
        }

        let listHandlers = this.listHandlers;
        let listHandler = this.listHandler;
        if (listHandler)
            listHandlers = [listHandler].concat(listHandlers.filter(h => h != listHandler));
        for (let h of listHandlers)
            this.tryUseListHandler(h, cursorRange);
    }

    private closeListUI() {
        const listHandler = this.listHandler;
        if (!listHandler)
            return;

        this.listHandler = null;
        this.listFilter = "";
        void this.onListCommand(listHandler.listId, new ListCommand(ListCommandKind.Hide));
    }

    // Helpers

    private tryUseListHandler(listHandler: ListHandler, cursorRange: Range) : boolean {
        const matchText = listHandler.getMatchText(cursorRange);
        const isActive = listHandler == this.listHandler;
        if (!matchText) {
            if (isActive)
                void this.closeListUI();
            return false;
        }

        const listFilter = listHandler.getFilter(matchText);
        if (!isActive) {
            this.listHandler = listHandler;
            this.listFilter = listFilter;
            void this.onListCommand(listHandler.listId, new ListCommand(ListCommandKind.Show, listFilter));
        }
        else if (listFilter != this.listFilter) {
            this.listFilter = listFilter;
            void this.onListCommand(listHandler.listId, new ListCommand(ListCommandKind.Show, listFilter));
        }
        return true;
    }

    private getCursorRange(): Range | null {
        const selection = document.getSelection();
        if (!selection.isCollapsed || selection.rangeCount !== 1)
            return null;

        const cursorRange = selection.getRangeAt(0);
        const node = cursorRange.startContainer;
        if (node.nodeType != Node.TEXT_NODE || !this.contentDiv.contains(node))
            return null

        return cursorRange;
    }

    private fixSelection() {
        const selection = document.getSelection();
        if (!selection.rangeCount)
            return;

        const cursorRange = selection.getRangeAt(0);
        if (!cursorRange)
            return;

        const node = cursorRange.startContainer;
        const endNode = cursorRange.endContainer;
        if (!node || !this.contentDiv.contains(node) || !this.contentDiv.contains(endNode))
            return;

        this.lastSelectedRange = cursorRange;
        if (!cursorRange.collapsed)
            return;

        const listParents = (node: Node) => {
            const parents = new Array<HTMLElement>();
            let parent = node;
            while (parent !== this.contentDiv) {
                if (parent.nodeType == Node.ELEMENT_NODE)
                    parents.push(parent as HTMLElement)
                parent = parent.parentElement;
            }
            parents.reverse();
            return parents;
        }

        const parents = listParents(node);
        for (let parent of parents) {
            const elementContentEditable = parent as unknown as ElementContentEditable;
            if (elementContentEditable.contentEditable && !elementContentEditable.isContentEditable) {
                const newRange = document.createRange();
                newRange.setStartAfter(parent);
                newRange.collapse(false);
                selection.removeAllRanges();
                selection.addRange(newRange);
                this.lastSelectedRange = newRange;
                return;
            }
        }
        return;
    }

    private restoreSelection() {
        if (!this.lastSelectedRange)
            return;

        const selection = document.getSelection();
        selection.removeAllRanges();
        try {
            selection.addRange(this.lastSelectedRange);
        }
        catch (e) {
            console.error(`${LogScope}.restoreSelection: error`, e);
        }
    }

    private expandSelection(listHandler: ListHandler) : boolean {
        const cursorRange = this.getCursorRange();
        if (!cursorRange)
            return false;

        const matchRange = listHandler.getMatchRange(cursorRange);
        if (!matchRange)
            return false;

        const selection = document.getSelection();
        selection.removeAllRanges();
        selection.addRange(matchRange);
        return true;
    }
}

abstract class ListHandler {
    protected constructor(
        public readonly listId: string,
        public readonly editor: MarkupEditor)
    { }

    public getFilter = (matchText: string) => matchText.substring(1);

    public getMatchText(cursorRange: Range): string {
        const matchRange = this.getMatchRange(cursorRange);
        if (!matchRange)
            return "";

        const textNode = matchRange.startContainer as Text;
        if (!textNode)
            return "";

        const text = textNode.textContent;
        return text.substring(matchRange.startOffset, matchRange.endOffset);
    }

    public getMatchRange(cursorRange: Range): Range | null {
        const matchLength = this.getMatchLength(cursorRange);
        if (matchLength === 0)
            return null;

        const matchRange = cursorRange.cloneRange();
        matchRange.setStart(cursorRange.startContainer, cursorRange.startOffset - matchLength);
        return matchRange;
    }

    public getMatchLength(range: Range): number {
        const textNode = range.startContainer as Text;
        if (!textNode)
            return 0;

        const endOffset = range.startOffset; // It's always collapsed
        const startOffset = this.getMatchStart(textNode.textContent, endOffset);
        return (startOffset == null || startOffset >= endOffset) ? 0 : endOffset - startOffset;
    }

    public abstract getMatchStart(text: string, endOffset: number): number | null;
}

class MentionListHandler extends ListHandler {
    static wrongPrefixRe = /[\p{L}\p{Nd}_@`]/u;

    constructor(editor: MarkupEditor) {
        super(MentionListId, editor);
    }

    public getMatchStart(text: string, endOffset: number): number | null {
        let i = endOffset - 1;
        for (; i >= 0; i--) {
            const c = text[i];
            if (c == ' ' || c == '\n' || c == '/')
                return null;
            if (c == '@')
                break;
        }

        if (i == 0)
            return i;

        const c = text[i - 1];
        if (MentionListHandler.wrongPrefixRe.test(c))
            return null;

        return i;
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

// Helpers

const emptyLinesRe = /\n*/g
const newLineRe = /\r\n/g

function trimText(text: string) {
    // NOTE(AY): Write a real implementation of this later
    return text.trim();
}

function normalize(text: string) {
    return text.normalize().replace(newLineRe, "\n");
}
