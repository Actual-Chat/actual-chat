/**
 * E2E test: validation messages render in the user's UI language, field name included.
 *
 * Covers both routes of docs/plans/validation-localization-forward-keys.md:
 * - a BCL attribute ([Required]) produces English that MessageIndex reverse-matches back to
 *   Validation_Required_Format, whose {field} is then filled from the localized FormSection label
 * - our own attribute ([PhoneNumber]) reports a Validation_* key that resolves directly
 *
 * The Name case is the interesting one: it fails if either half is wrong - an unmatched template
 * leaves the sentence English, and an unlocalized label leaves an English noun inside a Russian
 * sentence ("Заполните поле «Name»."), which is exactly what the Form_* keys exist to prevent.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/validation-localization.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { Locator, Page } from 'playwright';
import {
    BASE_URL, connectBrowser, ensureSignedIn, screenshot, setUILanguage,
    skipOnboarding, waitForAppReady, type BrowserConnection,
} from './helpers';

const ENGLISH = 'en-US';
const RUSSIAN = 'ru-RU';

const shot = (name: string) => screenshot('e2e-validation', `l10n-${name}`);

// FormSection tags itself with the InputId, which FormModel derives as
// "<form-id>-<PropertyName>" — the only stable handle on a section.
function section(root: Locator | Page, property: string): Locator {
    return root.locator(`section.form-section[data-control-id$="-${property}"]`).first();
}

// TextBox debounces its input listener, so type + blur (Tab fires Blazor's
// @onchange) and give the validation worker a moment to drain its queue.
async function typeAndBlur(input: Locator, value: string) {
    await input.click();
    await input.selectText();
    if (value === '')
        await input.press('Backspace');
    else
        await input.pressSequentially(value, { delay: 20 });
    await input.press('Tab');
}

async function expectError(formSection: Locator, expected: string, timeout = 10_000) {
    await expect.poll(
        () => formSection.locator('.form-section-validation').innerText().catch(() => ''),
        { timeout, interval: 200 },
    ).toContain(expected);
}

describe('validation message localization', () => {
    let conn: BrowserConnection;
    let page: Page;
    let modal: Locator;
    let nameSection: Locator;
    let phoneSection: Locator;
    let nameInput: Locator;
    let phoneInput: Locator;
    let originalName: string;
    let originalLanguage = ENGLISH;

    beforeAll(async () => {
        conn = await connectBrowser();
        page = await conn.context.newPage();
        await ensureSignedIn(page);

        // Server render mode: a language change ends in History.ForceReload, and under WASM the
        // reload re-boots MONO against whatever the service worker cached, which double-faults
        // after a server rebuild. Server mode is also where per-circuit localization matters.
        await page.goto(`${BASE_URL}/fusion/renderMode/s`, { waitUntil: 'domcontentloaded' });
        await waitForAppReady(page);

        originalLanguage = await setUILanguage(page, RUSSIAN);

        // The forced reload leaves the settings modal open; come back on a clean route.
        await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' }).catch(() => { /* ignore */ });
        await waitForAppReady(page);

        modal = page.locator('.own-account-editor-modal');
        for (let attempt = 0; attempt < 3; attempt++) {
            const navigated = await page
                .goto(`${BASE_URL}/settings/account`, { waitUntil: 'domcontentloaded' })
                .then(() => true).catch(() => false);
            if (!navigated)
                continue;

            await waitForAppReady(page).catch(() => { /* ignore */ });
            await skipOnboarding(page);
            const tile = page.locator('.your-account-tile .first-tile-item').first();
            const shown = await tile.waitFor({ state: 'visible', timeout: 15_000 })
                .then(() => true).catch(() => false);
            if (!shown)
                continue;

            await tile.click({ force: true });
            const opened = await modal.waitFor({ state: 'visible', timeout: 10_000 })
                .then(() => true).catch(() => false);
            if (opened)
                break;
        }
        await modal.waitFor({ state: 'visible', timeout: 5_000 })
            .catch(async (e: unknown) => {
                await page.screenshot({ path: shot('no-modal') }).catch(() => { /* ignore */ });
                throw e;
            });

        nameSection = section(modal, 'Name');
        phoneSection = section(modal, 'Phone');
        nameInput = nameSection.locator('input').first();
        phoneInput = phoneSection.locator('input').first();
        await nameInput.waitFor({ state: 'visible', timeout: 10_000 });
        originalName = await nameInput.inputValue();
    }, 180_000);

    afterAll(async () => {
        await page.screenshot({ path: shot('final') }).catch(() => { /* ignore */ });
        // Nothing is saved, but the language lives in the shared browser profile.
        await page.keyboard.press('Escape').catch(() => { /* ignore */ });
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
    }, 120_000);

    it('should localize the label a required field reports', async () => {
        // arrange
        // "Имя" is Form_Name/YourAccount_Name — the label the user sees, substituted into {field}.
        const label = (await nameSection.locator('.form-section-label').innerText()).trim();

        // act
        await typeAndBlur(nameInput, '');

        // assert
        await expectError(nameSection, 'Заполните поле');
        const message = (await nameSection.locator('.form-section-validation').innerText()).trim();
        await page.screenshot({ path: shot('required') });
        console.log(`Label: "${label}" | message: "${message}"`);

        expect(message, 'the field name must be the localized label, not the English property name')
            .toContain(label);
        expect(/[A-Za-z]/.test(message), `message still contains Latin letters: "${message}"`)
            .toBe(false);

        // cleanup
        await typeAndBlur(nameInput, originalName);
    }, 90_000);

    it('should localize a message our own attribute reports by key', async () => {
        // act
        await typeAndBlur(phoneInput, '123');

        // assert
        await expectError(phoneSection, 'Номер телефона слишком короткий.');
        await page.screenshot({ path: shot('phone') });
    }, 90_000);
});
