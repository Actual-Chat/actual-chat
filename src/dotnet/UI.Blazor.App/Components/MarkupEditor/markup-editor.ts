// TODO: remove eslint-disables and fix errors
/* eslint-disable @typescript-eslint/no-unnecessary-condition,@typescript-eslint/no-deprecated,@typescript-eslint/no-unsafe-argument,@typescript-eslint/no-unsafe-member-access */
import { DeviceInfo } from 'device-info';
import { getOrInheritData } from 'dom-helpers';
import { throttle } from 'actuallab-core';
import { preventDefaultForEvent } from 'event-handling';
import { UndoStack } from './undo-stack';
import { getLogs } from 'logging';
import { ScreenSize } from '../../../UI.Blazor/Services/ScreenSize/screen-size';
import { fastRaf } from 'fast-raf';

const { debugLog, errorLog } = getLogs('MarkupEditor');

const MentionListId = '@';
const ZeroWidthSpace = '\u200b';
const ZeroWidthSpaceRe = new RegExp(ZeroWidthSpace, 'g');
const CrlfRe = /\r\n/g;
const RegexEscapeRe = /[.*+?^${}()|[\]\\]/g;
const IdCharRe = /[\p{L}\p{N}_:.%~\-]/u;
const emptyHtmlVariants = new Set<string>(['', '\n', '\r\n', '<br>', '<br >', '<br/>', '<br />']);

export class MarkupEditor {
    static create(
        editorDiv: HTMLDivElement,
        blazorRef: DotNet.DotNetObject,
        autofocus: boolean,
    ) {
        return new MarkupEditor(editorDiv, blazorRef, autofocus);
    }

    public readonly contentDiv: HTMLDivElement;
    public hasContent = false;
    public changed: (html: string, text: string) => void = () => { /* empty */ };

    private isContentDivInitialized = false;
    private lastSelectedRange?: Range;
    private undoStack: UndoStack<string>;
    private currentTransaction: (() => void) | null = null;
    private lastHtml: string | null = null;

    private readonly listHandlers: ListHandler[];
    private listHandler?: ListHandler;
    private listFilter = '';
    // Anchor of the '@' the user explicitly dismissed the picker for; it stays suppressed
    // until the match at that anchor is gone (caret moved before it, or the '@' was deleted).
    private dismissedMatch: { node: Node; offset: number } | null = null;
    // While > Date.now(), suppress the mention picker — set on paste so pasted "@…" never auto-opens it.
    private pasteGuardUntil = 0;
    private sizeObserver: ResizeObserver | null = null;
    private parent: HTMLElement | null = null;

    constructor(
        public readonly editorDiv: HTMLDivElement,
        public readonly blazorRef: DotNet.DotNetObject,
        autofocus: boolean,
    ) {
        debugLog?.log(`constructor`);

        this.contentDiv = editorDiv.querySelector(':scope > .editor-content')!;
        this.listHandlers = [new MentionListHandler(this)]
        this.undoStack = new UndoStack<string>(
            () => normalize(this.contentDiv.innerHTML),
            value => {
                this.transaction('init', () => {
                    // To make sure both undo and redo stacks have something
                    document.execCommand('insertHTML', false, '&#8203');
                    document.execCommand('insertHTML', false, '&#8203');
                    document.execCommand('undo', false);
                    this.setHtml(value, false, false);
                });
            },
            (x: string, y: string) => x === y,
            333);

        // Attach listeners & observers
        this.contentDiv.addEventListener('focus', this.onFocus)
        this.contentDiv.addEventListener('keydown', this.onKeyDown);
        this.contentDiv.addEventListener('keypress', this.onKeyPress);
        this.contentDiv.addEventListener('paste', this.onPaste);
        this.contentDiv.addEventListener('beforeinput', this.onBeforeInput);
        this.contentDiv.addEventListener('input', this.onInput);
        document.addEventListener('selectionchange', this.onSelectionChange);
        document.addEventListener('click', this.onDocumentClick);

        this.updateHasContent(this.contentDiv.innerHTML);
        if (autofocus)
            this.focus();

        this.parent = this.editorDiv.parentElement!;
        if (parent) {
            this.sizeObserver = new ResizeObserver(this.onResize);
            this.sizeObserver.observe(this.editorDiv);
        }
    }

    public dispose() {
        this.contentDiv.removeEventListener('focus', this.onFocus)
        this.contentDiv.removeEventListener('keydown', this.onKeyDown);
        this.contentDiv.removeEventListener('keypress', this.onKeyPress);
        this.contentDiv.removeEventListener('paste', this.onPaste);
        this.contentDiv.removeEventListener('beforeinput', this.onBeforeInput);
        this.contentDiv.removeEventListener('input', this.onInput);
        document.removeEventListener('selectionchange', this.onSelectionChange);
        document.removeEventListener('click', this.onDocumentClick);
        this.sizeObserver?.disconnect();
    }

    private onResize: ResizeObserverCallback = (entries) => {
        const parent = this.parent;
        const editor = this.editorDiv;
        if (!parent || !editor)
            return;

        let contentHeight = 0;
        let lineHeight = 0;
        let maxAutoHeight = 0;

        fastRaf({
            read: () => {
                let maxHeight = 0;
                for (const entry of entries) {
                    maxHeight = Math.max(maxHeight, entry.contentRect.height);
                }

                const style = getComputedStyle(editor);
                lineHeight = parseFloat(style.lineHeight);
                contentHeight = maxHeight;
                maxAutoHeight = ScreenSize.isNarrow() ? 0 : lineHeight * 2;
            },
            write: () => {
                if (contentHeight <= maxAutoHeight) {
                    parent.style.transition = 'height 0.25s ease';
                    parent.style.height = 'auto';
                } else {
                    const newHeight = `${contentHeight}px`;
                    if (parent.style.height !== newHeight) {
                        parent.style.transition = 'height 0.25s ease';
                        parent.style.height = newHeight;
                    }
                }
            },
        });
    };

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
                    this.updateHasContent(html);
                    this.changed(html, this.getText());
                }
            }
        }
    }

    public focus(force = false) {
        if (!force && this.hasFocus()){
            debugLog?.log(`focus: already in focus, skipped`);
            return;
        }

        debugLog?.log('focus: setting focus on contentDiv');
        this.contentDiv.focus();

        // TODO: remove if everything works fine on all platforms
        // if (!DeviceInfo.isIos) {
        //     debugLog?.log('focus: setting focus on contentDiv');
        //     this.contentDiv.focus();
        //     return;
        // }
        //
        // // The code blow makes sure mobile keyboard is shown on iOS.
        // // It works only after the first interaction.
        // debugLog?.log('focus: using iOS/force workaround');
        // const contentDiv = this.contentDiv;
        // const tempInput = document.createElement('input');
        // tempInput.style.position = 'absolute';
        // tempInput.style.top = (contentDiv.offsetTop + 7) + 'px';
        // tempInput.style.left = contentDiv.offsetLeft + 'px';
        // tempInput.style.height = '0';
        // tempInput.style.opacity = '0';
        // document.body.appendChild(tempInput);
        // tempInput.focus();
        // Timeout.startRegular(100, () => {
        //     contentDiv.focus();
        //     contentDiv.click();
        //     document.body.removeChild(tempInput);
        // });
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

    public isEditable(mustBeEditable: boolean | null = null): boolean {
        if (mustBeEditable !== null) {
            if (this.contentDiv.isContentEditable == mustBeEditable)
                return false;

            this.contentDiv.setAttribute('contenteditable', mustBeEditable ? 'true' : 'false');
        }
        const isEditable = this.contentDiv.isContentEditable;
        debugLog?.log(`isEditable(${mustBeEditable}) -> ${isEditable}`);
        return isEditable;
    }

    /** Called by Blazor */
    public getText() {
        // Strip ZWSPs the converter inserts around mentions as caret anchors —
        // they must not leak into the saved markup (they'd multiply each round-trip).
        let text = this.contentDiv.innerText.replace(ZeroWidthSpaceRe, '');
        // If the user deleted the trailing space, the mention id ends up glued
        // to the next word and the parser's IdChar.AtLeastOnceString() would
        // swallow it. For every known mention id in the DOM, insert a single
        // space when the id is immediately followed by an IdChar.
        const ids = new Set<string>();
        this.contentDiv.querySelectorAll('.editor-mention').forEach(el => {
            const id = el.getAttribute('data-id');
            if (id) ids.add(id);
        });
        for (const id of ids) {
            const escaped = id.replace(RegexEscapeRe, '\\$&');
            const re = new RegExp('(`' + escaped + ')(?=' + IdCharRe.source + ')', 'gu');
            text = text.replace(re, '$1 ');
        }
        return text;
    }

    public getHtml() {
        return this.contentDiv.innerHTML;
    }

    public setHtml(html: string, mustFocus = false, clearUndoStack = true) {
        this.dismissedMatch = null;
        this.transaction('setHtml', () => {
            this.contentDiv.innerHTML = html;
            this.fixEverything();
        })
        // Moving cursor to the end, brings focus to the editor.
        // Will do it only when html is not empty or focus is requested explicitly.
        if (html !== '' || mustFocus)
            this.moveCursorToTheEnd();
        if (clearUndoStack)
            this.undoStack.clear();
    }

    public insertHtml(html: string, listId?: string, needToFocus = true) {
        if (!this.hasFocus() && needToFocus) {
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
    }

    public beginMention() {
        const mentionListWrapper = document.querySelector('.mention-list-wrapper');
        if (mentionListWrapper == null)
            return;

        this.dismissedMatch = null;

        if (!this.hasFocus()) {
            this.focus();
            this.restoreSelection();
        }

        const isMentionListOpen = !mentionListWrapper.classList.contains('non-visible');
        const { selection, range, caretPosition, prevChar } = this.getPrevCharInfo();
        let newCaretPosition = caretPosition;
        if (!isMentionListOpen) {
            const html = prevChar.trim() != '' ? ' @' : '@';
            this.insertHtml(html);
        }
        else {
            if (prevChar == '@') {
                newCaretPosition = caretPosition - 1;
                const oldText = range.endContainer.textContent ?? '';
                const newText = oldText.substring(0, caretPosition - 1) + oldText.substring(caretPosition);
                this.setHtml(newText);
            }
            const newRange = document.createRange();
            if (this.contentDiv.childNodes.length > 0)
                newRange.setStart(this.contentDiv.childNodes[0], newCaretPosition);
            newRange.collapse(true);
            selection.removeAllRanges();
            selection.addRange(newRange);
        }
    }

    private getPrevCharInfo(): { selection: Selection; caretPosition: number; range: Range; prevChar: string } {
        let prevChar = '';
        let range: Range;
        let clonedRange: Range = document.createRange();
        let caretPosition = 0;
        const selection = window.getSelection()!; // We dont care about hidden iframes
        if (selection && selection.rangeCount > 0) {
            range = selection.getRangeAt(0);
            clonedRange = range.cloneRange();
            clonedRange.selectNodeContents(this.contentDiv);
            clonedRange.setEnd(range.endContainer, range.endOffset);
            caretPosition = clonedRange.toString().length;
            if (caretPosition > 0)
                prevChar = clonedRange.toString().charAt(caretPosition - 1);
        }
        return {
            selection: selection,
            range: clonedRange,
            caretPosition: caretPosition,
            prevChar: prevChar,
        };
    }

    public moveCursorToTheEnd() {
        const range = document.createRange();
        range.selectNodeContents(this.contentDiv);
        range.collapse(false); // false means collapse to end rather than the start
        const selection = document.getSelection()!;
        selection.removeAllRanges();
        selection.addRange(range);
    }

    // Backend method invokers

    private async onPost(): Promise<void> {
        await this.blazorRef.invokeMethodAsync('OnPost', this.getText());
    }

    private async onCancel(): Promise<void> {
        await this.blazorRef.invokeMethodAsync('OnCancel');
    }

    private async onOpenPrevious(): Promise<void> {
        await this.blazorRef.invokeMethodAsync('OnOpenPrevious');
    }

    private async onListCommand(listId: string, command: ListCommand): Promise<boolean> {
        debugLog?.log(`onListCommand(): listId:`, listId, ', command:', command.kind, ', filter:', command.filter);
        return await this.blazorRef.invokeMethodAsync<boolean>('OnListCommand', listId, command);
    }

    // Event handlers

    private onFocus = () => {
        debugLog?.log('onFocus');
        if (!this.isContentDivInitialized) {
            this.isContentDivInitialized = true;
            this.transaction('onFocus - init', () => {
                document.execCommand('insertBrOnReturn', false, 'true');
                document.execCommand('styleWithCSS', false, 'false');
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
                ok(); return;
            }
            if (e.code === 'Down' || e.code === 'ArrowDown') {
                void this.onListCommand(listHandler.listId, new ListCommand(ListCommandKind.GoToNextItem));
                ok(); return;
            }
            if (e.code === 'Enter' || e.code === 'NumpadEnter') {
                // The picker may have nothing to insert (e.g. "No matches found"), in which case
                // Enter must fall back to its normal behavior instead of being silently eaten.
                const isShiftKey = DeviceInfo.isMobile ? true : e.shiftKey;
                const isPost = e.ctrlKey || e.metaKey || e.altKey || !isShiftKey;
                void this.onListCommand(listHandler.listId, new ListCommand(ListCommandKind.InsertItem))
                    .then(isHandled => {
                        if (isHandled)
                            return;

                        this.closeListUI();
                        if (isPost)
                            void this.onPost();
                        else
                            this.insertLineFeed();
                    });
                ok(); return;
            }
            if (e.code === 'Escape') {
                this.dismissList();
                ok(); return;
            }
        }

        // Cancel on escape
        if (e.code === 'Escape') {
            void this.onCancel();
            ok(); return;
        }

        // Up key & empty content = open previous
        if ((e.code === 'Up' || e.code === 'ArrowUp') && !this.hasContent) {
            void this.onOpenPrevious();
            ok(); return;
        }
    }

    private onKeyPress = (e: KeyboardEvent) => {
        debugLog?.log(`onKeyPress, code = "${e.code}", event:`, e)

        const ok = () => preventDefaultForEvent(e);

        // Suppress bold, italic, and underline shortcuts
        if ((e.ctrlKey || e.metaKey)) {
            const lowerCaseCode = e.code.toLowerCase();
            if (lowerCaseCode === 'b' || lowerCaseCode === 'i' || lowerCaseCode == 'u')
            { ok(); return; }
        }

        // Post + fix the new line insertion when cursor is in the end of the document
        if (e.code === 'Enter' || e.code === 'NumpadEnter') {
            const isShiftKey = DeviceInfo.isMobile ? true : e.shiftKey;
            const isPost = e.ctrlKey || e.metaKey || e.altKey || !isShiftKey;
            if (isPost) {
                void this.onPost()
                ok(); return;
            }

            this.insertLineFeed();
            ok(); return;
        }
    }

    private onPaste = (e: ClipboardEvent) => {
        debugLog?.log(`onPaste, event:`, e)
        const ok = () => preventDefaultForEvent(e);

        const data = e.clipboardData;
        if (!data)
        { ok(); return; }

        // Never let a paste auto-open the mention picker (the inserted text may contain "@…").
        this.pasteGuardUntil = Date.now() + 1500;

        // Our own copies carry the exact source markup in data-voxt-markup — reconstruct mentions from it.
        const voxtMarkup = extractVoxtMarkup(data.getData('text/html'));
        if (voxtMarkup) {
            ok();
            void this.blazorRef.invokeMethodAsync<string>('ParseTextToHtml', voxtMarkup)
                .then(html => {
                    this.pasteGuardUntil = Date.now() + 300; // re-arm to cover the post-insert selectionchange
                    if (!html)
                        this.transaction('onPaste', () => this.insertTextAtCursor(voxtMarkup));
                    else
                        this.insertHtml(html);
                })
                .catch((err: unknown) => {
                    errorLog?.log('onPaste: ParseTextToHtml (voxt-markup) failed', err);
                    this.transaction('onPaste', () => this.insertTextAtCursor(voxtMarkup));
                });
            return;
        }

        const plainText = data.getData('text');
        const text = cleanupPastedText(plainText);
        const url = data.getData('text/uri-list');
        const concatenatedText = text?.length && url?.length ? `${text}\n${url}` : (text || url);

        if (!concatenatedText) { ok(); return; }

        const looksLikeMarkup = concatenatedText.includes('@`') || /@[a-z]:[\w%.\-:]+/.test(concatenatedText);
        if (!looksLikeMarkup) {
            this.transaction('onPaste', () => {
                this.insertTextAtCursor(concatenatedText);
            });
            ok();
            return;
        }

        ok();
        void this.blazorRef.invokeMethodAsync<string>('ParseTextToHtml', concatenatedText)
            .then(html => {
                this.pasteGuardUntil = Date.now() + 300; // re-arm to cover the post-insert selectionchange
                if (!html) {
                    this.transaction('onPaste', () => this.insertTextAtCursor(concatenatedText));
                    return;
                }
                this.insertHtml(html);
            })
            .catch((err: unknown) => {
                errorLog?.log('onPaste: ParseTextToHtml failed', err);
                this.transaction('onPaste', () => this.insertTextAtCursor(concatenatedText));
            });
    }

    public insertText(text: string) {
        if (!this.hasFocus()) {
            this.focus();
            this.restoreSelection();
        }
        this.transaction('insertText', () => {
            this.insertTextAtCursor(text);
        });
    }

    private insertTextAtCursor(text)
    {
        const selection = window.getSelection()!;
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
        this.restoreSelection();
    };

    private onBeforeInput = (e: InputEvent) => {
        debugLog?.log(`onBeforeInput, event:`, e)
        const ok = () => e.preventDefault();

        switch (e.inputType) {
        case 'historyUndo':
            this.undoStack.undo();
            { ok(); return; }
        case 'historyRedo': {
            this.undoStack.redo();
            ok(); return;
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

    private updateHasContent(html: string): void {
        /* Old check:
        const childNodes = this.contentDiv.childNodes;
        const theOnlyChild = childNodes.length === 1 ? childNodes.item(0) : null;
        this.hasContent = childNodes.length !== 0 && !(theOnlyChild?.nodeName === 'BR' && theOnlyChild?.nodeValue == null);
        */
        const stripped = html.replace(ZeroWidthSpaceRe, '');
        const hasContent = stripped.length > 8 || !emptyHtmlVariants.has(stripped.toLowerCase());
        if (this.hasContent === hasContent)
            return;

        this.hasContent = hasContent;
        const classList = this.contentDiv.classList;
        if (hasContent) {
            classList.remove('is-empty');
            classList.add('has-content');
        }
        else {
            classList.remove('has-content');
            classList.add('is-empty');
        }
    }

    // List UI (lists, etc.) support

    // Closes the picker and keeps it closed for the '@' it was showing - called on Escape
    // and from Blazor when the user closes the picker via its own UI.
    public dismissList() {
        const listHandler = this.listHandler;
        if (listHandler) {
            const cursorRange = this.getCursorRange();
            const matchRange = cursorRange ? listHandler.getMatchRange(cursorRange) : null;
            this.dismissedMatch = matchRange
                ? { node: matchRange.startContainer, offset: matchRange.startOffset }
                : null;
        }
        this.closeListUI();
    }

    private insertLineFeed() {
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
    }

    private updateListUIThrottled = throttle(() => this.updateListUI(), 250);
    private updateListUI() {
        debugLog?.log(`updateListUI`);
        if (Date.now() < this.pasteGuardUntil) {
            this.closeListUI();
            return;
        }
        const cursorRange = this.getCursorRange();
        if (!cursorRange) {
            this.closeListUI();
            return;
        }

        let listHandlers = this.listHandlers;
        const listHandler = this.listHandler;
        if (listHandler)
            listHandlers = [listHandler].concat(listHandlers.filter(h => h != listHandler));
        for (const h of listHandlers)
            this.tryUseListHandler(h, cursorRange);
    }

    private closeListUI() {
        debugLog?.log(`closeListUI`);
        const listHandler = this.listHandler;
        if (!listHandler)
            return;

        this.listHandler = undefined;
        this.listFilter = '';
        void this.onListCommand(listHandler.listId, new ListCommand(ListCommandKind.Hide));
    }

    // Helpers

    private tryUseListHandler(listHandler: ListHandler, cursorRange: Range): boolean {
        const matchRange = listHandler.getMatchRange(cursorRange);
        const matchText = matchRange ? listHandler.getMatchText(matchRange) : '';
        const isActive = listHandler == this.listHandler;
        if (!matchRange || !matchText) {
            this.dismissedMatch = null;
            if (isActive)
                this.closeListUI();
            return false;
        }

        const dismissedMatch = this.dismissedMatch;
        if (dismissedMatch
            && dismissedMatch.node === matchRange.startContainer
            && dismissedMatch.offset === matchRange.startOffset) {
            if (isActive)
                this.closeListUI();
            return false;
        }

        const listFilter = listHandler.getFilter(matchText);
        if (!isActive) {
            if (!listHandler.canAutoOpen(matchText))
                return false;

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
        const selection = document.getSelection()!;
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

        const selection = document.getSelection()!;
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

        const selection = document.getSelection()!;
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
        const selection = document.getSelection()!;
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
        for (const parent of parents) {
            const mention = asMention(parent);
            if (mention) {
                debugLog?.log(`fixSelection: mention:`, mention);
                const newRange = document.createRange();
                const text = getPostMentionText(mention);
                if (text?.textContent?.startsWith(ZeroWidthSpace)) {
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
            let skipNode: Node | null = null;
            for (const node of parent.childNodes) {
                if (node === skipNode) {
                    skipNode = null;
                    continue;
                }

                debugLog?.log('fixContent: processing', node);
                let text = asText(node);
                if (text) {
                    const oldText = text.textContent;
                    const newText = oldText?.replace(ZeroWidthSpaceRe, ''); // \u200B (hex) = 8203 (dec)
                    if (newText?.length !== oldText?.length) {
                        text.textContent = newText ?? null;
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
                if (!text?.textContent?.startsWith(ZeroWidthSpace)) {
                    debugLog?.log('fixContent: removing mention', mention);
                    mention.remove();
                } else
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

    public getMatchText(matchRange: Range): string {
        const textNode = matchRange.startContainer as Text;
        if (!textNode)
            return '';

        const text = textNode.textContent;
        return text?.substring(matchRange.startOffset, matchRange.endOffset) ?? '';
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
        const startOffset = this.getMatchStart(textNode.textContent ?? '', endOffset);
        return (startOffset == null || startOffset >= endOffset) ? 0 : endOffset - startOffset;
    }

    // Whether this match may open the list; a failing match still keeps an open one alive.
    public canAutoOpen(matchText: string): boolean {
        return matchText.length > 0;
    }

    public abstract getMatchStart(text: string, endOffset: number): number | null;
}

class MentionListHandler extends ListHandler {
    // A whitelist, not a blacklist: no list of "wrong" prefixes stays complete, and a stray "@"
    // inside a word ("test-@actual.chat") must not start a mention.
    static okPrefixRe = /[\s(\[{"'«\u200b]/u;
    // Everything \s matches except U+2005, which joins the words of a name in readable markup
    // (MentionMarkup.ReadableSpace) and therefore is not a word boundary here.
    static separatorRe = /[^\S\u2005]/u;
    static separatorRunRe = /[^\S\u2005]+/gu;
    static MaxWordCount = 3;

    constructor(editor: MarkupEditor) {
        super(MentionListId, editor);
    }

    // Strip the leading '@' and outer quotes if present, then normalize the
    // multi-word separator (U+2005) to ASCII space so the picker's multi-word
    // matcher sees `Voxt Announcements` regardless of which form was typed.
    public getFilter = (matchText: string) => {
        let s = matchText.substring(1);
        if (s.startsWith('"')) {
            s = s.substring(1);
            if (s.endsWith('"'))
                s = s.substring(0, s.length - 1);
        }
        return s.replace(/\u2005/g, ' ');
    };

    // Only a match with no separator in it may open the list; once open, getMatchStart keeps
    // matching across separators, so a space typed while the list is up doesn't close it.
    public canAutoOpen(matchText: string): boolean {
        return matchText.length > 0 && !MentionListHandler.separatorRe.test(matchText);
    }

    public getMatchStart(text: string, endOffset: number): number | null {
        let i = endOffset - 1;
        for (; i >= 0; i--) {
            const c = text[i];
            if (c == '\n' || c == '/')
                return null;

            if (c == '@')
                break;
        }

        if (i < 0)
            return null;

        if (i > 0 && !MentionListHandler.okPrefixRe.test(text[i - 1]))
            return null;

        // A separator right after '@' means this isn't a mention at all, wherever the caret is.
        const next = text[i + 1];
        if (next != null && MentionListHandler.separatorRe.test(next))
            return null;

        // Past MaxWordCount the user is writing prose, not picking a mention.
        const wordCount = text.substring(i + 1, endOffset).split(MentionListHandler.separatorRunRe).length;
        if (wordCount > MentionListHandler.MaxWordCount)
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

// eslint-disable-next-line @typescript-eslint/no-unnecessary-type-parameters
function castNode<TNode extends Node>(node: Node | null, nodeType: number): TNode | null {
    if (!node)
        return null;

    if (node.nodeType !== nodeType)
        return null;

    return node as unknown as TNode;
}

function asHTMLElement(node: Node): HTMLElement | null {
    return castNode<HTMLElement>(node, Node.ELEMENT_NODE);
}

function asText(node: Node | null): Text | null {
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
    if (!mention?.nextSibling)
        return null;

    let text = asText(mention.nextSibling);
    while (text && text.textContent?.length == 0)
        text = asText(text.nextSibling);
    return text;
}

// Pulls the original markup out of clipboard HTML produced by our copy action (SelectionUI).
function extractVoxtMarkup(html: string | null): string | null {
    if (!html?.includes('data-voxt-markup'))
        return null;
    try {
        const doc = new DOMParser().parseFromString(html, 'text/html');
        return doc.querySelector('[data-voxt-markup]')?.getAttribute('data-voxt-markup') ?? null;
    } catch {
        return null;
    }
}

function cleanupPastedText(text: string): string {
    // Strip zero-width spaces (they smuggle in via formatted HTML paste) and
    // normalize \r\n → \n so the markup parser sees a single line-ending style.
    // The previous "collapse \n\n → \n when no single \n is present" heuristic
    // was buggy (its single-LF probe used a regex whose `^`/`$` were treated as
    // literal characters, so it mis-fired and collapsed real paragraph breaks).
    return normalize(text.replace(ZeroWidthSpaceRe, ''));
}

function normalize(text: string): string {
    return text.normalize().replace(CrlfRe, '\n');
}

function listParents(start: Node, endExclusive: Node): HTMLElement[] {
    const parents = new Array<HTMLElement>();
    let node: Node | null = start;
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
