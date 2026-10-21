/**
 * E2E test: the "Translate to:" languages in the translation-language modal render as a styled
 * switch - separate segments, the selected one marked - not as run-together text (#5532).
 *
 * Run:
 *   AC_E2E_SERVER=external npx vitest run tests/ts/e2e/translation-language-modal-switch.test.ts \
 *     --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { Locator, Page } from 'playwright';
import {
    BASE_URL, connectBrowser, ensureSignedIn, openChat, screenshot, withUILanguage, type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e', `5532-translation-language-modal-${name}`);

const SPOKEN_LANGUAGES = ['en-US', 'ru-RU', 'kk-KZ'];

describe('translation language modal', () => {
    let conn: BrowserConnection;
    let page: Page;
    let originalLanguages: string[] | null = null;
    let mustHideTranslationSubHeader = false;

    beforeAll(async () => {
        conn = await connectBrowser();
        page = await conn.context.newPage();
        await ensureSignedIn(page);
        await openChat(page);
        originalLanguages = await setSpokenLanguages(page, SPOKEN_LANGUAGES);
    }, 120_000);

    afterAll(async () => {
        if (originalLanguages)
            await setSpokenLanguages(page, originalLanguages).catch(() => { /* ignore */ });
        if (mustHideTranslationSubHeader) {
            await openChat(page).catch(() => { /* ignore */ });
            await page.locator('.chat-header-control-panel .translation-btn').first().click({ force: true })
                .catch(() => { /* ignore */ });
        }
        await page.close().catch(() => { /* ignore */ });
        if (conn.ownsBrowser) {
            await conn.context.close();
            await conn.browser.close();
        }
    }, 90_000);

    it('shows "Translate to:" languages as separate segments with the selected one marked', async () => {
        // arrange
        await openChat(page);
        const subHeader = page.locator('.translation-sub-header').first();
        if (!await subHeader.isVisible().catch(() => false)) {
            await page.locator('.chat-header-control-panel .translation-btn').first().click({ force: true });
            mustHideTranslationSubHeader = true;
        }
        await subHeader.waitFor({ state: 'visible', timeout: 10_000 });

        // act
        const modal = page.locator('.translation-language-modal').first();
        await impl();
        const buttons = modal.locator('.language-btn-group button');
        await buttons.first().waitFor({ state: 'visible', timeout: 10_000 });
        await page.waitForTimeout(500);
        await page.screenshot({ path: shot('modal') });

        // assert
        expect(await buttons.count()).toBe(SPOKEN_LANGUAGES.length);
        const segments = await Promise.all((await buttons.all()).map(button => button.evaluate(
            (el: HTMLElement): Segment => {
                const rect = el.getBoundingClientRect();
                return {
                    text: el.innerText.trim(),
                    left: rect.left,
                    right: rect.right,
                    height: rect.height,
                    isClipped: el.scrollWidth > el.clientWidth,
                    isSelected: el.getAttribute('aria-selected') === 'true',
                    color: getComputedStyle(el).color,
                };
            })));
        for (const segment of segments) {
            expect(segment.height, `"${segment.text}" height`).toBeGreaterThanOrEqual(32);
            expect(segment.isClipped, `"${segment.text}" label is clipped`).toBe(false);
        }
        for (let i = 1; i < segments.length; i++) {
            const gap = segments[i].left - segments[i - 1].right;
            expect(gap, `gap between "${segments[i - 1].text}" and "${segments[i].text}"`).toBeGreaterThan(0);
        }
        const selected = segments.filter(s => s.isSelected);
        expect(selected).toHaveLength(1);
        for (const segment of segments.filter(s => !s.isSelected))
            expect(segment.color, `"${segment.text}" looks the same as the selected one`).not.toBe(selected[0].color);

        await page.keyboard.press('Escape');
        await modal.waitFor({ state: 'hidden', timeout: 10_000 }).catch(() => { /* ignore */ });

        // The trigger sits in a sub-header that has just slid in, and force: true aims at the box as
        // it was measured - a single click can land beside it and be lost with no trace at all.
        async function impl() {
            for (let attempt = 0; attempt < 3; attempt++) {
                await subHeader.locator('.c-language').first().click({ force: true }).catch(() => { /* ignore */ });
                const opened = await modal.waitFor({ state: 'visible', timeout: 5_000 })
                    .then(() => true).catch(() => false);
                if (opened)
                    return;
            }
            await page.screenshot({ path: shot('missing') });
            throw new Error('The translation language modal never opened');
        }
    }, 60_000);
});

interface Segment {
    text: string;
    left: number;
    right: number;
    height: number;
    isClipped: boolean;
    isSelected: boolean;
    color: string;
}

/** Sets the account's primary, second and third spoken languages ('' = none) on the Voice &
 *  Transcription settings tab and returns the ones it replaced. */
async function setSpokenLanguages(page: Page, languages: string[]): Promise<string[]> {
    await page.goto(withUILanguage(`${BASE_URL}/settings`), { waitUntil: 'domcontentloaded' });
    const tab = page.locator('.settings-modal button[data-tab-id="transcription"]').first();
    await tab.waitFor({ state: 'visible', timeout: 30_000 });
    await tab.click({ force: true });
    const selects = page.locator('.settings-modal .languages-form select');
    await selects.nth(2).waitFor({ state: 'visible', timeout: 15_000 });
    const original = await Promise.all([0, 1, 2].map(i => selects.nth(i).inputValue()));
    // Filled slots first, then the cleared ones from the end, so no slot is ever left empty in the middle.
    for (let i = 0; i < 3; i++) {
        if (languages[i])
            await impl(selects.nth(i), languages[i]);
    }
    for (let i = 2; i >= 1; i--) {
        if (!languages[i])
            await impl(selects.nth(i), '');
    }
    await page.keyboard.press('Escape');
    await page.locator('.settings-modal').first().waitFor({ state: 'hidden', timeout: 10_000 })
        .catch(() => { /* ignore */ });
    return original;

    async function impl(select: Locator, language: string) {
        if (await select.inputValue() === language)
            return;

        await select.selectOption(language);
        // The form re-renders from the saved settings, so the value sticks only once they are saved.
        await select.page().waitForTimeout(1_000);
        if (await select.inputValue() !== language)
            throw new Error(`The spoken language didn't change to "${language}"`);
    }
}
