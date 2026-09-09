import type { Page } from 'playwright';
import { BASE_URL, waitForEditor } from '../../../tests/ts/e2e/helpers';
import type { DebugAttachment, DebugUIRoot } from './debug-ui-api';
import { ensureConnected } from './page-state';
import { waitForDebugUI } from './session';

/** Mirrors PeerChatId.New: both user ids sorted ordinally, joined after a `p-` prefix. */
export function getPeerChatId(userIdA: string, userIdB: string): string {
    return `p-${[userIdA, userIdB].sort().join('-')}`;
}

export async function openChat(page: Page, chatId: string): Promise<void> {
    await page.goto(`${BASE_URL}/chat/${chatId}`, { waitUntil: 'domcontentloaded' });
    await waitForEditor(page);
    await ensureConnected(page);
    await waitForDebugUI(page);
}

export function sendMessage(page: Page, text: string): Promise<void> {
    return page.evaluate((message: string) => {
        const debugUI = (globalThis as DebugUIRoot).debugUI;
        if (!debugUI?.fake)
            throw new Error('debugUI.fake is missing - the page is on an older bundle.');

        return debugUI.fake.send(message);
    }, text);
}

export function attachFiles(page: Page, files: DebugAttachment[]): Promise<void> {
    return page.evaluate((attachments: DebugAttachment[]) => {
        const debugUI = (globalThis as DebugUIRoot).debugUI;
        if (!debugUI?.fake)
            throw new Error('debugUI.fake is missing - the page is on an older bundle.');

        return debugUI.fake.attach(attachments);
    }, files);
}

/**
 * Attaches through the real `<input type=file>`, i.e. the path a user takes minus the
 * picker dialog. Preferred over `attachFiles`, which needs the file in memory first.
 */
export async function attachFilePath(page: Page, filePath: string, timeoutMs = 30_000): Promise<void> {
    await page.setInputFiles('.chat-message-editor input.attachment-web-file-picker', filePath);
    // The upload starts on attach, so the message must not be posted before the
    // attachment shows up - Post() snapshots whatever is in the list at that moment.
    await page.locator('.attachment-list .attachment-item').first()
        .waitFor({ state: 'visible', timeout: timeoutMs });
}

export function getLastMessageText(page: Page): Promise<string> {
    return page.evaluate(() => {
        const items = document.querySelectorAll('.chat-message-markup');
        const last = items[items.length - 1];
        return last instanceof HTMLElement ? last.innerText.trim() : '';
    });
}
