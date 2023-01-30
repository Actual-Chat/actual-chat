import { getOrInheritData } from 'dom-helpers';
import { throttle } from 'promises';
import { UndoStack } from './undo-stack';
import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'MarkupEditor';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

const MentionListId = '@';
const ZeroWidthSpace = "\u200b";
const ZeroWidthSpaceRe = new RegExp(ZeroWidthSpace, "g");
const CrlfRe = /\r\n/g
const PrefixLfRe = /^(\s*\n)+/g
const SuffixLfRe = /(\n\s*)+$/g

export class MarkupEditor {
    static create(
        editorDiv: HTMLDivElement,
        blazorRef: DotNet.DotNetObject,
        autofocus: boolean,
    ) {
        return new MarkupEditor(editorDiv, blazorRef, autofocus);
    }

    public readonly contentDiv: HTMLDivElement;
    public changed: (html: string, text: string) => void = () => { };

    private readonly listHandlers: Array<ListHandler>;
    private listHandler?: ListHandler = null;
    private listFilter: string = "";
    private lastSelectedRange?: Range = null;
    private undoStack: UndoStack<string>;
    private currentTransaction: () => void | null;
    private lastHtml: string = null;

    constructor(
        public readonly editorDiv: HTMLDivElement,
        public readonly blazorRef: DotNet.DotNetObject,
        private readonly autofocus: boolean,
    ) {
        debugLog?.log(`constructor`);

        this.contentDiv = editorDiv.querySelector(":scope > .editor-content") as HTMLDivElement;
        this.listHandlers = [new MentionListHandler(this)]
        this.undoStack = new UndoStack<string>(
            () => normalize(this.contentDiv.innerHTML),
            value => {
                this.transaction(() => {
                    // To make sure both undo and redo stacks have something
                    document.execCommand("insertHTML", false, "&#8203");
                    document.execCommand("insertHTML", false, "&#8203");
                    document.execCommand("undo", false);
                    this.setHtml(value, false, false);
                });
            },
            (x: string, y: string) => x === y,
            333);

        // Attach listeners & observers
        this.contentDiv.addEventListener("focus", this.onFocus)
        this.contentDiv.addEventListener("blur", this.onBlur)
        this.contentDiv.addEventListener("mousedown", this.onMouseDown)
        this.contentDiv.addEventListener("keydown", this.onKeyDown);
        this.contentDiv.addEventListener("keypress", this.onKeyPress);
        this.contentDiv.addEventListener("paste", this.onPaste);
        this.contentDiv.addEventListener("beforeinput", this.onBeforeInput);
        this.contentDiv.addEventListener("input", this.onInput);
        document.addEventListener("selectionchange", this.onSelectionChange);
        document.addEventListener("click", this.onDocumentClick);

        if (autofocus)
            this.focus();
    }

    public dispose() {
        this.contentDiv.removeEventListener("focus", this.onFocus)
        this.contentDiv.removeEventListener("blur", this.onBlur)
        this.contentDiv.removeEventListener("mousedown", this.onMouseDown)
        this.contentDiv.removeEventListener("keydown", this.onKeyDown);
        this.contentDiv.removeEventListener("keypress", this.onKeyPress);
        this.contentDiv.removeEventListener("paste", this.onPaste);
        this.contentDiv.removeEventListener("beforeinput", this.onBeforeInput);
        this.contentDiv.removeEventListener("input", this.onInput);
        document.removeEventListener("selectionchange", this.onSelectionChange);
        document.removeEventListener("click", this.onDocumentClick);
    }

    public transaction(action: () => void): void {
        const oldTransaction = this.currentTransaction;
        this.currentTransaction = action;
        try {
            action();
        }
        finally {
            this.currentTransaction = oldTransaction;
            if (oldTransaction == null) {
                const html = this.contentDiv.innerHTML;
                if (html != this.lastHtml) {
                    this.lastHtml = html;
                    this.changed(html, this.getText());
                }
            }
        }
    }

    public focus() {
        this.contentDiv.focus();
        this.fixVirtualKeyboard();
    }

    public isEditable(isEditable: boolean = null): boolean {
        if (isEditable !== null)
            this.contentDiv.setAttribute('contenteditable', isEditable ? 'true' : 'false');
        return this.contentDiv.isContentEditable;
    }

    public getText() {
        return this.contentDiv.innerText;
    }

    public setHtml(html: string, mustFocus: boolean = false, clearUndoStack: boolean = true) {
        this.transaction(() => {
            this.contentDiv.innerHTML = html;
            this.fixEverything();
        })
        // Moving cursor to the end, brings focus to the editor.
        // Will do it only when html is not empty or focus is requested explicitly.
        if (html !== "" || mustFocus)
            this.moveCursorToTheEnd();
        if (clearUndoStack)
            this.undoStack.clear();
    }

    public insertHtml(html: string, listId?: string) {
        const isFocused = document.activeElement === this.contentDiv;
        if (!isFocused) {
            this.focus();
            this.restoreSelection();
        }
        if (!listId) {
            this.transaction(() => {
                document.execCommand('insertHTML', false, html);
                this.fixEverything();
            });
            return;
        }

        const listHandler = this.listHandlers.find(h => h.listId == listId);
        if (!listHandler)
            return;
        if (!this.expandSelection(listHandler))
            return;

        this.transaction(() => {
            document.execCommand('insertHTML', true, html);
            this.fixEverything();
        });
        this.closeListUI();
        this.moveCursorToTheEnd();
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

    private async onPost(): Promise<void> {
        await this.blazorRef.invokeMethodAsync("OnPost", this.getText());
    }

    private async onCancel(): Promise<void> {
        await this.blazorRef.invokeMethodAsync("OnCancel");
    }

    private async onOpenPrevious(): Promise<void> {
        await this.blazorRef.invokeMethodAsync("OnOpenPrevious");
    }

    private async onListCommand(listId: string, command: ListCommand): Promise<void> {
        debugLog?.log(`onListCommand(): listId:`, listId, ', command:', command.kind, ', filter:', command.filter);
        await this.blazorRef.invokeMethodAsync("OnListCommand", listId, command);
    }

    // Event handlers

    private onFocus = () => {
        this.transaction(() => {
            document.execCommand("insertBrOnReturn", false, "true");
            document.execCommand("styleWithCSS", false, "false");
        });
        this.fixVirtualKeyboard();
    }

    private onBlur = () => this.fixVirtualKeyboard()

    private onMouseDown = () => {
        this.focus();
    }

    private onKeyDown = (e: KeyboardEvent) => {
        // debugLog?.log(`onKeyDown: code = "${e.code}"`)

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
            if (e.code === 'Enter' || e.code === 'NumpadEnter') {
                e.preventDefault();
                void this.onListCommand(listHandler.listId, new ListCommand(ListCommandKind.InsertItem));
                return ok();
            }
            if (e.code === 'Escape') {
                e.preventDefault();
                if (!this.expandSelection(listHandler))
                    return ok();
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
        // debugLog?.log(`onKeyPress: code = "${e.code}"`)

        const ok = () => e.preventDefault();

        // Suppress bold, italic, and underline shortcuts
        if ((e.ctrlKey || e.metaKey)) {
            let lowerCaseCode = e.code.toLowerCase();
            if (lowerCaseCode === 'b' || lowerCaseCode === "i" || lowerCaseCode == "u")
                return ok();
        }

        // Post + fix the new line insertion when cursor is in the end of the document
        if (e.code === 'Enter' || e.code === 'NumpadEnter') {
            const isPost = e.ctrlKey || e.metaKey || e.altKey || !e.shiftKey;
            if (isPost) {
                void this.onPost()
                return ok();
            }

            this.transaction(() => {
                const text1 = this.getText();
                document.execCommand('insertHTML', false, '\n');
                const text2 = this.getText();
                const isBuggy = !text1.endsWith('\n') && text2.startsWith(text1);
                if (isBuggy) {
                    // Workaround for "Enter does nothing if cursor is in the end of the document" issue
                    document.execCommand('insertHTML', false, '\n');
                }
                this.fixContent();
            });
            return ok();
        }
    }

    private onPaste = (e: ClipboardEvent) => {
        const ok = () => e.preventDefault();

        const data = e.clipboardData;
        const text = cleanPastedText(data.getData('text'));

        // debugLog?.log(`onPaste: text:`, text)
        this.transaction(() => {
            document.execCommand('insertText', false, text);
        });
        return ok();
    }

    private onSelectionChange = () => {
        this.fixSelection();
        this.updateListUIThrottled();
    };

    private onDocumentClick = (event: Event): void => {
        if (!(event.target instanceof Element))
            return;

        const [_, trigger] = getOrInheritData(event.target, 'editorTrigger');
        if (trigger?.toLowerCase() !== 'true')
            return;

        focusAndOpenKeyboard(this.editorDiv, 300);
    };

    private onBeforeInput = (e: InputEvent) => {
        const ok = () => e.preventDefault();

        switch (e.inputType) {
            case "historyUndo":
                this.undoStack.undo();
                return ok();
            case "historyRedo": {
                this.undoStack.redo();
                return ok();
            }
            default:
                break;
        }
    }

    private onInput = (e: InputEvent) => {
        this.fixContent();
        this.undoStack.pushThrottled();
        this.updateListUIThrottled();
    }

    // List UI (lists, etc.) support

    private updateListUIThrottled = throttle(() => this.updateListUI(), 250);
    private updateListUI() {
        // debugLog?.log(`updateListUI`)
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

    private tryUseListHandler(listHandler: ListHandler, cursorRange: Range): boolean {
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

    private restoreSelection() {
        if (!this.lastSelectedRange)
            return;

        const selection = document.getSelection();
        selection.removeAllRanges();
        try {
            selection.addRange(this.lastSelectedRange);
        }
        catch (e) {
            errorLog?.log(`restoreSelection: unhandled error:`, e);
        }
    }

    private expandSelection(listHandler: ListHandler): boolean {
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

    // State fixers

    private fixEverything() {
        this.fixContent();
        this.fixSelection();
        this.fixVirtualKeyboard();
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
                const eParent = asHTMLElement(parent);
                if (eParent)
                    parents.push(eParent)
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

    private fixContent() {
        // We remove all elements with contentEditable == "false", which
        // aren't followed by "\u8203" character to workaround an issue with
        // typing & deletion of such elements in Chrome Android:
        // - https://github.com/ProseMirror/prosemirror/issues/565#issuecomment-552805191
        const process = (parent: Node) => {
            let mustNormalize = false;
            let skipAfter: Node = null;
            for (const node0 of parent.childNodes) {
                if (skipAfter) {
                    if (node0 !== skipAfter)
                        continue;
                    skipAfter = null;
                    continue;
                }

                const t0 = asText(node0);
                if (t0) {
                    const oldText = t0.textContent;
                    const newText = oldText.replace(ZeroWidthSpaceRe, ""); // \u200B (hex) = 8203 (dec)
                    if (newText.length !== oldText.length) {
                        t0.textContent = newText;
                        mustNormalize = true;
                    }
                    continue;
                }

                const e0 = asHTMLElement(node0);
                if (e0) {
                    if (e0.contentEditable !== "false") {
                        process(e0);
                        continue;
                    }
                    let t1 = asText(e0.nextSibling);
                    while (t1 && t1.textContent.length == 0) {
                        const t2 = asText(t1.nextSibling);
                        t1.remove();
                        t1 = t2;
                    }
                    if (t1 && t1.textContent.startsWith(ZeroWidthSpace)) {
                        skipAfter = t1;
                        continue;
                    }
                    node0.remove();
                }
            }

            if (mustNormalize)
                parent.normalize();
        }

        this.transaction(() => process(this.contentDiv));
    }

    private fixVirtualKeyboard() {
        return; // Maybe we'll use it some day

        if (!('virtualKeyboard' in navigator))
            return;
        let mustShow = document.activeElement == this.contentDiv;
        // @ts-ignore
        let virtualKeyboard = navigator.virtualKeyboard as { show(), hide() };
        if (mustShow)
            virtualKeyboard.show();
        else
            virtualKeyboard.hide();
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

function asHTMLElement(node: Node): HTMLElement | null {
    return castNode<HTMLElement>(node, Node.ELEMENT_NODE);
}

function asText(node: Node): Text | null {
    return castNode<Text>(node, Node.TEXT_NODE);
}

function castNode<TNode extends Node>(node: Node, nodeType: number): TNode | null {
    if (!node)
        return null;
    if (node.nodeType !== nodeType)
        return null;
    return node as unknown as TNode;
}

function cleanPastedText(text: string): string {
    text = text.replace(ZeroWidthSpaceRe, '');
    text = normalize(text);
    return text;
}

function normalize(text: string): string {
    return text.normalize().replace(CrlfRe, "\n");
}

const isChromium = window.navigator.userAgent.indexOf('Chrome') !== -1;

function focusAndOpenKeyboard(el: HTMLDivElement, timeout: number) {
    if (isChromium)
        return;

    const tempElement = document.createElement('input');
    tempElement.style.position = 'absolute';
    tempElement.style.top = (el.offsetTop + 7) + 'px';
    tempElement.style.left = el.offsetLeft + 'px';
    tempElement.style.height = '0';
    tempElement.style.opacity = '0';
    document.body.appendChild(tempElement);
    tempElement.focus();
    setTimeout(function() {
        el.focus();
        el.click();
        document.body.removeChild(tempElement);
    }, timeout);
}
