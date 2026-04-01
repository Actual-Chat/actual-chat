/**
 * E2E test: Upload an SVG file as avatar picture and verify it's converted to PNG.
 *
 * Prerequisites:
 * - Chrome running with remote debugging: `c chrome` (on Windows host)
 * - Server running (via `./run-watch.cmd` or `/server-start`)
 *
 * Run:
 *   npx vitest run tests/ts/e2e/svg-avatar-upload.test.ts --config tmp/vitest.e2e.config.ts
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

    beforeAll(async () => {
        svgFilePath = path.join(tmpDir, 'test-avatar.svg');
        fs.writeFileSync(svgFilePath, TEST_SVG);

        browser = await chromium.connectOverCDP(`http://${CHROME_HOST}:${CHROME_PORT}`);
        const contexts = browser.contexts();
        context = contexts.length > 0 ? contexts[0] : await browser.newContext();
        page = await context.newPage();
    }, 30_000);

    afterAll(async () => {
        await page?.close();
        if (fs.existsSync(svgFilePath))
            fs.unlinkSync(svgFilePath);
    });

    it('should sign in', async () => {
        await page.goto(BASE_URL, { waitUntil: 'domcontentloaded', timeout: 15000 });
        await page.waitForTimeout(5000);
        await dismissCookieConsent(page);

        if (!await isSignedIn(page)) {
            await signIn(page);
        }
        await skipOnboarding(page);
        await page.waitForTimeout(1000);

        const app = page.locator('.chat-list, .account-dropdown').first();
        await expect(app.isVisible({ timeout: 15000 })).resolves.toBe(true);
    }, 90_000);

    it('should upload SVG via API and receive converted PNG media', async () => {
        const svgContent = fs.readFileSync(svgFilePath, 'utf-8');

        // Upload SVG to the avatar picture endpoint (uses SvgToPngConverter server-side)
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

        console.log('Upload response:', result.status, result.body);
        expect(result.status).toBe(200);

        // Verify the response is a valid MediaRef with PNG blob path
        const mediaRef = JSON.parse(result.body);
        expect(mediaRef).toBeTruthy();
        // The BlobId should end with .png (converted from SVG)
        const blobId: string = mediaRef.BlobId ?? mediaRef.blobId ?? '';
        console.log('BlobId:', blobId);
        expect(blobId).toMatch(/\.png$/);
    }, 30_000);

    it('should upload SVG avatar through the settings UI', async () => {
        await page.goto(`${BASE_URL}/settings`, { waitUntil: 'domcontentloaded' });
        await page.waitForTimeout(3000);
        await skipOnboarding(page);

        // Wait for settings modal with "Your Account" tab
        const settingsModal = page.locator('.settings-modal');
        await settingsModal.waitFor({ state: 'visible', timeout: 10000 });

        const accountTab = page.locator('text=Your Account').first();
        if (await accountTab.isVisible({ timeout: 3000 }).catch(() => false)) {
            await accountTab.click();
            await page.waitForTimeout(1000);
        }
        await page.screenshot({ path: screenshot('svg-settings') });

        // Click the edit button in "My avatars" section (has star + edit buttons)
        const editButton = page.locator(':has(> button i.icon-star) > button:has(i.icon-edit)').first();
        await editButton.waitFor({ state: 'visible', timeout: 10000 });
        await editButton.click();
        await page.waitForTimeout(1000);

        // Find the file input in the avatar editor modal and upload SVG
        const fileInput = page.locator('.edit-avatar-modal input[type="file"]').first();
        await fileInput.waitFor({ state: 'attached', timeout: 5000 });
        await fileInput.setInputFiles(svgFilePath);
        await page.waitForTimeout(3000);
        await page.screenshot({ path: screenshot('svg-avatar-uploaded') });

        // Verify the uploaded image is visible in the editor
        const avatarImage = page.locator('.edit-avatar-modal .pic img, .edit-avatar-modal .c-top img').first();
        const hasImage = await avatarImage.isVisible({ timeout: 5000 }).catch(() => false);
        expect(hasImage).toBe(true);

        // Save
        const saveButton = page.locator('.edit-avatar-modal button:has-text("Save")').first();
        await saveButton.waitFor({ state: 'visible', timeout: 3000 });
        await saveButton.click();
        await page.waitForTimeout(2000);
        await page.screenshot({ path: screenshot('svg-avatar-saved') });
    }, 60_000);
});
