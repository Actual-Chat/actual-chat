/**
 * E2E test: form validation through AsyncDataAnnotationsValidator.
 *
 * Covers the migration to the BCL's async DataAnnotations pipeline
 * (Validator.TryValidateObjectAsync / TryValidatePropertyAsync), #4134:
 * - per-field validation on change (sign-in phone-or-email, account phone)
 * - whole-form validation on mount / submit (account name is [Required])
 * - the Form.IsValid gate that enables the submit button
 *
 * Run:
 *   npx vitest run tests/ts/e2e/form-validation.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { BrowserContext, Locator, Page } from 'playwright';
import {
    BASE_URL, connectBrowser, dismissCookieConsent, ensureSignedIn, screenshot,
    skipOnboarding, waitForAppReady, type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e-validation', name);

// FormSection tags itself with the InputId, which FormModel derives as
// "<form-id>-<PropertyName>" — the only stable handle on a section.
function section(root: Locator | Page, property: string): Locator {
    return root.locator(`section.form-section[data-control-id$="-${property}"]`).first();
}

function validationMessage(formSection: Locator): Locator {
    return formSection.locator('.form-section-validation');
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
        () => validationMessage(formSection).innerText().catch(() => ''),
        { timeout, interval: 200 },
    ).toContain(expected);
}

async function expectNoError(formSection: Locator, timeout = 10_000) {
    await expect.poll(
        () => validationMessage(formSection).innerText().catch(() => ''),
        { timeout, interval: 200 },
    ).toBe('');
}

describe('form validation', () => {
    let conn: BrowserConnection;

    beforeAll(async () => {
        conn = await connectBrowser();
    }, 60_000);

    afterAll(async () => {
        if (conn.ownsBrowser) {
            await conn.context.close();
            await conn.browser.close();
        }
    });

    describe('sign-in phone-or-email field', () => {
        let context: BrowserContext;
        let page: Page;
        let phoneOrEmail: Locator;
        let field: Locator;

        beforeAll(async () => {
            // A signed-out context: the sign-in modal is the only place PhoneOrEmailAttribute runs.
            context = await conn.browser.newContext({ ignoreHTTPSErrors: true });
            page = await context.newPage();
            await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
            await waitForAppReady(page);
            await dismissCookieConsent(page);

            await page.locator('button.signin-button-group, button.signin-button').first()
                .click({ force: true });
            const step = page.locator('.provider-select-step');
            await step.waitFor({ state: 'visible', timeout: 30_000 });
            field = section(step, 'PhoneOrEmail');
            phoneOrEmail = field.locator('input').first();
            await phoneOrEmail.waitFor({ state: 'visible', timeout: 15_000 });
        }, 90_000);

        afterAll(async () => {
            await page.screenshot({ path: shot('signin-final') }).catch(() => { /* ignore */ });
            await context.close();
        });

        it('should reject input that is neither a phone nor an email', async () => {
            // act
            await typeAndBlur(phoneOrEmail, 'abc');

            // assert
            await expectError(field, 'Enter a phone number or email address.');
        }, 60_000);

        it('should reject a too-short phone number', async () => {
            // act
            await typeAndBlur(phoneOrEmail, '12345');

            // assert
            await expectError(field, 'Phone number is too short.');
        }, 60_000);

        it('should reject a malformed email address', async () => {
            // act
            await typeAndBlur(phoneOrEmail, 'foo@');

            // assert
            await expectError(field, 'Email address is invalid.');
        }, 60_000);

        it('should accept a valid email and enable Continue', async () => {
            // act
            await typeAndBlur(phoneOrEmail, 'someone@example.com');

            // assert
            await expectNoError(field);
            await expect.poll(
                () => page.locator('.provider-select-step button[type="submit"]').first().isEnabled(),
                { timeout: 10_000, interval: 200 },
            ).toBe(true);
        }, 60_000);

        it('should accept a valid phone number', async () => {
            // act
            await typeAndBlur(phoneOrEmail, '+1 555 555 5550');

            // assert
            await expectNoError(field);
        }, 60_000);
    });

    describe('own account editor', () => {
        let page: Page;
        let modal: Locator;
        let nameSection: Locator;
        let phoneSection: Locator;
        let nameInput: Locator;
        let phoneInput: Locator;
        let originalName: string;
        let originalPhone: string;

        beforeAll(async () => {
            page = await conn.context.newPage();
            await ensureSignedIn(page);

            modal = page.locator('.own-account-editor-modal');
            for (let attempt = 0; attempt < 3; attempt++) {
                await page.goto(`${BASE_URL}/settings/account`, { waitUntil: 'domcontentloaded' });
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
            await modal.waitFor({ state: 'visible', timeout: 5_000 });

            nameSection = section(modal, 'Name');
            phoneSection = section(modal, 'Phone');
            nameInput = nameSection.locator('input').first();
            phoneInput = phoneSection.locator('input').first();
            await nameInput.waitFor({ state: 'visible', timeout: 10_000 });
            originalName = await nameInput.inputValue();
            originalPhone = await phoneInput.inputValue();
        }, 120_000);

        afterAll(async () => {
            await page.screenshot({ path: shot('account-final') }).catch(() => { /* ignore */ });
            // Close without saving — every case below restores the field it touched anyway.
            await page.keyboard.press('Escape').catch(() => { /* ignore */ });
            await page.close();
        });

        it('should report the required Name field and disable Save', async () => {
            // act
            await typeAndBlur(nameInput, '');

            // assert
            await expectError(nameSection, 'The Name field is required.');
            const save = modal.locator('button:has-text("Save")').first();
            await expect.poll(() => save.isDisabled(), { timeout: 10_000, interval: 200 }).toBe(true);

            // cleanup
            await typeAndBlur(nameInput, originalName);
            await expectNoError(nameSection);
            await expect.poll(() => save.isEnabled(), { timeout: 10_000, interval: 200 }).toBe(true);
        }, 60_000);

        it('should report a too-short phone number', async () => {
            // act
            await typeAndBlur(phoneInput, '123');

            // assert
            await expectError(phoneSection, 'Phone number is too short.');
        }, 60_000);

        it('should report invalid phone characters', async () => {
            // act
            await typeAndBlur(phoneInput, '+1 555 555 55x0');

            // assert
            await expectError(phoneSection, 'Phone number contains invalid characters.');
        }, 60_000);

        it('should clear the error once the phone number is valid', async () => {
            // act
            await typeAndBlur(phoneInput, originalPhone || '+1 555 555 5550');

            // assert
            await expectNoError(phoneSection);
        }, 60_000);
    });
});
