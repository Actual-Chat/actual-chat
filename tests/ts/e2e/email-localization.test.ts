/**
 * E2E test: every email the server sends is worded in the language the recipient's app is in.
 *
 * - The sign-in code goes to a first-time visitor, who has no account: the server learns their
 *   language only from the guest settings the client stores on startup. Each case opens the app
 *   in a fresh context with its own browser locale and asks for a code as that guest.
 * - The email verification code and the daily digest go to a signed-in account, in the UI
 *   language chosen in its settings. One throwaway account gets an unread chat, then each case
 *   signs in again (a session may request one code a minute), picks a language, and triggers both.
 *
 * The mails are read back from smtp4dev - the mail catcher docker-compose runs on :25 (SMTP) and
 * :5080 (web UI + API), where every mail the local server sends ends up - and each one is
 * rendered to tmp/e2e-email-<kind>-<language>.png, to look at.
 *
 * Skipped when smtp4dev isn't reachable; AC_E2E_SMTP4DEV_URL overrides its address. The digest
 * needs a working summarizer (CoreSettings__OpenAIKey): without one a digest has no content and
 * is never sent.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/email-localization.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { BrowserContext, Page } from 'playwright';
import {
    BASE_URL, TEST_EMAIL_2, connectBrowser, getLocalHosts, newUserContext, openChat, requestEmailCode,
    screenshot, setUILanguage, skipOnboarding, waitForAppReady, type BrowserConnection,
} from './helpers';
import { ENGLISH, loadStrings, UI_LANGUAGES, type Catalog } from './localization-catalog';

const APP_NAME = 'Voxt';
const LANGUAGE_CODES = ['es-ES', 'ru-RU', 'de-DE', 'ja-JP'];

interface CapturedMail {
    id: string;
    to: string;
    subject: string;
    receivedDate: string;
}

async function findSmtp4DevUrl(): Promise<string | null> {
    const candidates = process.env.AC_E2E_SMTP4DEV_URL
        ? [process.env.AC_E2E_SMTP4DEV_URL]
        : getLocalHosts().map(host => `http://${host}:5080`);
    for (const url of candidates) {
        const isReachable = await fetch(`${url}/api/messages`, { signal: AbortSignal.timeout(3000) })
            .then(x => x.ok, () => false);
        if (isReachable)
            return url;
    }
    return null;
}

// Empty when smtp4dev isn't reachable: every hook then returns early and every test skips itself
let smtp4DevUrl = '';

/** The newest mail to `to` received at or after `since` - one address gets a digest per language. */
async function findMail(to: string, since: Date): Promise<CapturedMail | null> {
    const response = await fetch(`${smtp4DevUrl}/api/messages`, { signal: AbortSignal.timeout(5000) });
    const body = await response.json() as { results?: CapturedMail[] } | CapturedMail[];
    const mails = Array.isArray(body) ? body : body.results ?? [];
    return mails
        .filter(x => x.to === to && new Date(x.receivedDate) >= since)
        .sort((a, b) => b.receivedDate.localeCompare(a.receivedDate))[0] ?? null;
}

async function waitForMail(to: string, since: Date, timeout: number, hint = ''): Promise<CapturedMail> {
    let mail: CapturedMail | null = null;
    await expect.poll(async () => mail = await findMail(to, since), {
        timeout,
        interval: 500,
        message: `no mail to ${to} reached smtp4dev. ${hint}`,
    }).not.toBeNull();
    return mail!;
}

// The renderer escapes everything outside Basic Latin, so the text is compared decoded
function decodeHtml(html: string): string {
    return html
        .replace(/&#x([0-9a-f]+);/gi, (_, hex: string) => String.fromCodePoint(parseInt(hex, 16)))
        .replace(/&#(\d+);/g, (_, dec: string) => String.fromCodePoint(parseInt(dec, 10)))
        .replace(/&quot;/g, '"')
        .replace(/&amp;/g, '&');
}

/** Saves the mail as tmp/e2e-email-<kind>-<subtag>.png and returns its decoded HTML. */
async function readMail(context: BrowserContext, mail: CapturedMail, kind: string, subtag: string): Promise<string> {
    const html = await (await fetch(`${smtp4DevUrl}/api/messages/${mail.id}/html`)).text();
    const page = await context.newPage();
    await page.setViewportSize({ width: 640, height: 900 });
    await page.setContent(html, { waitUntil: 'load' });
    await page.screenshot({ path: screenshot(`e2e-email-${kind}`, subtag), fullPage: true });
    await page.close();
    return decodeHtml(html);
}

function expectLocalizedChrome(text: string, subtag: string, strings: Catalog) {
    expect(text).toContain(`lang="${subtag}"`);
    expect(text).toContain(strings['Documents_PrivacyPolicy']);
    expect(text).toContain(strings['Documents_TermsConditions']);
}

function expectCodeMail(text: string, subtag: string, strings: Catalog, english: Catalog) {
    expectLocalizedChrome(text, subtag, strings);
    expect(text).toContain(strings['EmailCode_Title']);
    expect(text).toContain(strings['EmailCode_Prompt_Format'].replace('{0}', APP_NAME));
    expect(text).toContain(strings['EmailCode_Warning']);
    expect(text).not.toContain(english['EmailCode_Title']);
    expect(text).not.toContain(english['EmailCode_Warning']);
}

async function postMessage(page: Page, text: string) {
    const messageInput = page.locator('#message-input .editor-content[contenteditable="true"]').first();
    await messageInput.click({ force: true });
    // ChatMessageEditor restores per-chat drafts, so a leftover one would be prepended to ours
    await messageInput.evaluate(el => {
        el.innerHTML = '';
        el.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await messageInput.click({ force: true });
    await page.keyboard.type(text);
    // MarkupEditor posts on 'keypress' for Enter; dispatching it skips the actionability checks
    await messageInput.dispatchEvent('keypress', { key: 'Enter', code: 'Enter', bubbles: true, cancelable: true });
    await page.waitForTimeout(2000);
}

async function openOwnAccountEditor(page: Page) {
    const modal = page.locator('.own-account-editor-modal');
    for (let attempt = 0; attempt < 3; attempt++) {
        await page.goto(`${BASE_URL}/settings/account`, { waitUntil: 'domcontentloaded' });
        await waitForAppReady(page).catch(() => { /* ignore */ });
        await skipOnboarding(page);
        const tile = page.locator('.your-account-tile .first-tile-item').first();
        const isShown = await tile.waitFor({ state: 'visible', timeout: 15_000 }).then(() => true, () => false);
        if (!isShown)
            continue;

        await tile.click({ force: true });
        const isOpened = await modal.waitFor({ state: 'visible', timeout: 10_000 }).then(() => true, () => false);
        if (isOpened)
            break;
    }
    await modal.waitFor({ state: 'visible', timeout: 5_000 });
    return modal;
}

describe('email localization', () => {
    let conn: BrowserConnection;
    const english = loadStrings(ENGLISH.subtag);

    beforeAll(async () => {
        smtp4DevUrl = await findSmtp4DevUrl() ?? '';
        if (!smtp4DevUrl) {
            console.log('smtp4dev is not reachable - skipping the email localization test');
            return;
        }

        conn = await connectBrowser();
    });

    afterAll(async () => {
        if (smtp4DevUrl && conn.ownsBrowser)
            await conn.browser.close();
    });

    describe.each(LANGUAGE_CODES)('a guest whose browser is in %s', code => {
        const language = UI_LANGUAGES.find(x => x.code === code)!;
        const strings = loadStrings(language.subtag);
        let context: BrowserContext | undefined;

        afterAll(async () => {
            await context?.close().catch(() => { /* ignore */ });
        });

        it('gets the sign-in code in that language', async ({ skip }) => {
            if (!smtp4DevUrl) {
                skip();
                return;
            }

            // arrange
            const email = `l10n-code-${language.subtag}-${Date.now()}@example.com`;
            context = await conn.browser.newContext({ ignoreHTTPSErrors: true, locale: code });
            const page = await context.newPage();
            await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
            // The app sets <html lang> once it has resolved the language it stores for the guest
            await page.waitForFunction(
                expected => document.documentElement.lang.startsWith(expected),
                language.subtag,
                { timeout: 60_000 });
            const since = new Date();

            // act
            await requestEmailCode(page, email);

            // assert
            const mail = await waitForMail(email, since, 20_000);
            const text = await readMail(context, mail, 'signin-code', language.subtag);
            expect(mail.subject).toBe(strings['EmailCode_SignInSubject_Format'].replace('{0}', APP_NAME));
            expectCodeMail(text, language.subtag, strings, english);
        }, 120_000);
    });

    describe('an account', () => {
        const accountEmail = `test-claude-agent-l10n-${Date.now().toString(36)}@actual.chat`;
        // The digest goes to the account's email, which asking for a verification code changes
        let currentEmail = accountEmail;

        let writer: { context: BrowserContext; page: Page } | undefined;

        beforeAll(async () => {
            if (!smtp4DevUrl)
                return;

            // A digest lists the chats with entries past the read position, so the account
            // has to have been in the chat once
            const reader = await newUserContext(conn, accountEmail);
            await openChat(reader.page);
            await reader.page.waitForTimeout(3_000);
            // Leave the chat before closing: a server-side circuit outlives its page for a while,
            // and a chat view still mounted in it would keep reading what is posted next
            await reader.page.goto(`${BASE_URL}/settings`, { waitUntil: 'domcontentloaded' });
            await waitForAppReady(reader.page);
            await reader.context.close();

            writer = await newUserContext(conn, TEST_EMAIL_2);
        }, 240_000);

        afterAll(async () => {
            await writer?.context.close().catch(() => { /* ignore */ });
        });

        describe.each(LANGUAGE_CODES)('with the UI in %s', code => {
            const language = UI_LANGUAGES.find(x => x.code === code)!;
            const strings = loadStrings(language.subtag);
            let context: BrowserContext | undefined;
            let page: Page | undefined;

            beforeAll(async () => {
                if (!smtp4DevUrl)
                    return;

                if (!writer)
                    throw new Error('The writer context is missing');

                // Signing in opens the last chat and reads it, so the unread message is posted
                // only after the account has moved to a page without a chat view
                ({ context, page } = await newUserContext(conn, accountEmail));
                await setUILanguage(page, code);
                await page.goto(`${BASE_URL}/test/email-templates`, { waitUntil: 'domcontentloaded' });
                await page.locator('button:has-text("Send Digest")').first()
                    .waitFor({ state: 'visible', timeout: 30_000 });
                await openChat(writer.page);
                await postMessage(writer.page, `Email localization check at ${new Date().toISOString()}`);
            }, 240_000);

            afterAll(async () => {
                await context?.close().catch(() => { /* ignore */ });
            });

            it('gets the digest in that language', async ({ skip }) => {
                if (!smtp4DevUrl || !context || !page) {
                    skip();
                    return;
                }

                // arrange
                const sendButton = page.locator('button:has-text("Send Digest")').first();
                const since = new Date();

                // act
                await sendButton.click();

                // assert
                const mail = await waitForMail(currentEmail, since, 60_000,
                    'A digest needs an unread chat and a working summarizer (CoreSettings__OpenAIKey).');
                const text = await readMail(context, mail, 'digest', language.subtag);
                expect(mail.subject).toBe(strings['EmailDigest_Subject_Format'].replace('{0}', APP_NAME));
                expectLocalizedChrome(text, language.subtag, strings);
                expect(text).toContain(strings['EmailDigest_Reason_Format'].replace('{0}', APP_NAME));
                expect(text).toContain(strings['EmailDigest_TurnOff']);
                expect(text).not.toContain(english['EmailDigest_TurnOff']);
            }, 180_000);

            it('gets the email verification code in that language', async ({ skip }) => {
                if (!smtp4DevUrl || !context || !page) {
                    skip();
                    return;
                }

                // arrange
                const email = `l10n-verify-${language.subtag}-${Date.now()}@example.com`;
                const modal = await openOwnAccountEditor(page);
                const emailInput = modal.locator('section.form-section[data-control-id$="-Email"] input').first();
                await emailInput.click();
                await emailInput.selectText();
                // TextBox debounces its input listener, and Tab fires Blazor's @onchange
                await emailInput.pressSequentially(email, { delay: 20 });
                await emailInput.press('Tab');
                const verifyButton = modal.locator('.phone-verifier .c-verify-btn').first();
                await verifyButton.waitFor({ state: 'visible', timeout: 15_000 });
                const since = new Date();

                // act
                await verifyButton.click();

                // assert
                const mail = await waitForMail(email, since, 30_000);
                currentEmail = email;
                const text = await readMail(context, mail, 'verify-code', language.subtag);
                expect(mail.subject).toBe(strings['EmailCode_VerifySubject_Format'].replace('{0}', APP_NAME));
                expectCodeMail(text, language.subtag, strings, english);
            }, 120_000);
        });
    });
});
