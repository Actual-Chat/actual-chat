import './chat-message-editor.css';
import { SlateEditorHandle } from '../SlateEditor/slate-editor-handle';

const LogScope: string = 'MessageEditor';

export class ChatMessageEditor {
    private blazorRef: DotNet.DotNetObject;
    private readonly editorDiv: HTMLDivElement;
    private readonly input: HTMLDivElement;
    private slateInput: HTMLDivElement;
    private readonly filesPicker: HTMLInputElement;
    private readonly postButton: HTMLButtonElement;
    private readonly attachButton: HTMLButtonElement;
    private readonly notifyPanel: HTMLDivElement;
    private readonly notifyPanelObserver : MutationObserver;
    private isTextMode: boolean = false;
    private isNarrowMode: boolean = false;
    private isPanelOpened: boolean = false;
    private attachmentsIdSeed: number = 0;
    private attachments: Map<number, Attachment> = new Map<number, Attachment>();

    static create(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject): ChatMessageEditor {
        return new ChatMessageEditor(editorDiv, blazorRef);
    }

    constructor(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this.editorDiv = editorDiv;
        this.blazorRef = blazorRef;
        this.input = this.editorDiv.querySelector(':scope div.post-panel div.message-input');
        this.filesPicker = this.editorDiv.querySelector(':scope div.post-panel input.files-picker');
        this.postButton = this.editorDiv.querySelector(':scope div.post-panel .post-message');
        this.attachButton = this.editorDiv.querySelector(':scope .attach-btn');
        this.notifyPanel = this.editorDiv.querySelector(':scope div.post-panel .notify-call-panel');

        // Wiring up event listeners
        this.editorDiv.addEventListener('click', this.onEditorFocus);
        this.editorDiv.addEventListener('input', this.onDeleteContentBackward);
        this.input.addEventListener('paste', this.onInputPaste);
        this.input.addEventListener('focusin', this.onInputFocusIn);
        this.input.addEventListener('focusout', this.onInputFocusOut);
        this.filesPicker.addEventListener('change', this.onFilesPickerChange);
        this.postButton.addEventListener('click', this.onPostClick);
        this.attachButton.addEventListener('click', this.onAttachButtonClick);
        this.notifyPanel.addEventListener('click', this.onNotifyPanel);

        const mobileLanguageBtn = this.editorDiv.querySelector(':scope div.mobile-control-panel .mobile-language-button');
        const mobilePlaybackBtn = this.editorDiv.querySelector(':scope div.mobile-control-panel .mobile-playback-toggle');
        if (mobileLanguageBtn)
            mobileLanguageBtn.addEventListener('click', this.onReturnFocusOnInput);
        if (mobilePlaybackBtn)
            mobilePlaybackBtn.addEventListener('click', this.onReturnFocusOnInput);

        this.notifyPanelObserver = new MutationObserver(this.syncAttachDropdownVisibility);
        this.notifyPanelObserver.observe(this.notifyPanel, {
            attributes: true,
        });
        this.changeMode();
    }

    private onDeleteContentBackward = ((event: InputEvent & { target: Element; }) => {
        if (event.inputType === 'deleteContentBackward' && this.getText().length == 0) {
            this.getSlateInput().focus();
        }
    });

    private onAttachButtonClick = ((event: Event & { target: Element; }) => {
        this.getSlateInput().focus();
    });

    private getSlateInput = () : HTMLDivElement => {
        this.slateInput = this.input.querySelector('[role="textbox"]');
        if (!this.slateInput)
            console.log('Slate editor not found.');
        return this.slateInput;
    }

    private isMobilePanelOpen = () : boolean => {
        let panel = this.editorDiv.querySelector('.mobile-control-panel');
        return panel && panel.getBoundingClientRect().height != 0;
    }

    private mobilePanelHandler = (enable: boolean) => {
        if (enable) {
            if (!this.editorDiv.classList.contains('narrow-panel')) {
                this.editorDiv.classList.add('narrow-panel');
                this.isNarrowMode = true;
            }
        } else {
            this.editorDiv.classList.remove('narrow-panel');
            this.isNarrowMode = false;
        }
    }

    private onEditorFocus = ((event: Event & { target: Element; }) => {
        const btn = event.target.closest('button');
        if (!this.isNarrowMode)
            return;
        if (btn && (btn.classList.contains('record-off-btn') || btn.classList.contains('record-on-btn'))) {
            if (this.getText() == '' && this.attachments.size == 0) {
                this.changeMode();
                this.mobilePanelHandler(false);
            } else
                this.getSlateInput().focus();
        } else {
            this.getSlateInput().focus();
        }
    });

    private onReturnFocusOnInput = ((event: Event & { target: Element; }) => {
        if (this.isMobilePanelOpen && this.isNarrowMode) {
            this.getSlateInput().focus();
            this.changeMode();
        }
    });

    private onNotifyPanel = (async (event: Event & { target: Element; }) => {
        if (event.target == this.notifyPanel || event.target.classList.contains('notify-call-content')) {
            if (this.notifyPanel.classList.contains('panel-opening')) {
                await this.blazorRef.invokeMethodAsync('CloseNotifyPanel');
            }
        }
    });

    private onInputPaste = ((event: ClipboardEvent & { target: Element; }) => {
        // Get pasted data via clipboard API
        // We need to handle only files pasting.
        // Text pasting is controlled by slate editor.
        const clipboardData = event.clipboardData;
        for (const item of clipboardData.items) {
            if (item.kind === 'file') {
                const file = item.getAsFile();
                void this.addAttachment(file);
                event.preventDefault();
            }
        }
    });

    private onInputFocusIn = ((event: Event & { target: Element; }) => {
        this.mobilePanelHandler(true);
    });

    private onInputFocusOut = ((event: Event & { target: Element; }) => {
        if ((this.isNarrowMode && this.getText() == '') || this.isTextMode || !this.isMobilePanelOpen)
            return;
        setTimeout(() => {
            this.mobilePanelHandler(false);
        }, 200)
    });

    private onFilesPickerChange = (async (event: Event & { target: Element; }) => {
        for (const file of this.filesPicker.files) {
            const added : boolean = await this.addAttachment(file);
            if (!added)
                break;
        }
        this.filesPicker.value = '';
    });

    private onPostClick = ((event: MouseEvent & { target: Element; }) => {
        const input = this.input.querySelector('[role="textbox"]') as HTMLDivElement;
        input.focus();
        this.changeMode();
    });

    private syncAttachDropdownVisibility = () => {
        const isPanelOpened = this.notifyPanel.classList.contains('panel-opening');
        if (this.isPanelOpened === isPanelOpened)
            return;
        this.isPanelOpened = isPanelOpened;
        const attach = this.editorDiv.querySelector(':scope .attach-dropdown');
        const label = this.editorDiv.querySelector(':scope label');
        const slate = this.getSlateInput();
        if (isPanelOpened) {
            setTimeout(() => {
                attach.classList.add('hidden');
                label.classList.add('w-0');
                slate.setAttribute('contenteditable', 'false');
            }, 150);
        } else {
            attach.classList.remove('hidden');
            label.classList.remove('w-0');
            slate.setAttribute('contenteditable', 'true');
        }

        if (this.notifyPanel.classList.contains('panel-closing')) {
            setTimeout(() => {
                this.notifyPanel.classList.replace('panel-closing', 'panel-closed');
            }, 150);
        }
    };

    private changeMode() {
        const text = this.getText();
        const isTextMode = text != '' || this.attachments.size > 0;
        if (this.isTextMode === isTextMode)
            return;
        this.isTextMode = isTextMode;
        if (isTextMode)
            this.editorDiv.classList.add('text-mode');
        else
            this.editorDiv.classList.remove('text-mode');
        this.playbackAnimationOff();
        this.notifyPanelAnimationOff();
    }

    private notifyPanelAnimationOff() : void {
        let classes = this.notifyPanel.classList;
        classes.remove('panel-opening', 'panel-closing');
    }

    private playbackAnimationOff() : void {
        const playbackWrapper = this.editorDiv.querySelector('.playback-wrapper');
        if (playbackWrapper == null)
            return;
        let classes = playbackWrapper.classList;
        if (classes.contains('listen-on-to-off')) {
            classes.replace('listen-on-to-off', 'listen-off');
        }
        else if (classes.contains('listen-off-to-on')) {
            classes.replace('listen-off-to-on', 'listen-on');
        }
    }

    private getText(): string {
        const editorHandle = this.editorHandle();
        if (!editorHandle)
            return '';
        return editorHandle.getText();
    }

    private async addAttachment(file: File): Promise<boolean> {
        const attachment: Attachment = { Id: this.attachmentsIdSeed, File: file, Url: '' };
        if (file.type.startsWith('image'))
            attachment.Url = URL.createObjectURL(file);
        const added : boolean = await this.blazorRef.invokeMethodAsync('AddAttachment', attachment.Id, attachment.Url, file.name, file.type, file.size);
        if (!added) {
            if (attachment.Url)
                URL.revokeObjectURL(attachment.Url);
        }
        else {
            this.attachmentsIdSeed++;
            this.attachments.set(attachment.Id, attachment);
            this.changeMode();
        }
        return added;
    }

    public removeAttachment(id: number) {
        const attachment = this.attachments.get(id);
        this.attachments.delete(id);
        if (attachment && attachment.Url)
            URL.revokeObjectURL(attachment.Url);
        this.changeMode();
    }

    public clearAttachments() {
        const attachments = this.attachments;
        attachments.forEach(a => {
            if (a && a.Url)
                URL.revokeObjectURL(a.Url);
        });
        this.attachments.clear();
        this.changeMode();
    }

    public postMessage = async (chatId: string, text : string, repliedChatEntryId?: number): Promise<number> => {
        const formData = new FormData();
        const attachmentsList = [];
        if (this.attachments.size > 0) {
            let i = 0;
            this.attachments.forEach(attachment => {
                formData.append('files[' + i + ']', attachment.File);
                attachmentsList.push({ 'id': i, 'filename': attachment.File.name, 'description': '' });
                i++;
            });
        }

        const payload = { 'text': text, 'attachments': attachmentsList, 'repliedChatEntryId': repliedChatEntryId };
        const payloadJson = JSON.stringify(payload);
        formData.append('payload_json', payloadJson);

        console.log(`${LogScope}: Sending post message request with ${attachmentsList.length} attachment(s)`);
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

    public onPostSucceeded = () => {
        for (const attachment of this.attachments.values()) {
            if (attachment.Url)
                URL.revokeObjectURL(attachment.Url);
        }
        this.attachments.clear();
        this.attachmentsIdSeed = 0;
        this.changeMode();
    };

    public showFilesPicker = () => {
        this.filesPicker.click();
    };

    public onSlateEditorRendered()
    {
        const editorHandle = this.editorHandle();
        if (!editorHandle) {
            console.error('SlateEditorHandle is undefined');
            return;
        }
        this.changeMode();
        editorHandle.onHasContentChanged = () => this.changeMode();
    }

    private editorHandle = () : SlateEditorHandle => {
        // @ts-ignore
        return this.input.editorHandle as SlateEditorHandle;
    };

    public dispose() {
        this.editorDiv.removeEventListener('click', this.onEditorFocus);
        this.editorDiv.removeEventListener('input', this.onDeleteContentBackward);
        this.input.removeEventListener('paste', this.onInputPaste);
        this.input.removeEventListener('focusin', this.onInputFocusIn);
        this.input.removeEventListener('focusout', this.onInputFocusOut);
        this.filesPicker.removeEventListener('change', this.onFilesPickerChange);
        this.postButton.removeEventListener('click', this.onPostClick);
        this.attachButton.removeEventListener('click', this.onAttachButtonClick);
        this.notifyPanel.removeEventListener('click', this.onNotifyPanel);
        this.notifyPanelObserver.disconnect();
        const mobileLanguageBtn = this.editorDiv.querySelector(':scope div.mobile-control-panel .mobile-language-button');
        const mobilePlaybackBtn = this.editorDiv.querySelector(':scope div.mobile-control-panel .mobile-playback-toggle');
        if (mobileLanguageBtn)
            mobileLanguageBtn.removeEventListener('click', this.onReturnFocusOnInput);
        if (mobilePlaybackBtn)
            mobilePlaybackBtn.removeEventListener('click', this.onReturnFocusOnInput);
    }
}

interface Attachment {
    File: File;
    Url: string;
    Id: number;
}
