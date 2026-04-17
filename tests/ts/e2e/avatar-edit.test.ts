/**
 * E2E test: Verify avatar editing works correctly.
 *
 * Covers the diff-based avatar update flow (fix for #3736):
 * - Editing avatar name and bio via the OwnAvatarEditorModal
 * - Creating a new avatar
 * - Verifying that edits persist after page reload
 *
 * Prerequisites:
 * - Server running (via `./run-watch.cmd` or `/server-start`)
 * - Optionally, Chrome with remote debugging: `c chrome` (port 9222)
 *   If Chrome CDP is not available, Playwright launches its own headless Chromium.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/avatar-edit.test.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import { chromium, type Browser, type BrowserContext, type Page } from 'playwright';
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
    return path.join(tmpDir, `e2e-${name}.png`);
}

// --- Helpers ---

async function connectOrLaunch(): Promise<{ browser: Browser; context: BrowserContext; ownsBrowser: boolean }> {
    try {
        const browser = await chromium.connectOverCDP(`http://${CHROME_HOST}:${CHROME_PORT}`);
        const contexts = browser.contexts();
        const context = contexts.length > 0 ? contexts[0] : await browser.newContext();
        console.log('Connected to host Chrome via CDP');
        return { browser, context, ownsBrowser: false };
    } catch {
        const browser = await chromium.launch({
            headless: true,
            args: ['--no-sandbox', '--disable-setuid-sandbox'],
        });
        const context = await browser.newContext({ ignoreHTTPSErrors: true });
        console.log('Launched headless Chromium (Chrome CDP not available)');
        return { browser, context, ownsBrowser: true };
    }
}

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

describe('avatar editing', () => {
    let browser: Browser;
    let context: BrowserContext;
    let page: Page;
    let ownsBrowser: boolean;
    const uniqueSuffix = Date.now().toString(36);

    beforeAll(async () => {
        ({ browser, context, ownsBrowser } = await connectOrLaunch());
        page = await context.newPage();

        // sign in
        await page.goto(BASE_URL, { waitUntil: 'domcontentloaded', timeout: 15000 });
        await page.waitForTimeout(5000);
        await dismissCookieConsent(page);

        if (!await isSignedIn(page)) {
            await signIn(page);
        }
        await skipOnboarding(page);
        await page.waitForTimeout(1000);
        console.log('Signed in successfully');
    }, 90_000);

    afterAll(async () => {
        await page?.close();
        if (ownsBrowser) {
            await context?.close();
            await browser?.close();
        }
    });

    it('should create a new avatar and set name and bio', async () => {
        // arrange
        await page.goto(`${BASE_URL}/settings`, { waitUntil: 'domcontentloaded' });
        await page.waitForTimeout(3000);
        await skipOnboarding(page);

        const settingsModal = page.locator('.settings-modal');
        await settingsModal.waitFor({ state: 'visible', timeout: 10000 });

        const accountTab = page.locator('text=Your Account').first();
        if (await accountTab.isVisible({ timeout: 3000 }).catch(() => false)) {
            await accountTab.click();
            await page.waitForTimeout(1000);
        }

        // act - click "+ Add avatar"
        const addBtn = page.locator('text=Add avatar').first();
        await addBtn.waitFor({ state: 'visible', timeout: 10000 });
        await page.screenshot({ path: screenshot('avatar-before-create') });
        await addBtn.click();
        await page.waitForTimeout(2000);

        // assert - editor modal opens
        const avatarModal = page.locator('.edit-avatar-modal');
        await avatarModal.waitFor({ state: 'visible', timeout: 5000 });
        await page.screenshot({ path: screenshot('avatar-created') });

        // act - set name and bio
        const nameInput = avatarModal.locator('input#avatar-editor-name, input[id*="name" i]').first();
        await nameInput.waitFor({ state: 'visible', timeout: 5000 });
        await nameInput.fill(`Avatar ${uniqueSuffix}`);
        await page.waitForTimeout(300);

        const bioInput = avatarModal.locator('textarea, input[id*="bio" i]').first();
        if (await bioInput.isVisible({ timeout: 3000 }).catch(() => false)) {
            await bioInput.fill(`Bio ${uniqueSuffix}`);
            await page.waitForTimeout(300);
        }

        // act - save
        const saveButton = avatarModal.locator('button:has-text("Save")').first();
        await saveButton.waitFor({ state: 'visible', timeout: 3000 });
        await saveButton.click();
        await page.waitForTimeout(2000);

        // assert - modal closed
        await expect(avatarModal.isVisible({ timeout: 3000 }).catch(() => false)).resolves.toBe(false);
        await page.screenshot({ path: screenshot('avatar-saved') });
    }, 60_000);

    it('should edit an existing avatar name', async () => {
        // arrange - navigate to settings and open avatar editor
        await page.goto(`${BASE_URL}/settings`, { waitUntil: 'domcontentloaded' });
        await page.waitForTimeout(3000);
        await skipOnboarding(page);

        const settingsModal = page.locator('.settings-modal');
        await settingsModal.waitFor({ state: 'visible', timeout: 10000 });

        const accountTab = page.locator('text=Your Account').first();
        if (await accountTab.isVisible({ timeout: 3000 }).catch(() => false)) {
            await accountTab.click();
            await page.waitForTimeout(1000);
        }

        // dismiss any tooltip bubbles
        const okBtn = page.locator('button:has-text("Ok")');
        if (await okBtn.isVisible({ timeout: 2000 }).catch(() => false)) {
            await okBtn.click();
            await page.waitForTimeout(500);
        }

        // act - open the avatar editor via the edit button on an avatar tile
        // Avatar tiles have both star and edit buttons; the Information section only has edit
        const avatarTile = page.locator(':has(> button:has(i.icon-star))').first();
        const avatarEditBtn = avatarTile.locator('button:has(i.icon-edit)');
        await avatarEditBtn.waitFor({ state: 'visible', timeout: 10000 });
        await page.screenshot({ path: screenshot('avatar-before-edit') });
        await avatarEditBtn.click();
        await page.waitForTimeout(1500);

        const avatarModal = page.locator('.edit-avatar-modal');
        await avatarModal.waitFor({ state: 'visible', timeout: 5000 });

        // act - change name
        const nameInput = avatarModal.locator('input#avatar-editor-name, input[id*="name" i]').first();
        await nameInput.waitFor({ state: 'visible', timeout: 5000 });
        const newName = `Edited ${uniqueSuffix}`;
        await nameInput.fill(newName);
        await page.waitForTimeout(300);
        await page.screenshot({ path: screenshot('avatar-editor-filled') });

        // act - save
        const saveButton = avatarModal.locator('button:has-text("Save")').first();
        await saveButton.click();
        await page.waitForTimeout(2000);

        // assert - re-open and verify name persisted
        await avatarEditBtn.waitFor({ state: 'visible', timeout: 10000 });
        await avatarEditBtn.click();
        await page.waitForTimeout(1500);
        await avatarModal.waitFor({ state: 'visible', timeout: 5000 });

        const savedName = await nameInput.inputValue();
        expect(savedName).toBe(newName);
        await page.screenshot({ path: screenshot('avatar-editor-verified') });

        // cleanup - close modal
        await page.keyboard.press('Escape');
        await page.waitForTimeout(500);
    }, 60_000);

    it('should persist avatar changes after page reload', async () => {
        // arrange - open editor and change name
        await page.goto(`${BASE_URL}/settings`, { waitUntil: 'domcontentloaded' });
        await page.waitForTimeout(3000);
        await skipOnboarding(page);

        const settingsModal = page.locator('.settings-modal');
        await settingsModal.waitFor({ state: 'visible', timeout: 10000 });

        const accountTab = page.locator('text=Your Account').first();
        if (await accountTab.isVisible({ timeout: 3000 }).catch(() => false)) {
            await accountTab.click();
            await page.waitForTimeout(1000);
        }

        // dismiss any tooltip bubbles
        const okBtn = page.locator('button:has-text("Ok")');
        if (await okBtn.isVisible({ timeout: 2000 }).catch(() => false)) {
            await okBtn.click();
            await page.waitForTimeout(500);
        }

        const avatarTile = page.locator(':has(> button:has(i.icon-star))').first();
        const avatarEditBtn = avatarTile.locator('button:has(i.icon-edit)');
        await avatarEditBtn.waitFor({ state: 'visible', timeout: 10000 });
        await avatarEditBtn.click();
        await page.waitForTimeout(1500);

        const avatarModal = page.locator('.edit-avatar-modal');
        await avatarModal.waitFor({ state: 'visible', timeout: 5000 });

        const nameInput = avatarModal.locator('input#avatar-editor-name, input[id*="name" i]').first();
        await nameInput.waitFor({ state: 'visible', timeout: 5000 });

        // act - set a unique name and save
        const persistName = `Persist ${uniqueSuffix}`;
        await nameInput.fill(persistName);
        await page.waitForTimeout(300);

        const saveButton = avatarModal.locator('button:has-text("Save")').first();
        await saveButton.click();
        await page.waitForTimeout(2000);

        // act - reload the page
        await page.goto(`${BASE_URL}/settings`, { waitUntil: 'domcontentloaded' });
        await page.waitForTimeout(3000);
        await skipOnboarding(page);
        await settingsModal.waitFor({ state: 'visible', timeout: 10000 });

        if (await accountTab.isVisible({ timeout: 3000 }).catch(() => false)) {
            await accountTab.click();
            await page.waitForTimeout(1000);
        }

        // dismiss tooltip again after reload
        if (await okBtn.isVisible({ timeout: 2000 }).catch(() => false)) {
            await okBtn.click();
            await page.waitForTimeout(500);
        }

        // assert - re-open editor and verify name survived reload
        await avatarEditBtn.waitFor({ state: 'visible', timeout: 10000 });
        await avatarEditBtn.click();
        await page.waitForTimeout(1500);
        await avatarModal.waitFor({ state: 'visible', timeout: 5000 });

        const reloadedName = await nameInput.inputValue();
        expect(reloadedName).toBe(persistName);
        await page.screenshot({ path: screenshot('avatar-persisted') });
    }, 60_000);
});
