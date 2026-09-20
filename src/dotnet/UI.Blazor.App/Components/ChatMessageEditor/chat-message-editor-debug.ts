import { registerDebugSurface } from 'debug-surface';

const EditorContentSelector = '#message-input .editor-content';
const FilePickerSelector = '.chat-message-editor input.attachment-web-file-picker';
const PostTimeoutMs = 10_000;

/** A file handed to `debugUI.fake.attach` from the console or from the harness. */
export interface DebugAttachment {
    name: string;
    type: string;
    base64: string;
}

// Console surface: `debugUI.fake.*` — writes a message and attaches files without a
// human at the keyboard. It drives the DOM rather than the ChatMessageEditor instance
// so a scenario exercises the same path a keystroke and a file pick take.
export function initChatEditorDebugConsole(): void {
    registerDebugSurface('fake', {
        send: sendMessage,
        attach: attachFiles,
    });
}

// Private methods

async function sendMessage(text: string): Promise<void> {
    const content = requireEditorContent();
    content.focus();
    if (text !== '')
        setText(content, text);

    content.dispatchEvent(new KeyboardEvent('keypress', {
        key: 'Enter',
        code: 'Enter',
        keyCode: 13,
        which: 13,
        bubbles: true,
        cancelable: true,
    }));
    await whenEditorIsEmpty(content);
    if (text !== '')
        await whenPosted(text);
}

function attachFiles(files: DebugAttachment[]): void {
    const input = document.querySelector<HTMLInputElement>(FilePickerSelector);
    if (!input)
        throw new Error('debugUI.fake: the attachment file picker is not on the page - open a chat first.');

    const transfer = new DataTransfer();
    for (const file of files)
        transfer.items.add(toFile(file));

    input.files = transfer.files;
    input.dispatchEvent(new Event('change', { bubbles: true }));
}

// An empty editor only says the post was submitted - the entry itself may still be in
// flight. Callers that care about ordering need whenPosted below as well.
async function whenEditorIsEmpty(content: HTMLElement): Promise<void> {
    const deadline = Date.now() + PostTimeoutMs;
    while (Date.now() < deadline) {
        if (content.innerText.trim() === '')
            return;

        await new Promise(resolve => setTimeout(resolve, 100));
    }
    throw new Error(`debugUI.fake.send: the editor did not clear within ${PostTimeoutMs}ms.`);
}

// Waits for the entry to come back from the server and render, so two harness-driven
// speakers can't overtake each other between the submit and the entry's local id.
async function whenPosted(text: string): Promise<void> {
    const deadline = Date.now() + PostTimeoutMs;
    while (Date.now() < deadline) {
        const messages = [...document.querySelectorAll<HTMLElement>('.chat-message-markup')];
        if (messages.some(x => x.innerText.includes(text)))
            return;

        await new Promise(resolve => setTimeout(resolve, 100));
    }
    throw new Error(`debugUI.fake.send: the message did not appear within ${PostTimeoutMs}ms.`);
}

// Replaces the content with a single text node and leaves the caret after it, which is
// the shape MarkupEditor.getText() reads back when the post handler runs.
function setText(content: HTMLElement, text: string): void {
    const node = document.createTextNode(text);
    content.replaceChildren(node);
    const selection = window.getSelection();
    if (!selection)
        return;

    const range = document.createRange();
    range.selectNodeContents(content);
    range.collapse(false);
    selection.removeAllRanges();
    selection.addRange(range);
}

function requireEditorContent(): HTMLElement {
    const content = document.querySelector<HTMLElement>(EditorContentSelector);
    if (!content)
        throw new Error('debugUI.fake: the message editor is not on the page - open a chat first.');

    return content;
}

function toFile(attachment: DebugAttachment): File {
    const binary = atob(attachment.base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++)
        bytes[i] = binary.charCodeAt(i);

    return new File([bytes], attachment.name, { type: attachment.type });
}
