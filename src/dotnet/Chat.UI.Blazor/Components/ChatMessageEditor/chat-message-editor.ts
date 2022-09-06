import './chat-message-editor.css';
import { SlateEditorHandle } from '../SlateEditor/slate-editor-handle';
import { debounce } from 'lodash';

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
    private isHorizontal: boolean = false;
    private initialHeight: number;
    private initialWidth: number;
    private isMobile: boolean;
    private windowHeight: number;
    private windowWidth: number;
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
        this.getInitialArgs();
        window.addEventListener('resize', debounce(this.onChangeViewSize, 100));
        this.input.addEventListener('paste', this.onInputPaste);
        this.filesPicker.addEventListener('change', this.onFilesPickerChange);
        this.postButton.addEventListener('click', this.onPostClick);
        this.attachButton.addEventListener('click', this.onAttachButtonClick);
        this.notifyPanel.addEventListener('click', this.onNotifyPanel);

        this.notifyPanelObserver = new MutationObserver(this.syncAttachDropdownVisibility);
        this.notifyPanelObserver.observe(this.notifyPanel, {
            attributes: true,
        });
        this.changeMode();
    }

    private mobileListenersHandler = (add: boolean) => {
        const panel = this.editorDiv.querySelector(':scope div.mobile-control-panel');
        if (panel) {
            const buttons = panel.querySelectorAll('.btn');
            if (buttons.length > 0) {
                if (add) {
                    buttons.forEach(b => {
                        b.addEventListener('click', this.onReturnFocusOnInput);
                    })
                    this.isMobile = true;
                } else {
                    buttons.forEach(b => {
                        b.removeEventListener('click', this.onReturnFocusOnInput);
                    })
                    this.isMobile = false;
                }
            }
        }
    }

    private getInitialArgs = () => {
        if (window.innerWidth < 1024) {
            this.initialHeight = window.innerHeight;
            this.initialWidth = window.innerWidth;
            this.mobileListenersHandler(true);
        }
        this.isHorizontal = window.innerWidth > window.innerHeight;
    }

    private onChangeViewSize = () => {
        const size = this.getWindowSize();
        const height = this.windowHeight = size[0];
        const width = this.windowWidth = size[1];
        const isMobile = width < 1024;
        const isHorizontal = width > height;
        if (isHorizontal != this.isHorizontal)
            this.isHorizontal = isHorizontal;
        if (isMobile != this.isMobile) {
            if (isMobile) {
                // switch desktop to mobile
                this.initialHeight = height;
                this.initialWidth = width;
                this.mobileListenersHandler(true);
            } else {
                // switch mobile to desktop
                this.mobileListenersHandler(false);
            }
        }
        if (this.isMobile) {
            // mobile view
            this.onChangeMobileView(width, height);
        } else {
            // desktop view
            return;
        }
    }

    private onChangeMobileView = (width: number, height: number) => {
        if (height < this.initialHeight && width == this.initialWidth) {
            console.log('Height is less than initial.');
            if (!this.editorDiv.classList.contains('narrow-panel')) {
                this.editorDiv.classList.add('narrow-panel');
                this.isNarrowMode = true;
            }
        } else if (height == this.initialHeight) {
            this.editorDiv.classList.remove('narrow-panel');
            this.isNarrowMode = false;
        }
    }

    private getWindowSize = () : [number, number] => {
        const height = window.innerHeight;
        const width = window.innerWidth;
        return [height, width];
    }

    private onAttachButtonClick = ((event: Event & { target: Element; }) => {
        if (this.isNarrowMode)
            this.getSlateInput().focus();
    });

    private getSlateInput = () : HTMLDivElement => {
        this.slateInput = this.input.querySelector('[role="textbox"]');
        if (!this.slateInput)
            console.log('Slate editor not found.');
        return this.slateInput;
    }

    private onReturnFocusOnInput = ((event: Event & { target: Element; }) => {
        if (this.isNarrowMode && this.isTextMode) {
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
        window.removeEventListener('resize', this.onChangeViewSize);
        this.input.removeEventListener('paste', this.onInputPaste);
        this.filesPicker.removeEventListener('change', this.onFilesPickerChange);
        this.postButton.removeEventListener('click', this.onPostClick);
        this.attachButton.removeEventListener('click', this.onAttachButtonClick);
        this.notifyPanel.removeEventListener('click', this.onNotifyPanel);
        this.notifyPanelObserver.disconnect();
    }
}

interface Attachment {
    File: File;
    Url: string;
    Id: number;
}
