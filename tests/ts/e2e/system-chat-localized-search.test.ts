/**
 * E2E test: the left search panel finds a system chat by its localized name (#4362).
 * With the UI in Russian, "Заметки" must find the Notes chat and "Анонсы" the announcements
 * chat, and each result must be shown under the Russian name - own groups are served from
 * LocalSearchUI, whose candidates carry the Chats.Get title.
 *
 * Run:
 *   ./node_modules/.bin/vitest run tests/ts/e2e/system-chat-localized-search.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { Page } from 'playwright';
import {
    BASE_URL, connectBrowser, ensureSignedIn, screenshot, setUILanguage,
    skipOnboarding, waitForAppReady, type BrowserConnection,
} from './helpers';

const RUSSIAN = 'ru-RU';
const shot = (name: string) => screenshot('system-chat-search', name);

async function clearSearch(page: Page) {
    const input = page.locator('.left-chat-search-input input[type="text"]').first();
    if (!await input.isVisible({ timeout: 1000 }).catch(() => false))
        return;

    await input.click({ force: true }).catch(() => { /* ignore */ });
    // fill('') alone doesn't fire Blazor's debounced input listener - use a real keystroke.
    await input.click({ clickCount: 3, force: true }).catch(() => { /* ignore */ });
    await page.keyboard.press('Backspace').catch(() => { /* ignore */ });
    await input.fill('').catch(() => { /* ignore */ });
    await page.waitForTimeout(800);
}

// LeftChatSearchInput resets the location filter to the current chat every time the panel
// opens. The location badge renders first, and its text is localized, so it's picked by position.
async function setLocationAnywhere(page: Page) {
    const badge = page.locator('.left-chat-search-input .search-filter-badge').first();
    if (await badge.isVisible({ timeout: 1500 }).catch(() => false)) {
        await badge.click({ force: true });
        await badge.waitFor({ state: 'hidden', timeout: 3_000 }).catch(() => { /* ignore */ });
    }
}

async function searchFor(page: Page, query: string) {
    await skipOnboarding(page);
    await clearSearch(page);
    const input = page.locator('.left-chat-search-input input[type="text"]').first();
    await input.waitFor({ state: 'visible', timeout: 10_000 });
    await input.click({ force: true });
    await page.waitForTimeout(200);
    await setLocationAnywhere(page);
    // pressSequentially: TextInput's input listener is debounced 800ms; fill() can land before it attaches.
    await input.pressSequentially(query, { delay: 60 });
    await page.waitForTimeout(2_500);
}

async function foundGroupTitles(page: Page): Promise<string[]> {
    await page.locator('.found-result.chat').first()
        .waitFor({ state: 'visible', timeout: 10_000 })
        .catch(() => { /* asserted by the caller */ });
    const titles = page.locator('.found-result.chat .c-title');
    const count = await titles.count();
    const result: string[] = [];
    for (let i = 0; i < count; i++)
        result.push(((await titles.nth(i).textContent({ timeout: 1000 }).catch(() => '')) ?? '').trim());
    return result;
}

describe('system chat localized search', () => {
    let conn: BrowserConnection;
    let page: Page;
    // '' is a real value here - the "Auto" option - so null is what marks "never switched".
    let originalLanguage: string | null = null;

    beforeAll(async () => {
        conn = await connectBrowser();
        page = await conn.context.newPage();
        await ensureSignedIn(page);
        originalLanguage = await setUILanguage(page, RUSSIAN);

        // The reload leaves the settings modal open; come back on a clean route.
        await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
        await waitForAppReady(page);
        await skipOnboarding(page);
        await page.locator('.left-chat-search-input input[type="text"]').first()
            .waitFor({ state: 'visible', timeout: 20_000 });
        await page.screenshot({ path: shot('00-ready-ru') });
    }, 120_000);

    afterAll(async () => {
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition -- page may be unset if beforeAll fails
        if (page) {
            await clearSearch(page);
            await page.keyboard.press('Escape').catch(() => { /* ignore */ });
            if (originalLanguage !== null) {
                try {
                    await setUILanguage(page, originalLanguage);
                } catch (e) {
                    console.log('Failed to restore UI language:', e instanceof Error ? e.message : String(e));
                }
            }
            await page.close();
        }
        if (conn.ownsBrowser) {
            await conn.context.close();
            await conn.browser.close();
        }
    });

    it.each([
        { query: 'Заметки', title: 'Заметки', name: 'notes' },
        { query: 'Анонсы', title: 'Анонсы Voxt', name: 'announcements' },
    ])('finds "$title" when searching for "$query"', async ({ query, title, name }) => {
        await searchFor(page, query);
        const titles = await foundGroupTitles(page);
        await page.screenshot({ path: shot(`01-${name}`) });

        expect(titles, `group results for "${query}"`).toContain(title);
    }, 45_000);

    it('does not match an own system chat by its English title', async () => {
        await searchFor(page, 'Notes');
        const titles = await foundGroupTitles(page);
        await page.screenshot({ path: shot('02-english-query') });

        // Deliberate: the local candidates carry the localized title only (docs/i18n.md).
        expect(titles).not.toContain('Заметки');
        expect(titles).not.toContain('Notes');
    }, 45_000);
});
