import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import type { Locator, Page } from 'playwright';
import {
    BASE_URL, connectBrowser, ensureSignedIn, openLanguageSelect, setUILanguage,
    waitForAppReady, type BrowserConnection,
} from './helpers';
import { ENGLISH, OFFERED_UI_LANGUAGES, UI_LANGUAGES, getTranslatedValues, loadStrings } from './localization-catalog';

const English = 'en-US';
const Spanish = 'es-ES';
// Fewer catalog texts than this on the settings screen means the reload didn't redraw it
const MinTranslatedTexts = 3;

describe('UI language picker', () => {
    let connection: BrowserConnection;
    let page: Page;

    beforeAll(async () => {
        connection = await connectBrowser();
        page = await connection.context.newPage();
        await ensureSignedIn(page);
        await page.goto(`${BASE_URL}/fusion/renderMode/s`, { waitUntil: 'domcontentloaded' });
        await waitForAppReady(page);
        await setUILanguage(page, English);
    }, 180_000);

    afterAll(async () => {
        // The language lives on the shared account, so a failed restore fails every later spec.
        await page.keyboard.press('Escape').catch(() => { /* ignore */ });
        try {
            await setUILanguage(page, English);
        } catch (e) {
            console.log('Failed to restore the UI language to English:', e instanceof Error ? e.message : String(e));
        }
        await page.close();
        if (connection.ownsBrowser) {
            await connection.context.close();
            await connection.browser.close();
        }
    }, 120_000);

    it('should prompt and reload when the effective UI language changes', async () => {
        // arrange
        const select = await openLanguageSelect(page);
        const prompt = page.locator('.confirm-modal').first();

        // act
        await select.selectOption(English);
        await page.waitForTimeout(300);

        // assert
        expect(await page.getByText('UI language', { exact: true }).first().isVisible()).toBe(true);
        expect(await prompt.isVisible()).toBe(false);

        // act
        await select.selectOption(Spanish);
        await prompt.waitFor({ state: 'visible', timeout: 10_000 });

        // assert
        const promptText = await prompt.innerText();
        expect(promptText).toContain('Apply now?');
        expect(promptText).toContain('Reload the UI to use the selected language.');
        expect(promptText).toContain('Later');
        expect(promptText).toContain('Reload');

        // act
        const currentUrl = page.url();
        await reload(page, prompt.getByRole('button', { name: 'Reload', exact: true }));

        // assert
        expect(page.url()).toBe(currentUrl);
        expect(await page.locator('html').getAttribute('lang')).toBe(Spanish);
    }, 90_000);

    describe('every language', () => {
        beforeAll(async () => {
            await setUILanguage(page, English);
        }, 60_000);

        // English goes last, so each switch leaves a language the previous case applied
        for (const language of [...OFFERED_UI_LANGUAGES.filter(x => x !== ENGLISH), ENGLISH]) {
            it(`should apply ${language.nativeName} (${language.code})`, async () => {
                // arrange
                const select = await openLanguageSelect(page);
                const previousCode = await select.inputValue();
                const previous = UI_LANGUAGES.find(x => x.code === previousCode) ?? ENGLISH;
                const previousStrings = loadStrings(previous.subtag);
                const prompt = page.locator('.confirm-modal').first();

                // act
                await select.selectOption(language.code);
                await prompt.waitFor({ state: 'visible', timeout: 10_000 });

                // assert
                expect(await prompt.innerText()).toContain(previousStrings.Settings_UILanguageApplyTitle);

                // act
                const reloadButton = prompt.getByRole('button', { name: previousStrings.Navbar_Reload, exact: true });
                await reload(page, reloadButton);

                // assert
                expect(await page.locator('html').getAttribute('lang')).toBe(language.code);
                const modal = page.locator('.settings-modal').first();
                await modal.waitFor({ state: 'visible', timeout: 15_000 });
                const expectedTexts = language === ENGLISH
                    ? new Set(Object.values(loadStrings(ENGLISH.subtag)))
                    : getTranslatedValues(language);
                const shownTexts = (await modal.innerText()).split('\n').map(x => x.trim());
                const translatedTexts = new Set(shownTexts.filter(x => expectedTexts.has(x)));
                expect(translatedTexts.size, `settings in ${language.code} show: ${shownTexts.join(' | ')}`)
                    .toBeGreaterThanOrEqual(MinTranslatedTexts);
            }, 60_000);
        }
    });
});

async function reload(page: Page, button: Locator): Promise<void> {
    const whenReloaded = page.waitForEvent('framenavigated', {
        predicate: frame => frame === page.mainFrame(),
        timeout: 20_000,
    });
    await button.click();
    await whenReloaded;
    await page.waitForLoadState('domcontentloaded');
    await waitForAppReady(page);
}
