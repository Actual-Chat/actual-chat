import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import type { Locator, Page } from 'playwright';
import {
    BASE_URL, connectBrowser, ensureSignedIn, openLanguageSelect, setUILanguage,
    waitForAppReady, type BrowserConnection,
} from './helpers';

const English = 'en-US';
const Spanish = 'es-ES';

describe('UI language picker', () => {
    let connection: BrowserConnection;
    let page: Page;
    let originalLanguage = English;

    beforeAll(async () => {
        connection = await connectBrowser();
        page = await connection.context.newPage();
        await ensureSignedIn(page);
        await page.goto(`${BASE_URL}/fusion/renderMode/s`, { waitUntil: 'domcontentloaded' });
        await waitForAppReady(page);
        originalLanguage = await setUILanguage(page, English);
    }, 180_000);

    afterAll(async () => {
        await setUILanguage(page, originalLanguage).catch(() => undefined);
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
        expect(promptText).toContain('Apply language change now?');
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
