import { DeviceInfo } from 'device-info';
import { getOrInheritData } from 'dom-helpers';
import { Timeout } from 'timeout';
import { throttle } from 'promises';
import { preventDefaultForEvent } from 'event-handling';
import { UndoStack } from './undo-stack';
import { Log } from 'logging';

const { debugLog, errorLog } = Log.get('MarkupEditor');

const MentionListId = '@';
const ZeroWidthSpace = "\u200b";
const ZeroWidthSpaceRe = new RegExp(ZeroWidthSpace, "g");
const CrlfRe = /\r\n/g
const DoubleLfRe = /\n\n/g
const SingleLfRe = /[^\n^]\n[^\n$]/g

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

    private isContentDivInitialized = false;
    private lastSelectedRange?: Range = null;
    private undoStack: UndoStack<string>;
    private currentTransaction: () => void | null;
    private lastHtml: string = null;

    private readonly listHandlers: Array<ListHandler>;
    private listHandler?: ListHandler = null;
    private listFilter: string = "";

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
                this.transaction('init', () => {
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
        this.contentDiv.removeEventListener("keydown", this.onKeyDown);
        this.contentDiv.removeEventListener("keypress", this.onKeyPress);
        this.contentDiv.removeEventListener("paste", this.onPaste);
        this.contentDiv.removeEventListener("beforeinput", this.onBeforeInput);
        this.contentDiv.removeEventListener("input", this.onInput);
        document.removeEventListener("selectionchange", this.onSelectionChange);
        document.removeEventListener("click", this.onDocumentClick);
    }

    public transaction(title: string, action: () => void): void {
        debugLog?.log(`-> transaction ${title}`);
        const oldTransaction = this.currentTransaction;
        this.currentTransaction = action;
        try {
            action();
        }
        finally {
            debugLog?.log(`<- transaction ${title}`);
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

    public focus(force = false) {
        if (!force) {
            if (this.hasFocus())
                return;

            if (!DeviceInfo.isIos) {
                debugLog?.log('focus');
                this.contentDiv.focus();
                return;
            }
        }

        // The code blow makes sure mobile keyboard is shown on iOS.
        // It works only after the first interaction.
        debugLog?.log('focus: using iOS/force workaround');
        const contentDiv = this.contentDiv;
        const tempInput = document.createElement('input');
        tempInput.style.position = 'absolute';
        tempInput.style.top = (contentDiv.offsetTop + 7) + 'px';
        tempInput.style.left = contentDiv.offsetLeft + 'px';
        tempInput.style.height = '0';
        tempInput.style.opacity = '0';
        document.body.appendChild(tempInput);
        tempInput.focus();
        Timeout.startRegular(100, () => {
            contentDiv.focus();
            contentDiv.click();
            document.body.removeChild(tempInput);
        });
    }

    public blur() {
        if (!this.hasFocus())
            return;

        if (!DeviceInfo.isIos) {
            this.contentDiv.blur();
            return;
        }
    }

    public hasFocus(): boolean {
        const activeElement = document.activeElement;
        if (!activeElement)
            return false;
        if (activeElement === this.contentDiv)
            return true;

        const parents = listParents(activeElement, this.contentDiv);
        for (const p of parents)
            if (activeElement === p)
                return true;

        return false;
    }

    public isEditable(mustBeEditable: boolean = null): boolean {
        if (mustBeEditable !== null) {
            if (this.contentDiv.isContentEditable == mustBeEditable)
                return;
            this.contentDiv.setAttribute('contenteditable', mustBeEditable ? 'true' : 'false');
        }
        const isEditable = this.contentDiv.isContentEditable;
        debugLog?.log(`isEditable(${mustBeEditable}) -> ${isEditable}`);
        return isEditable;
    }

    /** Called by Blazor */
    public getText() {
        return this.contentDiv.innerText;
    }

    public getHtml() {
        return this.contentDiv.innerHTML;
    }

    public setHtml(html: string, mustFocus: boolean = false, clearUndoStack: boolean = true) {
        this.transaction('setHtml', () => {
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
        if (!this.hasFocus()) {
            this.focus();
            this.restoreSelection();
        }
        if (!listId) {
            this.transaction('insertHtml', () => {
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

        this.transaction(`insertHtml(listId: ${listId})`, () => {
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
        debugLog?.log('onFocus');
        if (!this.isContentDivInitialized) {
            this.isContentDivInitialized = true;
            this.transaction('onFocus - init', () => {
                document.execCommand("insertBrOnReturn", false, "true");
                document.execCommand("styleWithCSS", false, "false");
            });
            this.focus(true);
        }
    }

    private onKeyDown = (e: KeyboardEvent) => {
        debugLog?.log(`onKeyDown, code = "${e.code}", event:`, e)

        const ok = () => preventDefaultForEvent(e);

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
                preventDefaultForEvent(e);
                void this.onListCommand(listHandler.listId, new ListCommand(ListCommandKind.InsertItem));
                return ok();
            }
            if (e.code === 'Escape') {
                preventDefaultForEvent(e);
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
        debugLog?.log(`onKeyPress, code = "${e.code}", event:`, e)

        const ok = () => preventDefaultForEvent(e);

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

            this.transaction('fix line feed', () => {
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
        debugLog?.log(`onPaste, event:`, e)
        const ok = () => preventDefaultForEvent(e);

        const data = e.clipboardData;
        const plainText = data.getData('text');
        const text = cleanupPastedText(plainText, data.types.includes('text/html'));

        // debugLog?.log(`onPaste: text:`, text)
        this.transaction('onPaste', () => {
            this.insertTextAtCursor(text);
        });
        return ok();
    }

    private insertTextAtCursor(text)
    {
        const selection = window.getSelection();
        const range = selection.getRangeAt(0);
        const node = document.createTextNode(text);
        range.deleteContents();
        range.insertNode(node);
        selection.setPosition(node, text.length);
    }

    private onSelectionChange = (e: Event) => {
        debugLog?.log(`onSelectionChange, event:`, e)
        this.fixSelection();
        this.updateListUIThrottled();
    };

    private onDocumentClick = (e: Event): void => {
        if (!(e.target instanceof Element))
            return;

        const [_, editorFocus] = getOrInheritData(e.target, 'editorFocus');
        if (editorFocus?.toLowerCase() !== 'true')
            return;

        debugLog?.log(`onDocumentClick: found data-editor-focus == 'true'`)
        this.focus();
    };

    private onBeforeInput = (e: InputEvent) => {
        debugLog?.log(`onBeforeInput, event:`, e)
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
        debugLog?.log(`onInput, event:`, e)
        this.fixContent();
        this.undoStack.pushThrottled();
        this.updateListUIThrottled();
    }

    // List UI (lists, etc.) support

    private updateListUIThrottled = throttle(() => this.updateListUI(), 250);
    private updateListUI() {
        debugLog?.log(`updateListUI`);
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
        debugLog?.log(`closeListUI`);
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
        debugLog?.log(`restoreSelection`);
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
        debugLog?.log(`expandSelection`);
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
        debugLog?.log(`fixEverything`);
        this.fixContent();
        this.fixSelection();
    }

    private fixSelection() {
        debugLog?.log(`fixSelection`);
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

        const hadFocus = this.hasFocus();
        const parents = listParents(node, this.contentDiv);
        parents.reverse();
        for (let parent of parents) {
            const mention = asMention(parent);
            if (mention) {
                debugLog?.log(`fixSelection: mention:`, mention);
                const newRange = document.createRange();
                const text = getPostMentionText(mention);
                if (text && text.textContent.startsWith(ZeroWidthSpace)) {
                    newRange.setStart(text, 1);
                    newRange.collapse(false);
                }
                else {
                    newRange.setStartAfter(mention);
                    newRange.collapse(false);
                    this.transaction('fixSelection - remove invalid mention', () => {
                        mention.remove();
                    })
                }
                selection.removeAllRanges();
                selection.addRange(newRange);
                if (hadFocus && DeviceInfo.isIos)
                    this.focus();
                this.lastSelectedRange = newRange;
                return;
            }
        }
        return;
    }

    private fixContent() {
        debugLog?.log(`fixContent`);

        // We remove all elements with contentEditable == "false", which
        // aren't followed by "\u8203" character to workaround an issue with
        // typing & deletion of such elements in Chrome Android:
        // - https://github.com/ProseMirror/prosemirror/issues/565#issuecomment-552805191
        const process = (parent: Node) => {
            let mustNormalize = false;
            let skipNode: Node = null;
            for (const node of parent.childNodes) {
                if (node === skipNode) {
                    skipNode = null;
                    continue;
                }

                debugLog?.log('fixContent: processing', node);
                let text = asText(node);
                if (text) {
                    const oldText = text.textContent;
                    const newText = oldText.replace(ZeroWidthSpaceRe, ""); // \u200B (hex) = 8203 (dec)
                    if (newText.length !== oldText.length) {
                        text.textContent = newText;
                        mustNormalize = true;
                    }
                    continue;
                }

                const element = asHTMLElement(node);
                if (!element)
                    continue;

                const mention = asMention(element);
                if (!mention) {
                    process(element);
                    continue;
                }

                text = getPostMentionText(element);
                if (!text || !text.textContent.startsWith(ZeroWidthSpace)) {
                    debugLog?.log('fixContent: removing mention', mention);
                    mention.remove();
                }
                else
                    skipNode = text;
            }

            if (mustNormalize)
                parent.normalize();
        }

        this.transaction('fixContent', () => process(this.contentDiv));
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
    static wrongPrefixRe = /[\p{L}\p{Nd}_@\/`]/u;

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

function castNode<TNode extends Node>(node: Node, nodeType: number): TNode | null {
    if (!node)
        return null;
    if (node.nodeType !== nodeType)
        return null;

    return node as unknown as TNode;
}

function asHTMLElement(node: Node): HTMLElement | null {
    return castNode<HTMLElement>(node, Node.ELEMENT_NODE);
}

function asText(node: Node): Text | null {
    return castNode<Text>(node, Node.TEXT_NODE);
}

function asMention(node: Node): HTMLElement | null {
    const element = node as HTMLElement;
    if (!element)
        return null;

    const contentEditable = element as ElementContentEditable;
    if (!contentEditable.contentEditable)
        return null;

    if (!contentEditable.isContentEditable)
        return element;

    const altIsContentEditable = element?.dataset['contentEditable'];
    if (altIsContentEditable && altIsContentEditable !== 'true')
        return element;

    return null;
}

function getPostMentionText(mention: HTMLElement): Text | null {
    let text = asText(mention.nextSibling);
    while (text && text.textContent.length == 0)
        text = asText(text.nextSibling);
    return text;
}

function cleanupPastedText(text: string, fixDoubleNewLines: boolean): string {
    text = text.replace(ZeroWidthSpaceRe, '');
    text = normalize(text);
    if (fixDoubleNewLines && !text.match(SingleLfRe))
        text = text.replace(DoubleLfRe, '\n');
    // text = text.trim(); // This makes pasting code quite inconvenient
    return text;
}

function normalize(text: string): string {
    return text.normalize().replace(CrlfRe, "\n");
}

function listParents(start: Node, endExclusive: Node): HTMLElement[] {
    const parents = new Array<HTMLElement>();
    let node = start;
    while (node && node !== endExclusive) {
        const eParent = asHTMLElement(node);
        if (eParent)
            parents.push(eParent)
        node = node.parentElement;
    }
    if (!node)
        parents.length = 0;
    return parents;
}
