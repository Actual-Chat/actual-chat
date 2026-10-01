/** Shared steps for e2e tests that need a call between two signed-in users in their peer chat. */

import type { Page } from 'playwright';
import { BASE_URL, withUILanguage } from './helpers';

export async function getUserId(page: Page): Promise<string> {
    return await page.evaluate(() => (window as unknown as { debugUI: { getUserId(): Promise<string> } })
        .debugUI.getUserId());
}

export async function send(page: Page, text: string) {
    // Focused, not clicked: the record button overlaps the editor on the narrow layout.
    const editor = page.locator('#message-input .editor-content[contenteditable="true"]').first();
    await editor.waitFor({ state: 'visible', timeout: 30_000 });
    await editor.focus();
    await page.keyboard.type(text, { delay: 20 });
    await page.keyboard.press('Enter');
    await page.waitForTimeout(1_500);
}

/** Opens the peer chat on both sides and has each post there: the callee's reply opens the peer-call gate. */
export async function openPeerChat(caller: Page, callee: Page) {
    const userIds = [await getUserId(caller), await getUserId(callee)].sort();
    const pm = withUILanguage(`${BASE_URL}/chat/p-${userIds.join('-')}`);
    await caller.goto(pm, { waitUntil: 'domcontentloaded' });
    await send(caller, 'call test');
    await callee.goto(pm, { waitUntil: 'domcontentloaded' });
    await send(callee, 'call test reply');
    await caller.reload({ waitUntil: 'domcontentloaded' });
}

/** Dials from the caller's open peer chat and accepts on the callee. */
export async function dialPeerCall(caller: Page, callee: Page) {
    const callButton = caller.locator('.btn-start-call').first();
    await callButton.waitFor({ state: 'visible', timeout: 30_000 });
    await callButton.click();
    const accept = callee.locator('.btn-call.c-accept').first();
    await accept.waitFor({ state: 'visible', timeout: 30_000 });
    await accept.click();
}

export async function startPeerCall(caller: Page, callee: Page) {
    await openPeerChat(caller, callee);
    await dialPeerCall(caller, callee);
}
