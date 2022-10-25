import './chat-message-editor.css';
import { throttle } from 'promises';
import { MarkupEditor } from '../MarkupEditor/markup-editor';
import { Log, LogLevel } from 'logging';

const LogScope = 'MessageEditor';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export class ChatMessageEditor {
    private blazorRef: DotNet.DotNetObject;
    private readonly editorDiv: HTMLDivElement;
    private markupEditor: MarkupEditor;
    private readonly input: HTMLDivElement;
    private readonly filePicker: HTMLInputElement;
    private readonly postButton: HTMLButtonElement;
    private readonly attachButton: HTMLButtonElement;
    private readonly notifyPanel: HTMLDivElement;
    private readonly notifyPanelObserver : MutationObserver;
    private lastHeight: number;
    private lastWidth: number;
    private isMobile: boolean = null; // Intended: updateLayout needs this on the first run
    private isNarrowMode: boolean = null; // Intended: updateLayout needs this on the first run
    private isTextMode: boolean = null; // Intended: updateTextMode needs this on the first run
    private isNotifyPanelOpen: boolean = false;
    private attachmentsIdSeed: number = 0;
    private attachments: Map<number, Attachment> = new Map<number, Attachment>();

    static create(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject): ChatMessageEditor {
        return new ChatMessageEditor(editorDiv, blazorRef);
    }

    constructor(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this.editorDiv = editorDiv;
        this.blazorRef = blazorRef;
        this.input = this.editorDiv.querySelector(':scope .post-panel .message-input');
        this.postButton = this.editorDiv.querySelector(':scope .post-panel .post-message');
        this.attachButton = this.editorDiv.querySelector(':scope .attach-btn');
        this.filePicker = this.editorDiv.querySelector(':scope .post-panel input.file-picker');
        this.notifyPanel = this.editorDiv.querySelector(':scope .notify-call-panel');

        this.updateLayout();
        this.updateTextMode();

        // Wiring up event listeners
        window.visualViewport.addEventListener('resize', throttle(this.onWindowResize, 250, 'delayHead'));
        this.input.addEventListener('paste', this.onInputPaste);
        this.filePicker.addEventListener('change', this.onFilePickerChange);
        this.attachButton.addEventListener('click', this.onAttachButtonClick);
        this.notifyPanel.addEventListener('click', this.onNotifyPanelClick);

        this.notifyPanelObserver = new MutationObserver(this.updateNotifyPanelRelated);
        this.notifyPanelObserver.observe(this.notifyPanel, {
            attributes: true,
        });
    }

    public dispose() {
        window.visualViewport.removeEventListener('resize', this.onWindowResize);
        this.input.removeEventListener('paste', this.onInputPaste);
        this.filePicker.removeEventListener('change', this.onFilePickerChange);
        this.attachButton.removeEventListener('click', this.onAttachButtonClick);
        this.notifyPanel.removeEventListener('click', this.onNotifyPanelClick);
        this.notifyPanelObserver.disconnect();
    }

    // Public methods

    public onMarkupEditorReady(markupEditor: MarkupEditor)
    {
        this.markupEditor = markupEditor;
        markupEditor.changed = () => this.updateTextMode();
        this.updateTextMode();
        if (this.isMobile)
            this.markupEditor.contentDiv.blur(); // We want to see the placeholder on mobile when you open a chat
    }

    public post = async (chatId: string, text : string, repliedChatEntryId?: number): Promise<number> => {
        const formData = new FormData();
        const attachments = [];
        if (this.attachments.size > 0) {
            let i = 0;
            this.attachments.forEach(attachment => {
                formData.append('files[' + i + ']', attachment.File);
                attachments.push({ 'id': i, 'filename': attachment.File.name, 'description': '' });
                i++;
            });
        }

        const payload = { 'text': text, 'attachments': attachments, 'repliedChatEntryId': repliedChatEntryId };
        const payloadJson = JSON.stringify(payload);
        formData.append('payload_json', payloadJson);

        debugLog?.log(`post: sending request with ${attachments.length} attachment(s)`);
        let url = 'api/chats/' + chatId + '/message';
        // @ts-ignore
        const baseUri = window.App.baseUri; // Web API base URI when running in MAUI
        if (baseUri)
            url = new URL(url, baseUri).toString();
        const response = await fetch(url, {
            method: 'POST',
            body: formData,
            credentials: 'include' // required to include third-party cookies in cross origin request when running in MAUI
        });

        if (!response.ok) {
            let reason = response.statusText;
            if (!reason)
                reason = 'unknown';
            throw new Error('Failed to send message. Reason: ' + reason);
        }
        const entryId = await response.text();
        return Number(entryId);
    };

    public showFilePicker = () => {
        this.filePicker.click();
    };

    public removeAttachment(id: number) {
        const attachment = this.attachments.get(id);
        this.attachments.delete(id);
        if (attachment?.Url)
            URL.revokeObjectURL(attachment.Url);
        this.updateTextMode();
    }

    public clearAttachments() {
        for (const attachment of this.attachments.values()) {
            if (attachment?.Url)
                URL.revokeObjectURL(attachment.Url);
        }
        this.attachments.clear();
        this.attachmentsIdSeed = 0;
        this.updateTextMode();
    }

    // Event handlers

    private onWindowResize = () => this.updateLayout()

    private onAttachButtonClick = ((event: Event & { target: Element; }) => {
        if (this.isNarrowMode)
            this.markupEditor.focus();
    });

    private onReturnFocusOnInput = ((event: Event & { target: Element; }) => {
        if (this.isNarrowMode) {
            this.markupEditor.focus();
            this.updateTextMode();
        }
    });

    private onNotifyPanelClick = (async (event: Event & { target: Element; }) => {
        if (event.target == this.notifyPanel || event.target.classList.contains('notify-call-content')) {
            if (this.notifyPanel.classList.contains('panel-opening')) {
                await this.blazorRef.invokeMethodAsync('CloseNotifyPanel');
            }
        }
    });

    private onInputPaste = ((event: ClipboardEvent & { target: Element; }) => {
        // Get pasted data via clipboard API
        // We need to handle only files pasting.
        // Text pasting is controlled by markup editor.
        const clipboardData = event.clipboardData;
        for (const item of clipboardData.items) {
            if (item.kind === 'file') {
                const file = item.getAsFile();
                void this.addAttachment(file);
                event.preventDefault();
            }
        }
    });

    private onFilePickerChange = (async (event: Event & { target: Element; }) => {
        for (const file of this.filePicker.files) {
            const added : boolean = await this.addAttachment(file);
            if (!added)
                break;
        }
        this.filePicker.value = '';
    });

    // Private methods

    private updateLayout = () => {
        const width = window.visualViewport.width;
        const height = window.visualViewport.height;
        const isMobile = width < 1024;

        if (this.isMobile === isMobile) {
            if (!isMobile)
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
                const isNarrowMode = Math.abs(height - minHeight) < 0.01; // FP: height == minHeight
                if (this.isNarrowMode !== isNarrowMode) {
                    this.isNarrowMode = isNarrowMode;
                    if (isNarrowMode)
                        this.editorDiv.classList.add('narrow-panel');
                    else
                        this.editorDiv.classList.remove('narrow-panel');
                }
            }
            this.lastHeight = height;
            return;
        }

        this.isMobile = isMobile;
        this.lastHeight = height;
        this.lastWidth = width;
        const buttons = this.editorDiv.querySelectorAll(':scope div.chat-audio-panel .btn');
        if (isMobile)
            buttons.forEach(b => b.addEventListener('click', this.onReturnFocusOnInput));
        else
            buttons.forEach(b => b.removeEventListener('click', this.onReturnFocusOnInput));
    }

    private updateTextMode() {
        const text = this.markupEditor?.getText() ?? '';
        const isTextMode = text != '' || this.attachments.size > 0;
        if (this.isTextMode === isTextMode)
            return;
        this.isTextMode = isTextMode;
        if (isTextMode)
            this.editorDiv.classList.add('text-mode');
        else
            this.editorDiv.classList.remove('text-mode');
        this.endAnimations();
    }

    private updateNotifyPanelRelated = () => {
        const isNotifyPanelOpen = this.notifyPanel.classList.contains('panel-opening');
        if (this.isNotifyPanelOpen === isNotifyPanelOpen)
            return;

        this.isNotifyPanelOpen = isNotifyPanelOpen;
        const attach = this.editorDiv.querySelector(':scope .attach-dropdown');
        const label = this.editorDiv.querySelector(':scope label');
        if (isNotifyPanelOpen) {
            self.setTimeout(() => {
                attach.classList.add('hidden');
                label.classList.add('hidden');
                this.markupEditor.isEditable(false);
            }, 150);
        } else {
            attach.classList.remove('hidden');
            label.classList.remove('hidden');
            this.markupEditor.isEditable(true);
        }

        if (this.notifyPanel.classList.contains('panel-closing')) {
            self.setTimeout(() => {
                this.notifyPanel.classList.replace('panel-closing', 'panel-closed');
            }, 150);
        }
    };

    private endAnimations() : void {
        this.notifyPanel.classList.remove('panel-opening', 'panel-closing');
        const playbackWrapper = this.editorDiv.querySelector('.playback-wrapper');
        if (!playbackWrapper)
            return;
        playbackWrapper.classList.replace('listen-on-to-off', 'listen-off');
        playbackWrapper.classList.replace('listen-off-to-on', 'listen-on');
    }

    private async addAttachment(file: File): Promise<boolean> {
        const attachment: Attachment = { Id: this.attachmentsIdSeed, File: file, Url: '' };
        if (file.type.startsWith('image'))
            attachment.Url = URL.createObjectURL(file);
        const added : boolean = await this.blazorRef.invokeMethodAsync(
            'AddAttachment', attachment.Id, attachment.Url, file.name, file.type, file.size);
        if (!added) {
            if (attachment.Url)
                URL.revokeObjectURL(attachment.Url);
        }
        else {
            this.attachmentsIdSeed++;
            this.attachments.set(attachment.Id, attachment);
            this.updateTextMode();
        }
        return added;
    }
}

interface Attachment {
    File: File;
    Url: string;
    Id: number;
}
