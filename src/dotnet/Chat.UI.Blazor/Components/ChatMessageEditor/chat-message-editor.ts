import {
    Subject,
    takeUntil,
    debounceTime,
    tap,
    fromEvent
} from 'rxjs';
import { preventDefaultForEvent } from 'event-handling';
import { throttle } from 'promises';
import { AttachmentList } from './attachment-list';
import { MarkupEditor } from '../MarkupEditor/markup-editor';
import { ScreenSize } from '../../../UI.Blazor/Services/ScreenSize/screen-size';
import { localSettings } from '../../../UI.Blazor/Services/Settings/local-settings';
import { Log } from 'logging';
import { DeviceInfo } from 'device-info';

const { debugLog } = Log.get('MessageEditor');

export type PanelMode = 'Normal' | 'Narrow';

export class ChatMessageEditor {
    private readonly backupRequired$ = new Subject<void>();
    private readonly disposed$: Subject<void> = new Subject<void>();
    private blazorRef: DotNet.DotNetObject;
    private readonly editorDiv: HTMLDivElement;
    private readonly postPanelDiv: HTMLDivElement;
    private readonly postButton: HTMLButtonElement;
    private readonly attachButton: HTMLButtonElement;
    private readonly input: HTMLDivElement;
    private readonly attachmentListObserver: MutationObserver;
    private readonly sideNavs: NodeListOf<Element>;
    private readonly sideNavObserver: MutationObserver;
    private markupEditor: MarkupEditor;
    private attachmentList: AttachmentList;
    private attachmentListElement: HTMLDivElement;
    private lastHeight: number;
    private lastWidth: number;
    private isNarrowScreen: boolean = null; // Intended: updateLayout needs this on the first run
    private panelModel: PanelMode = null; // Intended: updateLayout needs this on the first run
    private hasContent: boolean = null; // Intended: updateHasContent needs this on the first run
    private chatId: string;
    private smooth: boolean = false;

    static create(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject): ChatMessageEditor {
        return new ChatMessageEditor(editorDiv, blazorRef);
    }

    constructor(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        let domClassList = document.documentElement.classList;
        this.smooth = !domClassList.contains('device-ios');
        this.editorDiv = editorDiv;
        this.blazorRef = blazorRef;
        this.postPanelDiv = this.editorDiv.querySelector(':scope .post-panel');
        this.postButton = this.postPanelDiv.querySelector(':scope .post-message');
        this.attachButton = this.postPanelDiv.querySelector(':scope .attach-btn');
        this.input = this.postPanelDiv.querySelector(':scope .message-input');

        if (this.smooth)
            editorDiv.classList.add('smooth');

        this.updateLayout();
        this.updateHasContent();

        // Wiring up event listeners
        ScreenSize.event$
            .pipe(takeUntil(this.disposed$))
            .subscribe(this.updateLayout);

        fromEvent(this.input, 'paste')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: ClipboardEvent) => this.onInputPaste(event));

        fromEvent(this.postPanelDiv, 'click')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: MouseEvent) => this.onPostPanelClick(event));

        fromEvent(this.attachButton, 'click')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: MouseEvent) => this.onAttachButtonClick(event));

        this.backupRequired$.pipe(debounceTime(1000), tap(() => this.saveDraft())).subscribe();

        this.attachmentListObserver = new MutationObserver(this.updateAttachmentListState);
        this.attachmentListObserver.observe(this.editorDiv, {
            attributes: true,
            childList: true,
        });

        this.sideNavObserver = new MutationObserver(this.updateEditorFocus);
        this.sideNavs = document.querySelectorAll('.side-nav');
        if (ScreenSize.isNarrow()) {
            this.sideNavs.forEach(panel => {
                if (panel != null) {
                    this.sideNavObserver.observe(panel, {
                        attributes: true,
                        attributeFilter: ['data-side-nav'],
                    });
                }
            });
        }
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.backupRequired$.complete();
        this.disposed$.next();
        this.disposed$.complete();
        if (this.attachmentListElement != null) {
            this.attachmentListElement.removeEventListener('wheel', this.onHorizontalScroll);
        }
        this.attachmentListObserver.disconnect();
        this.sideNavs.forEach(_ => {
            this.sideNavObserver.disconnect();
        });
    }

    // Public methods

    private addAttachmentsObserver() {
        let lastElement = this.attachmentListElement.querySelector('.last-element');
        const callback = (mutationList, observer) => {
            mutationList.forEach(m => {
                m.addedNodes.forEach(e => {
                    if (e.className == 'attachment-item') {
                        lastElement.scrollIntoView({ behavior: 'smooth' });
                    }
                })
            })
        };
        let observer = new MutationObserver(callback);
        observer.observe(this.attachmentListElement, {
            attributes: true,
            childList: true,
            subtree: true,
        });

    }

    private updateAttachmentListState = (mutationList, observer) => {
        mutationList.forEach(m => {
            m.addedNodes.forEach(element => {
                if (element.className == 'attachment-list-wrapper') {
                    if (!this.editorDiv.classList.contains('attachment-mode')) {
                        this.editorDiv.classList.add('attachment-mode');
                    }
                    this.attachmentListElement = this.editorDiv.querySelector('.attachment-list')
                    this.addAttachmentsObserver();
                    fromEvent(this.attachmentListElement, 'wheel')
                        .pipe(takeUntil(this.disposed$))
                        .subscribe((event: WheelEvent) => this.onHorizontalScroll(event));
                }
            });
            m.removedNodes.forEach(element => {
                if (element.className == 'attachment-list-wrapper') {
                    this.editorDiv.classList.remove('attachment-mode');
                    if (this.attachmentListElement != null) {
                        this.attachmentListElement.removeEventListener('wheel', this.onHorizontalScroll);
                    }
                }
            });
        })
    };

    private onHorizontalScroll = ((event: WheelEvent) => {
        preventDefaultForEvent(event);
        this.attachmentListElement.scrollBy({ left: event.deltaY < 0 ? -30 : 30, });
    });

    /** Called by Blazor */
    public onNestedControlsReady(markupEditor: MarkupEditor, attachmentList: AttachmentList)
    {
        this.markupEditor = markupEditor;
        this.markupEditor.changed = () => {
            this.backupRequired$.next();
            this.updateHasContent();
        }
        this.attachmentList = attachmentList;
        this.attachmentList.changed = () => {
            this.updateHasContent();
        }
        this.updateHasContent();
        if (this.isNarrowScreen)
            this.markupEditor.contentDiv.blur(); // We want to see the placeholder on mobile when you open a chat
    }

    /** Called by Blazor */
    public setChatId(chatId: string) {
        this.chatId = chatId;
        this.attachmentList.setChatId(chatId)
        void this.restoreDraft();
    }

    // Event handlers

    private onPostPanelClick = ((event: MouseEvent) => {
        if (event.target === this.postPanelDiv)
            this.markupEditor.focus();
    });

    private onAttachButtonClick = ((event: MouseEvent) => {
        this.attachmentList.showFilePicker();
        if (this.panelModel == 'Narrow') {
            this.markupEditor.focus();
            this.updateHasContent();
        }
    });

    private onReturnFocusOnInput = ((event: MouseEvent) => {
        if (this.panelModel == 'Narrow') {
            debugLog?.log('onReturnFocusOnInput');
            this.markupEditor.focus();
            this.updateHasContent();
        }
    });

    private onInputPaste = async (event: ClipboardEvent) => {
        // Get pasted data via clipboard API
        // We need to handle only files pasting.
        // Text pasting is controlled by markup editor.
        const clipboardData = event.clipboardData;
        let isAdding = false;
        for (const item of clipboardData.items) {
            if (item.kind === 'file') {
                if (!isAdding)
                    preventDefaultForEvent(event); // We can do it only in the sync part of async handler
                isAdding = true;
                const file = item.getAsFile();
                if (await this.attachmentList.add(this.chatId, file))
                    this.updateHasContent();
            }
        }
    };

    // Private methods

    private updateLayout = () => {
        const width = window.visualViewport.width;
        const height = window.visualViewport.height;
        const isNarrowScreen = width < 1024;

        if (this.isNarrowScreen === isNarrowScreen) {
            if (!isNarrowScreen)
                return; // Nothing to update in desktop mode

            if (width != this.lastWidth) {
                // Orientation changed
                this.lastWidth = width;
                this.lastHeight = height;
                return;
            }
            if (height == this.lastHeight)
                return;

            // Maybe mobile keyboard pull-out / pull-in
            const minHeight = Math.min(height, this.lastHeight);
            const maxHeight = Math.max(height, this.lastHeight);
            const keyboardHeight = maxHeight - minHeight;
            debugLog?.log(`updateLayout: keyboardHeight:`, keyboardHeight, '/', maxHeight);
            if (keyboardHeight >= 0.2 * maxHeight) {
                // Mobile keyboard pull-out / pull-in
                const panelMode = Math.abs(height - minHeight) < 0.01 // FP: height == minHeight
                    ? 'Narrow'
                    : 'Normal';
                if (this.panelModel !== panelMode) {
                    this.panelModel = panelMode;
                    if (panelMode === 'Narrow') {
                        this.editorDiv.classList.remove('to-thick');
                        this.editorDiv.classList.add('narrow-panel', 'to-thin');
                    }
                    else {
                        this.editorDiv.classList.remove('narrow-panel', 'to-thin');
                        this.editorDiv.classList.add('to-thick');
                    }
                }
            }
            this.lastHeight = height;
            return;
        }

        this.isNarrowScreen = isNarrowScreen;
        this.lastHeight = height;
        this.lastWidth = width;
        const buttons = this.editorDiv.querySelectorAll(':scope div.chat-audio-panel .btn');
        if (isNarrowScreen)
            buttons.forEach(b => b.addEventListener('click', this.onReturnFocusOnInput));
        else
            buttons.forEach(b => b.removeEventListener('click', this.onReturnFocusOnInput));
    }

    private updateHasContent() {
        const text = this.markupEditor?.getText() ?? '';
        const isTextMode = text != '' || this.attachmentList?.some();
        if (this.hasContent === isTextMode)
            return;

        this.hasContent = isTextMode;
        if (isTextMode) {
            this.editorDiv.classList.remove('default-mode');
            this.editorDiv.classList.add('text-mode');
        } else {
            this.editorDiv.classList.remove('text-mode');
            this.editorDiv.classList.add('default-mode');
        }
        this.endAnimations();
    }

    private updateEditorFocus = (mutationList, observer) => {
        mutationList.forEach(m => {
            if (m.type == 'attributes') {
                let dataValue = m.target.dataset['sideNav'];
                if (dataValue == 'open' && this.markupEditor.hasFocus()) {
                    this.markupEditor.blur();
                    return;
                }
            }
        });
    }

    private endAnimations(): void {
        const playbackWrapper = this.editorDiv.querySelector('.playback-wrapper');
        if (!playbackWrapper)
            return;
        playbackWrapper.classList.replace('listen-on-to-off', 'listen-off');
        playbackWrapper.classList.replace('listen-off-to-on', 'listen-on');
    }

    private async saveDraft(): Promise<void> {
        if (!this.chatId)
            return;

        const text = this.markupEditor.getHtml();
        const keys = [`MessageDraft.${this.chatId}.Html`];
        const values = [!!text ? text : null];
        await localSettings.setMany(keys, values);
    }

    private async restoreDraft(): Promise<void> {
        const [html] = this.chatId && await localSettings.getMany([`MessageDraft.${this.chatId}.Html`]);
        this.markupEditor.setHtml(html ?? "", ScreenSize.isWide() && !DeviceInfo.isTouchCapable);
    }
}
