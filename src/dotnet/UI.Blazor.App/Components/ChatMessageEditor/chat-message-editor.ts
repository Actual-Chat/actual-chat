// TODO: remove eslint-disables and fix errors
/* eslint-disable @typescript-eslint/no-unused-vars,@typescript-eslint/no-unnecessary-condition,@typescript-eslint/require-await,@typescript-eslint/no-unsafe-assignment,@typescript-eslint/no-misused-promises,@typescript-eslint/no-unsafe-call,@typescript-eslint/no-unsafe-member-access */
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
import { CompactLayout } from 'compact-layout';
import { getLogs } from 'logging';
import { AttachmentWebFilePicker, AttachmentWebFilePickerBackend, PickFileResult } from './attachment-web-file-picker';

const { debugLog, warnLog } = getLogs('MessageEditor');

const LargeTextSuggestFileThreshold = 4_000;
const LargeTextAutoFileThreshold = 32_000;
const LargeTextPasteFileName = 'pasted.txt';
const CanConvertToFileClass = 'text-can-convert-to-file';

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
    private lastWidth: number;
    private isNarrowScreen: boolean | null = null; // Intended: updateLayout needs this on the first run
    private panelModel: PanelMode | null = null; // Intended: updateLayout needs this on the first run
    private heightAtFocus: number | null = null;
    private hasContent: boolean | null = null; // Intended: updateHasContent needs this on the first run
    private chatId: string;
    private hasAttachments: boolean;
    private dragDropOverlay: HTMLDivElement | null = null;
    private dragCounter = 0;
    private chatPanel: HTMLElement | null = null;

    static create(editorDiv: HTMLDivElement, filePickerBlazorRef: DotNet.DotNetObject): ChatMessageEditor {
        return new ChatMessageEditor(editorDiv, filePickerBlazorRef);
    }

    constructor(editorDiv: HTMLDivElement, filePickerBlazorRef: DotNet.DotNetObject) {
        const bodyClassList = document.body.classList;
        this.editorDiv = editorDiv;
        this.postPanelDiv = this.editorDiv.querySelector(':scope .post-panel')!;
        this.input = this.postPanelDiv.querySelector(':scope .message-input')!;
        this.filePickerBackend = new AttachmentWebFilePickerBackend(filePickerBlazorRef);
        this.filePickerInput = this.editorDiv.querySelector(':scope .attachment-web-file-picker')!;
        this.filePicker = new AttachmentWebFilePicker(this.filePickerBackend, this.filePickerInput);
        this.isSmooth = !bodyClassList.contains('device-ios');
        if (this.isSmooth)
            editorDiv.classList.add('smooth');

        this.updateLayout();
        this.updateHasContent();

        // Wiring up event listeners
        ScreenSize.event$
            .pipe(takeUntil(this.disposed$))
            .subscribe(this.updateLayout);

        fromEvent(this.input, 'focusin')
            .pipe(takeUntil(this.disposed$))
            .subscribe(this.onEditorFocus);
        fromEvent(this.input, 'focusout')
            .pipe(takeUntil(this.disposed$))
            .subscribe(this.onEditorBlur);

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

        // Drag-and-drop file attachment
        this.chatPanel = this.editorDiv.closest('.upload-drag-drop-host');
        if (this.chatPanel) {
            this.dragDropOverlay = document.createElement('div');
            this.dragDropOverlay.className = 'drag-drop-overlay';
            this.dragDropOverlay.innerHTML =
                '<div class="drag-drop-overlay-content">' +
                '<i class="icon-upload-cloud text-4xl"></i>' +
                '<span>Drop files to attach</span>' +
                '</div>';
            const panelStyle = getComputedStyle(this.chatPanel);
            if (panelStyle.position === 'static')
                (this.chatPanel).style.position = 'relative';
            this.chatPanel.appendChild(this.dragDropOverlay);

            fromEvent<DragEvent>(this.chatPanel, 'dragenter')
                .pipe(takeUntil(this.disposed$))
                .subscribe(e => this.onDragEnter(e));
            fromEvent<DragEvent>(this.chatPanel, 'dragover')
                .pipe(takeUntil(this.disposed$))
                .subscribe(e => this.onDragOver(e));
            fromEvent<DragEvent>(this.chatPanel, 'dragleave')
                .pipe(takeUntil(this.disposed$))
                .subscribe(e => this.onDragLeave(e));
            fromEvent<DragEvent>(this.chatPanel, 'drop')
                .pipe(takeUntil(this.disposed$))
                .subscribe(e => this.onDrop(e));
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
        this.sideNavs.forEach(() => {
            this.sideNavObserver.disconnect();
        });
        if (this.dragDropOverlay) {
            this.dragDropOverlay.remove();
            this.dragDropOverlay = null;
        }
    }

    // Public methods

    private addAttachmentsObserver() {
        const lastElement = this.attachmentListElement?.querySelector('.last-element');
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
        const observer = new MutationObserver(callback);
        observer.observe(this.attachmentListElement!, {
            attributes: true,
            childList: true,
            subtree: true,
        });

    }

    private updatePostPanelBorderRadius: ResizeObserverCallback = (entries) => {
        const clsLst = this.postPanelDiv.classList;
        entries.forEach(entry => {
            const height = entry.contentRect.height;
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
        fromEvent<ClipboardEvent>(this.input, 'paste', { capture: true })
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: ClipboardEvent) => this.onInputPaste(event));

        this.markupEditor.changed = (_html, text) => {
            this.backupRequired$.next();
            this.updateHasContent();
            this.updateCanConvertToFile(text);
        }
        this.updateHasContent();
        this.updateCanConvertToFile(this.markupEditor.getText());
        if (this.isNarrowScreen)
            this.markupEditor.contentDiv.blur(); // We want to see the placeholder on mobile when you open a chat
    }

    /** Called by Blazor */
    public convertCurrentTextToAttachment(): void {
        const text = this.markupEditor?.getText() ?? '';
        if (text.length < LargeTextSuggestFileThreshold)
            return;
        const file = new File([text], LargeTextPasteFileName, { type: 'text/plain' });
        void this.filePickerBackend.add([{ file: file, fileHandle: null }]);
    }

    private updateCanConvertToFile(text: string) {
        const canConvert = text.length >= LargeTextSuggestFileThreshold;
        this.editorDiv.classList.toggle(CanConvertToFileClass, canConvert);
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
        // We handle clipboard files here, and route large plain-text pastes
        // through the same attachment pipeline. Small text pastes fall through
        // to MarkupEditor (bubble phase on the inner .editor-content).
        const clipboardData = event.clipboardData;
        if (!clipboardData)
            return;

        const fileResults : PickFileResult[] = [];
        for (const item of clipboardData.items) {
            if (item.kind === 'file') {
                const file = item.getAsFile();
                if (!file)
                    continue; // Should not happen, but just in case

                fileResults.push({ file: file, fileHandle: null });
            }
        }

        if (fileResults.length === 0) {
            const text = clipboardData.getData('text');
            if (text && text.length >= LargeTextAutoFileThreshold) {
                const file = new File([text], LargeTextPasteFileName, { type: 'text/plain' });
                fileResults.push({ file: file, fileHandle: null });
                // Capture-phase + stopImmediatePropagation keeps MarkupEditor.onPaste
                // from inserting the same text into the editor.
                event.stopImmediatePropagation();
            }
        }

        if (fileResults.length === 0)
            return;

        preventDefaultForEvent(event); // Must run sync — async tail can't preventDefault
        void this.filePickerBackend.add(fileResults);
    };

    // Drag-and-drop handlers

    private hasFiles(e: DragEvent): boolean {
        return e.dataTransfer?.types?.includes('Files') ?? false;
    }

    private onDragEnter(e: DragEvent) {
        e.preventDefault();
        this.dragCounter++;
        if (this.hasFiles(e) && this.dragDropOverlay)
            this.dragDropOverlay.classList.add('active');
    }

    private onDragOver(e: DragEvent) {
        e.preventDefault();
        if (e.dataTransfer)
            e.dataTransfer.dropEffect = 'copy';
    }

    private onDragLeave(e: DragEvent) {
        this.dragCounter--;
        if (this.dragCounter <= 0) {
            this.dragCounter = 0;
            this.dragDropOverlay?.classList.remove('active');
        }
    }

    private onDrop(e: DragEvent) {
        e.preventDefault();
        this.dragCounter = 0;
        this.dragDropOverlay?.classList.remove('active');

        const files = e.dataTransfer?.files;
        if (!files || files.length === 0)
            return;

        const fileResults: PickFileResult[] = [];
        for (const file of files)
            fileResults.push({ file: file, fileHandle: null });
        void this.filePickerBackend.add(fileResults);
    }

    // Private methods

    private updateLayout = () => {
        const width = window.visualViewport?.width ?? window.innerWidth;
        const isNarrowScreen = width < 1024;

        if (width !== this.lastWidth) {
            // Orientation/window width change: re-baseline so a stale heightAtFocus
            // doesn't trigger the keyboard path with the wrong reference.
            if (this.heightAtFocus != null)
                this.heightAtFocus = window.visualViewport?.height ?? window.innerHeight;
            this.lastWidth = width;
        }

        this.updateKeyboardPanel();

        if (this.isNarrowScreen === isNarrowScreen)
            return;

        this.isNarrowScreen = isNarrowScreen;
        const buttons = this.editorDiv.querySelectorAll(':scope div.chat-audio-panel .btn');
        if (isNarrowScreen)
            buttons.forEach(b => b.addEventListener('click', this.onReturnFocusOnInput));
        else
            buttons.forEach(b => b.removeEventListener('click', this.onReturnFocusOnInput));
    }

    private onEditorFocus = () => {
        this.heightAtFocus = window.visualViewport?.height ?? window.innerHeight;
        this.updateKeyboardPanel();
    }

    private onEditorBlur = () => {
        this.heightAtFocus = null;
        this.applyPanelMode('Normal');
    }

    private updateKeyboardPanel = () => {
        if (this.heightAtFocus == null)
            return;
        const height = window.visualViewport?.height ?? window.innerHeight;
        const delta = this.heightAtFocus - height;
        debugLog?.log(`updateKeyboardPanel: delta:`, delta, '/ baseline:', this.heightAtFocus);
        this.applyPanelMode(delta > 100 ? 'Narrow' : 'Normal');
    }

    private applyPanelMode = (mode: PanelMode) => {
        if (this.panelModel === mode)
            return;
        const prev = this.panelModel;
        this.panelModel = mode;
        if (mode === 'Narrow') {
            this.editorDiv.classList.remove('to-thick');
            this.editorDiv.classList.add('narrow-panel', 'to-thin');
        }
        else {
            this.editorDiv.classList.remove('narrow-panel', 'to-thin');
            this.editorDiv.classList.add('to-thick');
        }
        if (mode === 'Narrow' && prev !== 'Narrow')
            CompactLayout.request('keyboard');
        else if (mode !== 'Narrow' && prev === 'Narrow')
            CompactLayout.release('keyboard');
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
                const dataValue = m.target.dataset['sideNav'];
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
        this.markupEditor.setHtml(html ?? '', ScreenSize.isWide() && !DeviceInfo.isTouchCapable);
    }
}
