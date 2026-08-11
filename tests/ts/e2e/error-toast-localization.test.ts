/**
 * E2E test: server-originated error messages shown in the error toast are
 * AI-translated into the device UI language (LocalizingUIActionFailureTracker →
 * ITranslations.GetTranslatedUIText). Uses the admin-only /test/toasts page,
 * whose "Small Error toast" button pushes a constant English message through
 * the same UICommander failure pipeline as real server errors.
 *
 * Requires the server to run with translation enabled (ChatSettings:
 * IsTranslationEnabled + an OpenAI key); without it the toast falls back to
 * the original English text and the test fails.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/error-toast-localization.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { Page } from 'playwright';
import {
    BASE_URL, connectBrowser, ensureSignedIn, screenshot, setUILanguage,
    waitForAppReady, type BrowserConnection,
} from './helpers';

const ENGLISH = 'en-US';
const RUSSIAN = 'ru-RU';
const ERROR_MESSAGE_FRAGMENT = "There's something wrong here";

const shot = (name: string) => screenshot('e2e', `error-toast-l10n-${name}`);

describe('Error toast localization', () => {
    let conn: BrowserConnection;
    let page: Page;
    let originalLanguage = ENGLISH;

    beforeAll(async () => {
        conn = await connectBrowser();
        page = await conn.context.newPage();
        await ensureSignedIn(page);

        // Server render mode: the language change ends in History.ForceReload, and under WASM
        // that re-boots MONO against service-worker-cached assets, which double-faults after a
        // server rebuild ("SystemInteropJS_BindAssemblyExports unexpected double fault").
        await page.goto(`${BASE_URL}/fusion/renderMode/s`, { waitUntil: 'domcontentloaded' });
        await waitForAppReady(page);
    }, 120_000);

    afterAll(async () => {
        // The CDP browser profile is shared across tests — always restore the language.
        try {
            await setUILanguage(page, originalLanguage);
        } catch (e) {
            console.log('Failed to restore UI language:', e instanceof Error ? e.message : String(e));
        }
        await page.close();
        if (conn.ownsBrowser) {
            await conn.context.close();
            await conn.browser.close();
        }
    }, 60_000);

    it('shows a server-error toast translated into the UI language', async () => {
        originalLanguage = await setUILanguage(page, RUSSIAN);
        await page.screenshot({ path: shot('language-set') });

        await page.goto(`${BASE_URL}/test/toasts`, { waitUntil: 'domcontentloaded' });
        await waitForAppReady(page);
        // The test page's own literals are not localized, so this text is stable.
        const errorButton = page.locator('button:has-text("Small Error toast")').first();
        await errorButton.waitFor({ state: 'visible', timeout: 15_000 });
        await errorButton.click();

        // The toast is published only after translation completes (or after the
        // localizer's 10s timeout, which falls back to the English original).
        const toastMessage = page.locator('.error-toast .c-message').first();
        await toastMessage.waitFor({ state: 'visible', timeout: 20_000 });
        const text = (await toastMessage.innerText()).trim();
        await page.screenshot({ path: shot('toast') });
        console.log(`Error toast text: "${text}"`);

        expect(text, 'toast still shows the English original — is translation enabled on the server?')
            .not.toContain(ERROR_MESSAGE_FRAGMENT);
        expect(/[А-Яа-яЁё]/.test(text), `expected Russian translation, got: "${text}"`).toBe(true);
    }, 90_000);
});
