/**
 * E2E test: Verify keyboard arrow navigation in the visual media viewer —
 * ArrowRight advances to the next media item, ArrowLeft goes back.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/visual-media-viewer-arrows.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, beforeAll, afterAll, expect } from 'vitest';
import type { Page } from 'playwright';
import * as fs from 'fs';
import * as path from 'path';
import {
    BASE_URL, connectBrowser, ensureSignedIn, skipOnboarding, screenshot,
    waitForChatReady, waitForEditor, type BrowserConnection,
} from './helpers';

// Real 32x32 JPEG from the .NET test fixtures — small but valid enough that
// the server-side ImageUploadProcessor classifies it as visual media.
const BASE_IMAGE_PATH = path.resolve(
    process.cwd(),
    'tests/Testing/TestImages/default.jpg',
);

const shot = (name: string) => screenshot('e2e', name);

const CHAT_URL = `${BASE_URL}/chat/the-actual-one`;

// Reads the main media Swiper's activeIndex directly off the web component.
async function activeIndex(page: Page): Promise<number> {
    return page.evaluate(() => {
        /* eslint-disable @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-member-access, @typescript-eslint/no-unsafe-assignment, @typescript-eslint/no-unnecessary-type-assertion */
        const el = document.querySelector('.media-swiper') as any;
        return (el?.swiper?.activeIndex ?? -1) as number;
        /* eslint-enable @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-member-access, @typescript-eslint/no-unsafe-assignment, @typescript-eslint/no-unnecessary-type-assertion */
    });
}

async function waitForActiveIndex(page: Page, index: number): Promise<void> {
    await page.waitForFunction((expected: number) => {
        /* eslint-disable @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-member-access, @typescript-eslint/no-unsafe-assignment, @typescript-eslint/no-unnecessary-type-assertion */
        const el = document.querySelector('.media-swiper') as any;
        return (el?.swiper?.activeIndex ?? -1) === expected;
        /* eslint-enable @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-member-access, @typescript-eslint/no-unsafe-assignment, @typescript-eslint/no-unnecessary-type-assertion */
    }, index, { timeout: 5_000 });
}

describe('visual media viewer arrow navigation', () => {
    let conn: BrowserConnection;
    let page: Page;
    const tag = `arrows-${Date.now().toString(36)}`;
    const imagePaths: string[] = [];

    beforeAll(async () => {
        conn = await connectBrowser();
        page = await conn.context.newPage();
        await ensureSignedIn(page);

        // Three byte-distinct JPEGs: default.jpg plus a unique comment appended
        // after the EOI marker (decoders ignore trailing bytes, but the content
        // hash differs, so each becomes its own media item / slide).
        const base = fs.readFileSync(BASE_IMAGE_PATH);
        for (let i = 0; i < 3; i++) {
            const buf = Buffer.concat([base, Buffer.from(`\n<!--${tag}-${i}-->`)]);
            const p = path.join(process.cwd(), 'tmp', `${tag}-${i}.jpg`);
            fs.writeFileSync(p, buf);
            imagePaths.push(p);
        }
    }, 120_000);

    afterAll(async () => {
        for (const p of imagePaths)
            fs.rmSync(p, { force: true });
        await page.close();
        if (conn.ownsBrowser) {
            await conn.context.close();
            await conn.browser.close();
        }
    });

    it('switches between media items with Left/Right arrows', async () => {
        // arrange — open a chat we can post in
        await page.goto(CHAT_URL, { waitUntil: 'domcontentloaded' });
        await waitForChatReady(page);
        await skipOnboarding(page);

        const joinButton = page.locator('button:has-text("Join this chat")');
        if (await joinButton.isVisible({ timeout: 3000 }).catch(() => false)) {
            await joinButton.click();
            await page.waitForTimeout(1500);
        }

        await waitForEditor(page);

        // act — attach three images and post a message with a unique marker tag
        const fileInput = page.locator('input.attachment-web-file-picker').first();
        await fileInput.waitFor({ state: 'attached', timeout: 5_000 });
        await fileInput.setInputFiles(imagePaths);

        // The attachment-list-wrapper renders only when AttachmentList has items;
        // wait for all three previews so we know the uploads were accepted.
        await page.locator('.attachment-list-wrapper').first()
            .waitFor({ state: 'visible', timeout: 15_000 });
        await page.waitForTimeout(2_000);

        const messageInput = page.locator('#message-input .editor-content[contenteditable="true"]').first();
        await messageInput.click({ force: true });
        await messageInput.evaluate(el => {
            el.innerHTML = '';
            el.dispatchEvent(new Event('input', { bubbles: true }));
        });
        await messageInput.click({ force: true });
        await page.keyboard.type(tag);
        await page.screenshot({ path: shot('arrows-before-post') });

        await messageInput.dispatchEvent('keypress', {
            key: 'Enter', code: 'Enter', bubbles: true, cancelable: true,
        });

        // The chat-view unmounts briefly after a post; re-navigate to re-render it.
        await page.waitForTimeout(2_000);
        await page.goto(CHAT_URL, { waitUntil: 'domcontentloaded' });
        await waitForChatReady(page);
        await skipOnboarding(page);

        const postedEntry = page
            .locator(`[data-chat-entry-id]:has(.chat-message-markup:has-text("${tag}"))`)
            .first();
        await postedEntry.waitFor({ state: 'visible', timeout: 30_000 });
        await page.screenshot({ path: shot('arrows-posted') });

        // act — open the viewer by clicking the first attachment tile (opens at index 0)
        const firstTile = postedEntry.locator('.image-attachment .c-content').first();
        await firstTile.waitFor({ state: 'visible', timeout: 15_000 });
        await firstTile.click();

        const viewerHeader = page.locator('.image-viewer-header').first();
        await viewerHeader.waitFor({ state: 'visible', timeout: 10_000 });

        // Wait for the Swiper web component to finish initializing with all slides.
        await page.waitForFunction(() => {
            /* eslint-disable @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-member-access, @typescript-eslint/no-unsafe-assignment, @typescript-eslint/no-unnecessary-type-assertion */
            const el = document.querySelector('.media-swiper') as any;
            return (el?.swiper?.slides?.length ?? 0) >= 3;
            /* eslint-enable @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-member-access, @typescript-eslint/no-unsafe-assignment, @typescript-eslint/no-unnecessary-type-assertion */
        }, { timeout: 10_000 });
        await page.screenshot({ path: shot('arrows-viewer-open') });

        // assert — starts at the first slide
        expect(await activeIndex(page)).toBe(0);

        // act + assert — ArrowRight advances to the next slide
        await page.keyboard.press('ArrowRight');
        await waitForActiveIndex(page, 1);
        await page.screenshot({ path: shot('arrows-after-right') });
        expect(await activeIndex(page)).toBe(1);

        // act + assert — ArrowRight again advances to the third slide
        await page.keyboard.press('ArrowRight');
        await waitForActiveIndex(page, 2);
        expect(await activeIndex(page)).toBe(2);

        // act + assert — ArrowLeft goes back to the second slide
        await page.keyboard.press('ArrowLeft');
        await waitForActiveIndex(page, 1);
        await page.screenshot({ path: shot('arrows-after-left') });
        expect(await activeIndex(page)).toBe(1);
    }, 180_000);
});
