import { DeviceInfo } from 'device-info';
import {
    Subject,
    takeUntil,
    debounceTime,
    tap,
    fromEvent
} from 'rxjs';
import { preventDefaultForEvent } from 'event-handling';
import { MarkupEditor } from '../MarkupEditor/markup-editor';
import { ScreenSize } from '../../../UI.Blazor/Services/ScreenSize/screen-size';
import { localSettings } from '../../../UI.Blazor/Services/Settings/local-settings';
import { Log } from 'logging';
import { AttachmentWebFilePicker, AttachmentWebFilePickerBackend } from './attachment-web-file-picker';

const { debugLog, infoLog, warnLog } = Log.get('MessageEditor');

export type PanelMode = 'Normal' | 'Narrow';

export class ChatMessageEditor {
    private readonly isSmooth: boolean = false;
    private readonly backupRequired$ = new Subject<void>();
    private readonly disposed$: Subject<void> = new Subject<void>();
    private readonly editorDiv: HTMLDivElement;
    private readonly postPanelDiv: HTMLDivElement;
    private readonly input: HTMLDivElement;
    private readonly filePickerInput: HTMLInputElement;
    private readonly filePickerBackend: AttachmentWebFilePickerBackend;
    private readonly filePicker: AttachmentWebFilePicker;
    private readonly postPanelHeightObserver: ResizeObserver;
    private readonly attachmentListObserver: MutationObserver;
    private readonly sideNavs: NodeListOf<Element>;
    private readonly sideNavObserver: MutationObserver;
    private markupEditor: MarkupEditor;
    private attachmentListElement: HTMLDivElement | null;
    private lastHeight: number;
    private lastWidth: number;
    private isNarrowScreen: boolean | null = null; // Intended: updateLayout needs this on the first run
    private panelModel: PanelMode | null = null; // Intended: updateLayout needs this on the first run
    private hasContent: boolean | null = null; // Intended: updateHasContent needs this on the first run
    private chatId: string;
    private hasAttachments: boolean;

    static create(editorDiv: HTMLDivElement, filePickerBlazorRef: DotNet.DotNetObject): ChatMessageEditor {
        return new ChatMessageEditor(editorDiv, filePickerBlazorRef);
    }

    constructor(editorDiv: HTMLDivElement, filePickerBlazorRef: DotNet.DotNetObject) {
        let domClassList = document.documentElement.classList;
        this.editorDiv = editorDiv;
        this.postPanelDiv = this.editorDiv.querySelector(':scope .post-panel')!;
        this.input = this.postPanelDiv.querySelector(':scope .message-input')!;
        this.filePickerBackend = new AttachmentWebFilePickerBackend(filePickerBlazorRef);
        this.filePickerInput = this.editorDiv.querySelector(':scope .attachment-web-file-picker')!;
        this.filePicker = new AttachmentWebFilePicker(this.filePickerBackend, this.filePickerInput);
        this.isSmooth = !domClassList.contains('device-ios');
        if (this.isSmooth)
            editorDiv.classList.add('smooth');

        this.updateLayout();
        this.updateHasContent();

        // Wiring up event listeners
        ScreenSize.event$
            .pipe(takeUntil(this.disposed$))
            .subscribe(this.updateLayout);

        this.backupRequired$.pipe(debounceTime(1000), tap(() => this.saveDraft())).subscribe();

        this.postPanelHeightObserver = new ResizeObserver(this.updatePostPanelBorderRadius);
        this.postPanelHeightObserver.observe(this.postPanelDiv);

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
        let lastElement = this.attachmentListElement?.querySelector('.last-element');
        if (lastElement == null) {
            warnLog?.log('addAttachmentsObserver: last-element not found');
            return;
        }

        const callback: MutationCallback = (mutations: MutationRecord[]): void => {
            mutations.forEach(m => {
                m.addedNodes.forEach(e => {
                    if ((e as HTMLElement).className == 'attachment-item') {
                        lastElement.scrollIntoView({ behavior: 'smooth' });
                    }
                })
            })
        };
        let observer = new MutationObserver(callback);
        observer.observe(this.attachmentListElement!, {
            attributes: true,
            childList: true,
            subtree: true,
        });

    }

    private updatePostPanelBorderRadius: ResizeObserverCallback = (entries) => {
        let clsLst = this.postPanelDiv.classList;
        entries.forEach(entry => {
            let height = entry.contentRect.height;
            if (height > 90) {
                if (!clsLst.contains('sharp-corners'))
                    clsLst.add('sharp-corners');
            } else {
                clsLst.remove('sharp-corners');
            }
        });
    };

    private updateAttachmentListState: MutationCallback = (mutationList, observer) => {
        mutationList.forEach(m => {
            m.addedNodes.forEach(element => {
                if ((element as HTMLElement).className == 'attachment-list-wrapper') {
                    if (!this.editorDiv.classList.contains('attachment-mode')) {
                        this.editorDiv.classList.add('attachment-mode');
                    }
                    this.attachmentListElement = this.editorDiv.querySelector('.attachment-list')!;
                    this.addAttachmentsObserver();
                    fromEvent(this.attachmentListElement, 'wheel')
                        .pipe(takeUntil(this.disposed$))
                        .subscribe((event: WheelEvent) => this.onHorizontalScroll(event));
                }
            });
            m.removedNodes.forEach(element => {
                if ((element as HTMLElement).className == 'attachment-list-wrapper') {
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
        this.attachmentListElement?.scrollBy({ left: event.deltaY < 0 ? -30 : 30, });
    });

    /** Called by Blazor */
    public notifyAttachmentListChanged(hasAttachments: boolean) {
        this.hasAttachments = hasAttachments;
        this.updateHasContent();
    }

    /** Called by Blazor */
    public onNestedControlsReady(markupEditor: MarkupEditor)
    {
        this.markupEditor = markupEditor;

        fromEvent(this.postPanelDiv, 'click')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: MouseEvent) => this.onPostPanelClick(event));
        fromEvent(this.input, 'paste')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: ClipboardEvent) => this.onInputPaste(event));

        this.markupEditor.changed = () => {
            this.backupRequired$.next();
            this.updateHasContent();
        }
        this.updateHasContent();
        if (this.isNarrowScreen)
            this.markupEditor.contentDiv.blur(); // We want to see the placeholder on mobile when you open a chat
    }

    /** Called by Blazor */
    public setChatId(chatId: string) {
        this.chatId = chatId;
        void this.restoreDraft();
    }

    /** Called by Blazor */
    public showWebFilePickerDialog = ((acceptTypes: string)=> {
        void this.filePicker.showFilePicker(acceptTypes);
        if (this.panelModel == 'Narrow') {
            this.markupEditor.focus();
            this.updateHasContent();
        }
    });

    // Event handlers

    private onPostPanelClick = ((event: MouseEvent) => {
        if (event.target === this.postPanelDiv)
            this.markupEditor.focus();
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
        if (!clipboardData)
            return;

        let isAdding = false;
            for (const item of clipboardData.items) {
            if (item.kind === 'file') {
                if (!isAdding)
                    preventDefaultForEvent(event); // We can do it only in the sync part of async handler
                isAdding = true;
                const file = item.getAsFile();
                if (!file)
                    continue; // Should not happen, but just in case

                void this.filePickerBackend.add(file, null);
            }
        }
    };

    // Private methods

    private updateLayout = () => {
        const width = window.visualViewport?.width ?? window.innerWidth;
        const height = window.visualViewport?.height ?? window.innerHeight;
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
        const hasText = this.markupEditor?.hasContent ?? false;
        const hasContent = hasText || this.hasAttachments;
        if (this.hasContent === hasContent)
            return;

        this.hasContent = hasContent;
        if (hasContent) {
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
        if (!!text)
            await localSettings.setMany(keys, [text]);
    }

    private async restoreDraft(): Promise<void> {
        const [html] = this.chatId && await localSettings.getMany([`MessageDraft.${this.chatId}.Html`]);
        this.markupEditor.setHtml(html ?? "", ScreenSize.isWide() && !DeviceInfo.isTouchCapable);
    }
}
