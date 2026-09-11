/**
 * E2E test: the "(setup)" links in the voice-settings and translation-language modals open
 * Settings on the Voice & Transcription tab, not on the first tab (#4486).
 *
 * Run:
 *   npx vitest run tests/ts/e2e/language-setup-opens-transcription-settings.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { Locator, Page } from 'playwright';
import {
    BASE_URL, connectBrowser, ensureSignedIn, screenshot, skipOnboarding,
    waitForChatReady, waitForEditor, type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e', `4486-language-setup-${name}`);

const CHAT_URL = `${BASE_URL}/chat/the-actual-one`;
const TRANSCRIPTION_TAB = 'transcription';

describe('language "(setup)" link opens the Voice & Transcription settings tab', () => {
    let conn: BrowserConnection;
    let page: Page;
    let mustHideTranslationSubHeader = false;

    beforeAll(async () => {
        conn = await connectBrowser();
        page = await conn.context.newPage();
        await ensureSignedIn(page);
    }, 120_000);

    afterAll(async () => {
        if (mustHideTranslationSubHeader) {
            await openChat(page);
            await page.locator('.chat-header-control-panel .translation-btn').first().click({ force: true })
                .catch(() => { /* ignore */ });
        }
        await page.close().catch(() => { /* ignore */ });
        if (conn.ownsBrowser) {
            await conn.context.close();
            await conn.browser.close();
        }
    }, 60_000);

    it('from the transcription language modal', async () => {
        await openChat(page);

        // The language options button: a round button on a wide screen, a text button on a narrow one.
        await page.locator('.transcription-options-btn, .volume-settings-btn').first().click({ force: true });
        const modal = page.locator('.transcription-options-modal').first();
        await modal.waitFor({ state: 'visible', timeout: 10_000 });
        await page.screenshot({ path: shot('voice-modal') });

        // "(setup)" shows with 2+ languages; with one, the hint's "Settings" link opens the same tab.
        await modal.locator('.language-btn-group .c-edit, .language-btn-group .c-settings-hint .link')
            .first().click({ force: true });

        await expectTranscriptionTabSelected(page, 'voice-settings');
        await closeSettings(page);
    }, 60_000);

    it('from the translation language modal', async () => {
        await openChat(page);

        const subHeader = page.locator('.translation-sub-header').first();
        if (!await subHeader.isVisible().catch(() => false)) {
            await page.locator('.chat-header-control-panel .translation-btn').first().click({ force: true });
            mustHideTranslationSubHeader = true;
        }
        await subHeader.waitFor({ state: 'visible', timeout: 10_000 });
        await subHeader.locator('.c-language').first().click({ force: true });

        const modal = page.locator('.translation-language-modal').first();
        await modal.waitFor({ state: 'visible', timeout: 10_000 });
        await page.screenshot({ path: shot('translation-modal') });
        await modal.locator('.language-btn-group .c-edit').first().click({ force: true });

        await expectTranscriptionTabSelected(page, 'translation-settings');
        await closeSettings(page);
    }, 60_000);
});

async function openChat(page: Page) {
    await page.goto(CHAT_URL, { waitUntil: 'domcontentloaded' });
    await waitForChatReady(page);
    await skipOnboarding(page);
    await waitForEditor(page);
}

async function expectTranscriptionTabSelected(page: Page, shotName: string) {
    const settings = page.locator('.settings-modal').first();
    await settings.waitFor({ state: 'visible', timeout: 15_000 });
    const selectedTab = settings.locator('.c-tab-item.on button[data-tab-id]').first();
    await selectedTab.waitFor({ state: 'visible', timeout: 10_000 });
    // The wrong tab renders its content right away; give the selection a beat to settle
    // so the assertion catches a late fallback to the first tab too.
    await page.waitForTimeout(1_000);
    await page.screenshot({ path: shot(shotName) });

    expect(await selectedTab.getAttribute('data-tab-id')).toBe(TRANSCRIPTION_TAB);
    expect((await tabContentHeaderOf(settings).innerText()).trim()).toBe(await tabTitleOf(selectedTab));
}

function tabContentHeaderOf(settings: Locator): Locator {
    return settings.locator('.settings-tab .settings-tab-header .c-title').first();
}

async function tabTitleOf(tab: Locator): Promise<string> {
    return (await tab.locator('.settings-tab-item > span').first().innerText()).trim();
}

async function closeSettings(page: Page) {
    const settings = page.locator('.settings-modal').first();
    await page.keyboard.press('Escape');
    await settings.waitFor({ state: 'hidden', timeout: 10_000 }).catch(() => { /* ignore */ });
}
