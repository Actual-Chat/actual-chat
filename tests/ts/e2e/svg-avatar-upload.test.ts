/**
 * E2E test: Verify SVG avatar upload works across all UI paths.
 *
 * Prerequisites:
 * - Server running (via `./run-watch.cmd` or `/server-start`)
 * - Optionally, Chrome with remote debugging: `c chrome` (port 9222)
 *   If Chrome CDP is not available, Playwright launches its own headless Chromium.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/svg-avatar-upload.test.ts --config tmp/vitest.e2e.config.ts
 *
 * Tests:
 * 1. API endpoint: POST /api/avatars/upload-picture (direct upload)
 * 2. Settings UI: Your Account → My avatars → Edit avatar → Upload SVG
 * 3. New Chat: Create group chat with SVG picture
 * 4. Verify uploaded SVG media is stored as PNG
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

// Simple SVG test file — a colored circle with text
const TEST_SVG = `<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200" width="200" height="200">
  <circle cx="100" cy="100" r="90" fill="#4A90D9" stroke="#2C5F8A" stroke-width="4"/>
  <text x="100" y="115" text-anchor="middle" font-size="48" font-family="sans-serif" fill="white">SVG</text>
</svg>`;

// --- Helpers ---

/** Try connecting to host Chrome via CDP; fall back to launching headless Chromium. */
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

describe('SVG avatar upload', () => {
    let browser: Browser;
    let context: BrowserContext;
    let page: Page;
    let svgFilePath: string;
    let ownsBrowser: boolean;

    beforeAll(async () => {
        svgFilePath = path.join(tmpDir, 'test-avatar.svg');
        fs.writeFileSync(svgFilePath, TEST_SVG);

        ({ browser, context, ownsBrowser } = await connectOrLaunch());
        page = await context.newPage();

        // Sign in
        await page.goto(BASE_URL, { waitUntil: 'domcontentloaded', timeout: 15000 });
        await page.waitForTimeout(5000);
        await dismissCookieConsent(page);

        if (!await isSignedIn(page)) {
            await signIn(page);
        }
        await skipOnboarding(page);
        await page.waitForTimeout(1000);
        await page.screenshot({ path: screenshot('signed-in') });
        console.log('Signed in successfully');
    }, 90_000);

    afterAll(async () => {
        await page?.close();
        if (ownsBrowser) {
            await context?.close();
            await browser?.close();
        }
        if (fs.existsSync(svgFilePath))
            fs.unlinkSync(svgFilePath);
    });

    it('should upload SVG via API and receive converted PNG media', async () => {
        const svgContent = fs.readFileSync(svgFilePath, 'utf-8');

        const result = await page.evaluate(async (svg: string) => {
            const blob = new Blob([svg], { type: 'image/svg+xml' });
            const formData = new FormData();
            formData.append('file', blob, 'test-avatar.svg');

            const response = await fetch('/api/avatars/upload-picture', {
                method: 'POST',
                body: formData,
            });

            return {
                status: response.status,
                body: await response.text(),
            };
        }, svgContent);

        console.log('API upload response:', result.status, result.body);
        expect(result.status).toBe(200);

        const mediaRef = JSON.parse(result.body);
        expect(mediaRef).toBeTruthy();

        // BlobId should end with .png (SVG was converted)
        const blobId: string = mediaRef.BlobId ?? mediaRef.blobId ?? '';
        console.log('BlobId:', blobId);
        expect(blobId).toMatch(/\.png$/);
    }, 30_000);

    it('should upload SVG avatar through the settings UI', async () => {
        await page.goto(`${BASE_URL}/settings`, { waitUntil: 'domcontentloaded' });
        await page.waitForTimeout(3000);
        await skipOnboarding(page);

        // Wait for settings modal
        const settingsModal = page.locator('.settings-modal');
        await settingsModal.waitFor({ state: 'visible', timeout: 10000 });

        // Navigate to "Your Account" tab
        const accountTab = page.locator('text=Your Account').first();
        if (await accountTab.isVisible({ timeout: 3000 }).catch(() => false)) {
            await accountTab.click();
            await page.waitForTimeout(1000);
        }
        await page.screenshot({ path: screenshot('svg-settings') });

        // Click the edit (pencil) icon in the "My avatars" section.
        // The TileButtons in "My avatars" has both a star button and an edit button,
        // while the "Information" section only has an edit button.
        const avatarTileButtons = page.locator(':has(> button:has(i.icon-star)):has(> button:has(i.icon-edit))').first();
        const avatarEditBtn = avatarTileButtons.locator('button:has(i.icon-edit)');
        await avatarEditBtn.waitFor({ state: 'visible', timeout: 10000 });
        await avatarEditBtn.click();
        await page.waitForTimeout(1500);
        await page.screenshot({ path: screenshot('svg-avatar-editor-modal') });

        // The OwnAvatarEditorModal should be open with class "edit-avatar-modal".
        const avatarModal = page.locator('.edit-avatar-modal');
        await avatarModal.waitFor({ state: 'visible', timeout: 5000 });

        // The file input is inside PicUpload > FileUpload, hidden.
        const fileInput = avatarModal.locator('input[type="file"]').first();
        await fileInput.waitFor({ state: 'attached', timeout: 5000 });
        await fileInput.setInputFiles(svgFilePath);
        await page.waitForTimeout(3000);
        await page.screenshot({ path: screenshot('svg-avatar-uploaded') });

        // Verify the uploaded image is visible in the editor
        const avatarPic = avatarModal.locator('.pic img, .pic-upload img').first();
        const hasPic = await avatarPic.isVisible({ timeout: 5000 }).catch(() => false);
        expect(hasPic).toBe(true);

        // Save
        const saveButton = avatarModal.locator('button:has-text("Save")').first();
        await saveButton.waitFor({ state: 'visible', timeout: 3000 });
        await saveButton.click();
        await page.waitForTimeout(2000);
        await page.screenshot({ path: screenshot('svg-avatar-saved') });
    }, 60_000);

    it('should upload SVG picture in New Chat modal', async () => {
        await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
        await page.waitForTimeout(2000);
        await skipOnboarding(page);

        // Click the "+" button in the navbar to open the Create menu
        const plusBtn = page.locator('button:has(i.icon-plus)').first();
        await plusBtn.waitFor({ state: 'visible', timeout: 10000 });
        await plusBtn.click();
        await page.waitForTimeout(500);

        // Click "New Chat" from the CreateMenu
        const newChatEntry = page.locator('text=New Chat').first();
        await newChatEntry.waitFor({ state: 'visible', timeout: 5000 });
        await newChatEntry.click();
        await page.waitForTimeout(1500);
        await page.screenshot({ path: screenshot('svg-new-chat-modal') });

        // The NewChatModal has PicUpload with a hidden file input.
        const fileInput = page.locator('.pic-upload input[type="file"]').first();
        await fileInput.waitFor({ state: 'attached', timeout: 5000 });
        await fileInput.setInputFiles(svgFilePath);
        await page.waitForTimeout(3000);
        await page.screenshot({ path: screenshot('svg-new-chat-uploaded') });

        // Verify pic-upload shows an image
        const chatPic = page.locator('.pic-upload .pic img').first();
        const hasPic = await chatPic.isVisible({ timeout: 5000 }).catch(() => false);
        expect(hasPic).toBe(true);

        // Close modal
        await page.keyboard.press('Escape');
        await page.waitForTimeout(500);
    }, 60_000);

    it('should store uploaded SVG media as PNG with correct content type', async () => {
        const svgContent = fs.readFileSync(svgFilePath, 'utf-8');

        const result = await page.evaluate(async (svg: string) => {
            const blob = new Blob([svg], { type: 'image/svg+xml' });
            const formData = new FormData();
            formData.append('file', blob, 'verify-avatar.svg');

            const uploadResp = await fetch('/api/avatars/upload-picture', {
                method: 'POST',
                body: formData,
            });

            if (!uploadResp.ok) return { error: `Upload failed: ${uploadResp.status}` };

            const mediaRef = await uploadResp.json();
            const blobUrl = mediaRef.BlobId ?? mediaRef.blobId ?? '';

            if (blobUrl) {
                try {
                    const mediaResp = await fetch(`/api/blobs/${blobUrl}`, { method: 'HEAD' });
                    return {
                        blobId: blobUrl,
                        mediaStatus: mediaResp.status,
                        mediaContentType: mediaResp.headers.get('content-type'),
                    };
                } catch (e: any) {
                    return { blobId: blobUrl, fetchError: e.message };
                }
            }

            return { blobId: blobUrl };
        }, svgContent);

        console.log('Media verification:', JSON.stringify(result, null, 2));
        expect(result).not.toHaveProperty('error');
        if ('blobId' in result) {
            expect(result.blobId).toMatch(/\.png$/);
        }
        if ('mediaContentType' in result && result.mediaContentType) {
            expect(result.mediaContentType).toContain('image/png');
        }
    }, 30_000);
});
