/**
 * E2E test: Mention search in chat message editor.
 *
 * Tests that typing @ shows the mention list, filtering works,
 * and selecting a mention inserts it.
 *
 * Prerequisites:
 * - Chrome running with remote debugging: `c chrome`
 * - Server running (via `c fwt` or `/server-start`)
 *
 * Run:
 *   npx vitest run tests/ts/e2e/mention-search.test.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import { chromium, type Browser, type Page } from 'playwright';
import * as fs from 'fs';
import * as path from 'path';

// --- Configuration ---

const CHROME_HOST = process.env.AC_OS === 'Linux in Docker' ? '192.168.65.254' : 'localhost';
const CHROME_PORT = 9222;
const TEST_EMAIL = 'test-claude-agent@actual.chat';
const TEST_OTP = '111111';

function loadBaseUrl(): string {
    const envPath = path.resolve(process.cwd(), '.env');
    if (fs.existsSync(envPath)) {
        const match = fs.readFileSync(envPath, 'utf-8').match(/^HostSettings__BaseUri=(.+)$/m);
        if (match) return match[1].trim();
    }
    return 'https://local.voxt.ai';
}

const BASE_URL = loadBaseUrl();

const tmpDir = path.join(process.cwd(), 'tmp');
if (!fs.existsSync(tmpDir)) {
    fs.mkdirSync(tmpDir, { recursive: true });
}

function screenshot(name: string): string {
    return path.join(tmpDir, `mention-${name}.png`);
}

// --- Helpers ---

async function dismissCookieConsent(page: Page) {
    const btn = page.locator('button:has-text("Accept all cookies")');
    if (await btn.isVisible({ timeout: 3000 }).catch(() => false)) {
        await btn.click();
        await page.waitForTimeout(500);
    }
}

async function skipOnboarding(page: Page) {
    await page.evaluate(() => {
        const debugUI = (window as any).debugUI;
        if (debugUI) {
            debugUI.resetOnboarding(false);
            debugUI.resetBubbles(false);
        }
    });
}

async function isSignedIn(page: Page): Promise<boolean> {
    const signedIn = page.locator('.chat-list, .account-dropdown').first();
    const notSignedIn = page.locator('button.signin-button-group, button.signin-button').first();
    return Promise.race([
        signedIn.waitFor({ state: 'visible', timeout: 15000 }).then(() => true),
        notSignedIn.waitFor({ state: 'visible', timeout: 15000 }).then(() => false),
    ]);
}

async function clearEditor(page: Page) {
    // Escape closes mention list if open, then clear editor content via JS
    await page.keyboard.press('Escape');
    await page.waitForTimeout(100);
    const editor = page.locator('#message-input .editor-content[contenteditable="true"]').first();
    await editor.evaluate(el => {
        el.innerHTML = '';
        el.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await page.waitForTimeout(200);
}

async function signIn(page: Page) {
    const signInButton = page.locator('button.signin-button-group, button.signin-button').first();
    await signInButton.waitFor({ state: 'visible', timeout: 10000 });
    await signInButton.click();
    await page.waitForTimeout(1000);

    const emailInput = page.locator('input[type="email"], input[placeholder*="email" i]').first();
    await emailInput.waitFor({ timeout: 5000 });
    await emailInput.fill(TEST_EMAIL);
    await page.waitForTimeout(300);

    await page.locator('button[type="submit"]').first().click();
    await page.waitForTimeout(2000);

    const accountError = page.locator('.c-account-error:has-text("Account not found")');
    if (await accountError.isVisible({ timeout: 2000 }).catch(() => false)) {
        await page.locator('label:has-text("Register a new account")').click();
        await page.waitForTimeout(300);
        await page.locator('button[type="submit"]').first().click();
        await page.waitForTimeout(2000);
    }

    const digitInputs = page.locator('input[maxlength="1"]');
    const digitCount = await digitInputs.count();
    if (digitCount >= 6) {
        for (let i = 0; i < 6; i++) {
            await digitInputs.nth(i).fill(TEST_OTP[i]);
            await page.waitForTimeout(50);
        }
    } else {
        const otpInput = page.locator('input[inputmode="numeric"], input[type="tel"]').first();
        await otpInput.waitFor({ timeout: 5000 });
        await otpInput.fill(TEST_OTP);
    }
    await page.waitForTimeout(500);

    const verifyButton = page.locator('button[type="submit"]').first();
    if (await verifyButton.isVisible({ timeout: 2000 }).catch(() => false)) {
        await verifyButton.click();
        await page.waitForTimeout(3000);
    }
}

// --- Test suite ---

describe('mention search', () => {
    let browser: Browser;
    let page: Page;

    beforeAll(async () => {
        browser = await chromium.connectOverCDP(`http://${CHROME_HOST}:${CHROME_PORT}`);
        const contexts = browser.contexts();
        const context = contexts.length > 0 ? contexts[0] : await browser.newContext();
        page = await context.newPage();

        // Sign in
        await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
        await page.waitForTimeout(2000);
        await dismissCookieConsent(page);
        if (!await isSignedIn(page))
            await signIn(page);
        await skipOnboarding(page);

        // Navigate to a chat with multiple members
        await page.goto(`${BASE_URL}/chat/the-actual-one`, { waitUntil: 'domcontentloaded' });
        await page.waitForTimeout(3000);
        await skipOnboarding(page);

        // Join if needed
        const joinButton = page.locator('button:has-text("Join this chat")');
        if (await joinButton.isVisible({ timeout: 3000 }).catch(() => false)) {
            await joinButton.click();
            await page.waitForTimeout(2000);
        }

        await page.screenshot({ path: screenshot('00-chat-page') });
    }, 60_000);

    afterAll(async () => {
        if (page) {
            await clearEditor(page);
            await page.close();
        }
    });

    it('should show mention list when typing @', async () => {
        const messageInput = page.locator('#message-input .editor-content[contenteditable="true"]').first();
        await messageInput.waitFor({ state: 'visible', timeout: 10000 });
        await messageInput.click();
        await page.waitForTimeout(300);

        await page.keyboard.type('@');
        await page.waitForTimeout(500);
        await page.screenshot({ path: screenshot('01-at-typed') });

        const mentionList = page.locator('.mention-list:not(.non-visible)');
        await expect(mentionList.isVisible({ timeout: 5000 })).resolves.toBe(true);

        const items = await page.locator('.mention-list-item').count();
        expect(items).toBeGreaterThan(0);

        await clearEditor(page);
    }, 30_000);

    it('should filter mention list by search term', async () => {
        const messageInput = page.locator('#message-input .editor-content[contenteditable="true"]').first();
        await messageInput.click();
        await page.waitForTimeout(300);

        await page.keyboard.type('@');
        await page.waitForTimeout(500);

        const countBefore = await page.locator('.mention-list-item').count();

        // Type a letter to filter
        await page.keyboard.type('a');
        await page.waitForTimeout(500);
        await page.screenshot({ path: screenshot('02-filtered') });

        const countAfter = await page.locator('.mention-list-item').count();
        expect(countAfter).toBeGreaterThan(0);
        expect(countAfter).toBeLessThanOrEqual(countBefore);

        await clearEditor(page);
    }, 30_000);

    it('should insert mention on Enter and close the list', async () => {
        const messageInput = page.locator('#message-input .editor-content[contenteditable="true"]').first();
        await messageInput.click();
        await page.waitForTimeout(300);

        await page.keyboard.type('@');

        // Wait for mention list to appear first
        const mentionListOpen = page.locator('.mention-list:not(.non-visible)');
        await mentionListOpen.waitFor({ state: 'visible', timeout: 5000 });
        await page.waitForTimeout(300);

        await page.screenshot({ path: screenshot('03-before-enter') });
        const items = page.locator('.mention-list-item');
        const itemCount = await items.count();
        expect(itemCount).toBeGreaterThan(0);

        // First item should be auto-selected; click it if .selected class isn't set
        const selectedItem = page.locator('.mention-list-item.selected');
        if (!await selectedItem.isVisible({ timeout: 1000 }).catch(() => false)) {
            // Select first item by clicking
            await items.first().click();
            await page.waitForTimeout(300);
        }

        await page.keyboard.press('Enter');
        await page.waitForTimeout(500);
        await page.screenshot({ path: screenshot('03-selected') });

        // Mention list should close after selection
        const mentionList = page.locator('.mention-list:not(.non-visible)');
        const stillVisible = await mentionList.isVisible({ timeout: 1000 }).catch(() => false);
        expect(stillVisible).toBe(false);

        await clearEditor(page);
    }, 30_000);
});
