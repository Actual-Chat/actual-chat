import type { Page } from 'playwright';

export type RecorderState = 'off' | 'on' | 'listen' | 'take-phone' | 'mic-disabled' | 'no-btn' | 'unknown';

export function getRecorderState(page: Page): Promise<RecorderState> {
    return page.evaluate(() => {
        const btn = document.querySelector('.rec-btn');
        if (!btn)
            return 'no-btn';

        const classes = btn.className;
        if (classes.includes('record-on-btn'))
            return 'on';
        if (classes.includes('record-off-btn'))
            return 'off';
        if (classes.includes('listen-only-btn'))
            return 'listen';
        if (classes.includes('take-phone-btn'))
            return 'take-phone';
        if (classes.includes('mic-disabled'))
            return 'mic-disabled';

        return 'unknown';
    });
}

export function isDisconnected(page: Page): Promise<boolean> {
    return page.evaluate(() => {
        const overlay = document.querySelector('#app-connection-state.reconnect-overlay');
        if (!(overlay instanceof HTMLElement))
            return false;

        const style = getComputedStyle(overlay);
        return style.display !== 'none' && style.visibility !== 'hidden' && overlay.offsetParent !== null;
    });
}

/** Reloads when the reconnect overlay is up, then waits for the chat UI to be usable. */
export async function ensureConnected(page: Page, timeoutMs = 30_000): Promise<void> {
    if (await isDisconnected(page))
        await page.reload({ waitUntil: 'domcontentloaded' });

    await page.locator('#message-input .editor-content[contenteditable="true"]')
        .first()
        .waitFor({ state: 'visible', timeout: timeoutMs });
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        if (!await isDisconnected(page))
            return;

        await page.waitForTimeout(500);
    }
    throw new Error(`ensureConnected: still disconnected after ${timeoutMs}ms.`);
}

/** Skips the onboarding bubbles - they sit over the editor and swallow clicks meant for it. */
export async function dismissBubbles(page: Page, timeoutMs = 5_000): Promise<void> {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        const skip = page.locator('.btn-bubble:not(.btn-next)').first();
        const next = page.locator('.btn-bubble.btn-next').first();
        if (await skip.isVisible())
            await skip.click();
        else if (await next.isVisible())
            await next.click();
        else
            return;

        await page.waitForTimeout(300);
    }
}

// A real click is what supplies the user activation that resumes suspended AudioContexts.
// It's forced because the button pulses while recording and never counts as stable.
export async function clickRecorder(page: Page): Promise<void> {
    await dismissBubbles(page);
    const button = page.locator('.rec-btn');
    await button.waitFor({ state: 'visible', timeout: 8_000 });
    await button.click({ force: true });
}
