/**
 * E2E test: a link to a point on a map renders as a map card (#4145).
 *
 * Posts messages with Google Maps, Apple Maps and OpenStreetMap links that carry coordinates and
 * checks that each gets a map with a pin instead of the regular link preview card.
 *
 * Prerequisites:
 * - Server running (server-loop / run-watch), with internet access: the server crawls the links.
 * - A browser with WebGL: `ai chrome` (port 9222), or AC_E2E_BROWSER=headless AC_E2E_HEADED=1.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/map-link-preview.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll, afterEach } from 'vitest';
import type { Locator, Page } from 'playwright';
import {
    clearBrowserCache, connectBrowser, ensureSignedIn, openChat, screenshot, watchMapPaint,
    type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e', name);

// A preview is kept per URL, so every link is a new one on each run: the server then has to build its preview
const RUN_ID = Date.now();
const MAP_LINKS = [
    {
        name: 'google',
        url: `https://www.google.com/maps?q=48.858370,2.294481&t=${RUN_ID}`,
        caption: '48.85837, 2.294481',
        title: null,
    },
    {
        name: 'apple',
        url: `https://maps.apple.com/?ll=40.689247,-74.044502&q=Liberty+Island&t=${RUN_ID}`,
        caption: '40.689247, -74.044502',
        title: 'Liberty Island',
    },
    {
        name: 'osm',
        url: `https://www.openstreetmap.org/?mlat=51.500729&mlon=-0.124625&t=${RUN_ID}#map=15/51.500729/-0.124625`,
        caption: '51.500729, -0.124625',
        title: null,
    },
];

const MAP_SETTLE_MS = 6_000;

describe('map link preview', () => {
    let conn: BrowserConnection;
    let page: Page;
    let viewportToRestore: ReturnType<Page['viewportSize']> = null;

    beforeAll(async () => {
        conn = await connectBrowser();
        page = await conn.context.newPage();
        await clearBrowserCache(page);
        await ensureSignedIn(page);
    }, 120_000);

    // Not in the test's finally: the failure screenshot is taken before afterEach and must still see the narrow window
    afterEach(async () => {
        if (viewportToRestore)
            await page.setViewportSize(viewportToRestore);
        viewportToRestore = null;
    });

    afterAll(async () => {
        await page.close();
        if (conn.ownsBrowser) {
            await conn.context.close();
            await conn.browser.close();
        }
    });

    for (const link of MAP_LINKS) {
        it(`shows a map for a ${link.name} link`, async () => {
            // arrange
            await openChat(page);
            const mapPainted = watchMapPaint(page);
            const text = `Map link ${link.name} ${Date.now()}`;

            // act
            const message = await postMessage(page, `${text} ${link.url}`, text);

            // assert
            const card = message.locator('.map-link-preview').first();
            await card.waitFor({ state: 'visible', timeout: 30_000 });
            await card.locator('.maplibregl-marker').first().waitFor({ state: 'visible', timeout: 30_000 });
            await mapPainted();
            expect(await card.getAttribute('href')).toBe(link.url);
            expect((await card.locator('.c-caption').innerText()).trim()).toBe(link.caption);
            if (link.title != null)
                expect((await card.locator('.c-title').innerText()).trim()).toBe(link.title);
            expect(await message.locator('.link-preview').count()).toBe(0);

            // Tiles keep streaming in after the first one, so let the map settle for the picture
            await page.waitForTimeout(MAP_SETTLE_MS);
            await page.screenshot({ path: shot(`map-link-${link.name}`) });
            await card.screenshot({ path: shot(`map-link-${link.name}-card`) });
        }, 120_000);
    }

    it('zooms the map out when there is nothing to draw around the point', async () => {
        // arrange — taiga with no mapped features for kilometers around
        await openChat(page);
        const text = `Map link wilderness ${Date.now()}`;

        // act
        const url = `https://www.google.com/maps/search/62.485937,+42.318932?t=${RUN_ID}`;
        const message = await postMessage(page, `${text} ${url}`, text);

        // assert
        const card = message.locator('.map-link-preview').first();
        await card.waitFor({ state: 'visible', timeout: 30_000 });
        await card.locator('.maplibregl-marker').first().waitFor({ state: 'visible', timeout: 30_000 });
        await page.waitForTimeout(MAP_SETTLE_MS);
        await card.screenshot({ path: shot('map-link-wilderness-card') });
    }, 120_000);

    it('reloads the map when a request for it stalls', async () => {
        // arrange — a sprite request that never completes; MapLibre itself would wait for it forever
        await openChat(page);
        const spriteRe = /\/sprites\/.*\.png/;
        let isStalling = true;
        await page.route(spriteRe, route => isStalling ? undefined : route.continue());
        const text = `Map link stalled ${Date.now()}`;

        try {
            // act
            const message = await postMessage(page, `${text} ${MAP_LINKS[0].url}-stalled`, text);
            const card = message.locator('.map-link-preview').first();
            await card.locator('.maplibregl-marker').first().waitFor({ state: 'visible', timeout: 30_000 });
            const spriteLoaded = page.waitForResponse(r => spriteRe.test(r.url()) && r.ok(), { timeout: 30_000 });
            isStalling = false;

            // assert — only a rebuilt map asks for the sprite again
            await spriteLoaded;
            await page.waitForTimeout(MAP_SETTLE_MS);
            await card.screenshot({ path: shot('map-link-stalled-card') });
        } finally {
            await page.unroute(spriteRe);
        }
    }, 120_000);

    it('keeps the regular card for a link without a point', async () => {
        // arrange
        await openChat(page);
        const text = `Plain link ${Date.now()}`;

        // act
        const message = await postMessage(page, `${text} https://github.com/maplibre/maplibre-gl-js`, text);

        // assert
        await message.locator('.link-preview').first().waitFor({ state: 'visible', timeout: 30_000 });
        expect(await message.locator('.map-link-preview').count()).toBe(0);
        await page.screenshot({ path: shot('map-link-plain') });
    }, 120_000);

    it('fits the map card into a narrow screen', async () => {
        // arrange
        viewportToRestore = page.viewportSize();
        await page.setViewportSize({ width: 390, height: 844 });
        const mapPainted = watchMapPaint(page);

        // act
        await openChat(page);

        // assert
        const card = page.locator('.map-link-preview').last();
        await card.waitFor({ state: 'visible', timeout: 30_000 });
        // The list re-lays out after the resize, so the card can drop out between the wait and the read
        await expect.poll(
            async () => (await card.boundingBox())?.width ?? 0,
            { timeout: 15_000 },
        ).toBeGreaterThan(0);
        const box = (await card.boundingBox())!;
        expect(box.x + box.width).toBeLessThanOrEqual(390);

        await mapPainted();
        await page.screenshot({ path: shot('map-link-narrow') });
    }, 120_000);
});

async function postMessage(page: Page, content: string, marker: string): Promise<Locator> {
    const editor = page.locator('#message-input .editor-content[contenteditable="true"]').first();
    await editor.click({ force: true });
    // ChatMessageEditor restores per-chat drafts, so a previous run's unsent text would be prepended
    await editor.evaluate(el => {
        el.innerHTML = '';
        el.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await editor.click({ force: true });
    await page.keyboard.type(content);
    // MarkupEditor posts on 'keypress' for Enter; dispatching it skips the actionability checks
    await editor.dispatchEvent('keypress', { key: 'Enter', code: 'Enter', bubbles: true, cancelable: true });

    const message = page.locator(`.message-wrapper:has(.chat-message-markup:has-text("${marker}"))`).first();
    await message.waitFor({ state: 'visible', timeout: 30_000 });
    return message;
}
