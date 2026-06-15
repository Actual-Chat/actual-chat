/**
 * E2E test: Verify SVG avatar upload works across all UI paths.
 *
 * Prerequisites:
 * - Server running (or AC_E2E_SERVER=managed)
 * - Optionally, Chrome with remote debugging: `c chrome` (port 9222)
 *
 * Run:
 *   npx vitest run tests/ts/e2e/svg-avatar-upload.test.ts --config vitest.config.e2e.ts
 *
 * Tests:
 * 1. API endpoint: POST /api/avatars/upload-picture (direct upload)
 * 2. Settings UI: Your Account → My avatars → Edit avatar → Upload SVG
 * 3. New Chat: Create group chat with SVG picture
 * 4. Verify uploaded SVG media is stored as PNG
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { Page } from 'playwright';
import * as fs from 'fs';
import * as path from 'path';
import {
    BASE_URL, connectBrowser, ensureSignedIn, skipOnboarding,
    screenshot, waitForAppReady, type BrowserConnection,
} from './helpers';

// Simple SVG test file — a colored circle with text
const TEST_SVG = `<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200" width="200" height="200">
  <circle cx="100" cy="100" r="90" fill="#4A90D9" stroke="#2C5F8A" stroke-width="4"/>
  <text x="100" y="115" text-anchor="middle" font-size="48" font-family="sans-serif" fill="white">SVG</text>
</svg>`;

const tmpDir = path.join(process.cwd(), 'tmp');

describe('SVG avatar upload', () => {
    let conn: BrowserConnection;
    let page: Page;
    let svgFilePath: string;

    beforeAll(async () => {
        svgFilePath = path.join(tmpDir, 'test-avatar.svg');
        if (!fs.existsSync(tmpDir)) fs.mkdirSync(tmpDir, { recursive: true });
        fs.writeFileSync(svgFilePath, TEST_SVG);

        conn = await connectBrowser();
        page = await conn.context.newPage();

        await ensureSignedIn(page);
        await page.waitForTimeout(1000);
        await page.screenshot({ path: screenshot('e2e', 'signed-in') });
        console.log('Signed in successfully');
    }, 90_000);

    afterAll(async () => {
        await page.close();
        if (conn.ownsBrowser) {
            await conn.context.close();
            await conn.browser.close();
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

        const mediaRef = JSON.parse(result.body) as Record<string, unknown>;
        expect(mediaRef).toBeTruthy();

        // BlobId should end with .png (SVG was converted)
        const blobId = (mediaRef.BlobId ?? mediaRef.blobId ?? '') as string;
        console.log('BlobId:', blobId);
        expect(blobId).toMatch(/\.png$/);
    }, 30_000);

    it('should upload SVG avatar through the settings UI and convert to PNG', async () => {
        await page.goto(`${BASE_URL}/settings`, { waitUntil: 'domcontentloaded' });
        await waitForAppReady(page);
        await skipOnboarding(page);

        // Wait for settings modal
        const settingsModal = page.locator('.settings-modal');
        await settingsModal.waitFor({ state: 'visible', timeout: 15000 });

        // Navigate to "Your Account" tab
        const accountTab = page.locator('text=Your Account').first();
        if (await accountTab.isVisible({ timeout: 3000 }).catch(() => false)) {
            await accountTab.click();
            await page.waitForTimeout(1000);
        }
        await page.screenshot({ path: screenshot('e2e', 'svg-settings') });

        // Click the edit (pencil) icon in the "My avatars" section.
        const avatarTileButtons = page.locator(':has(> button:has(i.icon-star)):has(> button:has(i.icon-edit))').first();
        const avatarEditBtn = avatarTileButtons.locator('button:has(i.icon-edit)');
        await avatarEditBtn.waitFor({ state: 'visible', timeout: 10000 });
        await avatarEditBtn.click();
        await page.waitForTimeout(1500);
        await page.screenshot({ path: screenshot('e2e', 'svg-avatar-editor-modal') });

        // The OwnAvatarEditorModal should be open with class "edit-avatar-modal".
        const avatarModal = page.locator('.edit-avatar-modal');
        await avatarModal.waitFor({ state: 'visible', timeout: 5000 });

        // AvatarPicFormBlock now uses ImageCropPicker, whose <input type="file"> is
        // attached to the document root (not inside the avatar modal). Target it
        // globally — the modal currently has only one file input in the DOM tree.
        const fileInput = page.locator('input[type="file"]').first();
        await fileInput.waitFor({ state: 'attached', timeout: 5000 });
        await fileInput.setInputFiles(svgFilePath);

        // After file selection, ImageCropPicker opens PicCropModal on top of the
        // avatar editor. Confirm the crop to trigger server-side conversion + upload.
        const cropModal = page.locator('.pic-crop-modal');
        await cropModal.waitFor({ state: 'visible', timeout: 10_000 });
        const confirmBtn = cropModal.locator('.btn-confirm').first();
        await confirmBtn.waitFor({ state: 'visible', timeout: 5_000 });
        await confirmBtn.click();
        await cropModal.waitFor({ state: 'hidden', timeout: 15_000 });
        await page.screenshot({ path: screenshot('e2e', 'svg-avatar-uploaded') });

        // Verify the preview image is visible and is served as PNG.
        // Pic renders via <image-skeleton> (LitElement, light DOM) containing <img>.
        const avatarPic = avatarModal.locator('.pic img').first();
        await avatarPic.waitFor({ state: 'visible', timeout: 10_000 });

        // src starts as `blob:` (just-selected SVG) and swaps to the server `.png` URL
        // once upload+conversion finishes. Poll instead of reading once.
        await expect.poll(
            async () => await avatarPic.getAttribute('src') ?? '',
            { timeout: 30_000, interval: 250 },
        ).toMatch(/\.png/);

        // Save
        const saveButton = avatarModal.locator('button:has-text("Save")').first();
        await saveButton.waitFor({ state: 'visible', timeout: 3000 });
        await saveButton.click();
        await page.waitForTimeout(2000);
        await page.screenshot({ path: screenshot('e2e', 'svg-avatar-saved') });
    }, 60_000);

    it('should upload SVG picture in New Chat modal and convert to PNG', async () => {
        await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
        await waitForAppReady(page);
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
        await page.screenshot({ path: screenshot('e2e', 'svg-new-chat-modal') });

        // The NewChatModal has PicUpload with a hidden file input.
        const fileInput = page.locator('.pic-upload input[type="file"]').first();
        await fileInput.waitFor({ state: 'attached', timeout: 5000 });
        await fileInput.setInputFiles(svgFilePath);
        await page.waitForTimeout(3000);
        await page.screenshot({ path: screenshot('e2e', 'svg-new-chat-uploaded') });

        // Verify pic-upload shows an image that is PNG (converted from SVG)
        const chatPic = page.locator('.pic-upload .pic img').first();
        const hasPic = await chatPic.isVisible({ timeout: 5000 }).catch(() => false);
        expect(hasPic).toBe(true);

        await expect.poll(
            async () => await chatPic.getAttribute('src') ?? '',
            { timeout: 30_000, interval: 250 },
        ).toMatch(/\.png/);

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

            const mediaRef = (await uploadResp.json()) as Record<string, unknown>;
            const blobUrl = (mediaRef.BlobId ?? mediaRef.blobId ?? '') as string;

            if (blobUrl) {
                try {
                    const mediaResp = await fetch(`/api/blobs/${blobUrl}`, { method: 'HEAD' });
                    return {
                        blobId: blobUrl,
                        mediaStatus: mediaResp.status,
                        mediaContentType: mediaResp.headers.get('content-type'),
                    };
                } catch (e: unknown) {
                    return { blobId: blobUrl, fetchError: e instanceof Error ? e.message : String(e) };
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
