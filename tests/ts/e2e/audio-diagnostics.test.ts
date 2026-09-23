/**
 * E2E test: Audio Diagnostics modal opens from the chat activity panel and renders its tabs.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/audio-diagnostics.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, beforeAll, afterAll, expect } from 'vitest';
import type { Page } from 'playwright';
import {
    BASE_URL, connectBrowser, dismissCookieConsent, ensureSignedIn, openChat,
    screenshot, waitForAppReady, withUILanguage, type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e', `audio-diagnostics-${name}`);

describe('Audio Diagnostics modal opens from the chat activity panel', () => {
    let conn: BrowserConnection;
    let page: Page;

    beforeAll(async () => {
        conn = await connectBrowser();
        page = await conn.context.newPage();
        await page.goto(`${BASE_URL}/fusion/renderMode/w`, { waitUntil: 'domcontentloaded' });
        await ensureSignedIn(page);
    }, 120_000);

    afterAll(async () => {
        await page.close();
        if (conn.ownsBrowser) {
            await conn.context.close();
            await conn.browser.close();
        }
    });

    it('enables audio diagnostics, opens the modal, and shows the session tab', async () => {
        await page.goto(withUILanguage(`${BASE_URL}/settings`), { waitUntil: 'domcontentloaded' });
        await waitForAppReady(page);
        await dismissCookieConsent(page);

        const devToolsTab = page.locator('.settings-tab-item:has-text("Developer tools")').first();
        await devToolsTab.waitFor({ state: 'visible', timeout: 15_000 });
        await devToolsTab.click({ force: true });

        const enable = page.locator(
            '.settings-tab-content .tile-item:has-text("Enable audio diagnostics")',
        ).first();
        await enable.waitFor({ state: 'visible', timeout: 10_000 });
        // The setting is read into the model a beat after the first render, so poll the toggle's
        // own state instead of clicking blind — a click during that window flips it back off.
        // aria-checked, not data-input-checked: the latter holds a bool, and Blazor renders a
        // bool attribute the HTML way — present but empty when set, absent when not.
        const toggle = enable.locator('label.toggle').first();
        for (let i = 0; i < 8; i++) {
            if (await toggle.getAttribute('aria-checked') === 'true')
                break;

            await enable.click({ force: true });
            await page.waitForTimeout(800);
        }
        expect(await toggle.getAttribute('aria-checked')).toBe('true');

        // The button that shows the modal lives in the chat activity panel, which renders only
        // during call activity — recording is the cheapest of those, and the mic is fake here.
        await openChat(page);
        await conn.context.grantPermissions(['microphone'], { origin: BASE_URL });
        await page.locator('.recorder-wrapper button').first().click();
        const activityPanel = page.locator('.chat-activity-panel').first();
        await activityPanel.waitFor({ state: 'visible', timeout: 20_000 });

        try {
            await activityPanel.locator('.c-diagnostics-btn').first().click({ force: true });

            const modal = page.locator('.audio-diagnostics-modal').first();
            await modal.waitFor({ state: 'visible', timeout: 10_000 });

            // The Session tab and the native audio-session section render on the web build
            // (Web Audio context is web-only; the native session shows "not managed").
            await modal.locator('.diag-tab:has-text("Session")').first()
                .waitFor({ state: 'visible', timeout: 5_000 });
            await modal.locator('.diag-section-header:has-text("Native audio session")').first()
                .waitFor({ state: 'visible', timeout: 5_000 });

            await page.screenshot({ path: shot('opened') });
            await page.keyboard.press('Escape');
            await modal.waitFor({ state: 'hidden', timeout: 10_000 }).catch(() => { /* ignore */ });
        } finally {
            // Hang up, so the next spec doesn't start inside a call.
            await activityPanel.locator('.chat-audio-controls .c-hangup').first()
                .click({ force: true }).catch(() => { /* ignore */ });
            await activityPanel.waitFor({ state: 'hidden', timeout: 10_000 })
                .catch(() => { /* ignore */ });
        }
    }, 120_000);
});
