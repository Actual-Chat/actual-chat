/**
 * E2E test: Global people search in the left search panel.
 * Regression for SearchBackend's `MustNot HasChild MatchAll` filter — that bug
 * stripped from the Global subgroup any user who was anyone's contact.
 *
 * Run:
 *   ./node_modules/.bin/vitest run tests/ts/e2e/people-search.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { Page } from 'playwright';
import {
    BASE_URL, connectBrowser, ensureSignedIn, skipOnboarding,
    screenshot, type BrowserConnection,
} from './helpers';

async function clearSearch(page: Page) {
    const input = page.locator('.left-chat-search-input input[type="text"]').first();
    if (!await input.isVisible({ timeout: 1000 }).catch(() => false)) return;
    await input.click({ force: true }).catch(() => { /* ignore */ });
    // fill('') alone doesn't fire Blazor's debounced input listener — use a real keystroke.
    await input.click({ clickCount: 3, force: true }).catch(() => { /* ignore */ });
    await page.keyboard.press('Backspace').catch(() => { /* ignore */ });
    await input.fill('').catch(() => { /* ignore */ });
    await page.waitForTimeout(800);
}

async function searchFor(page: Page, query: string) {
    await skipOnboarding(page);
    await clearSearch(page);
    const input = page.locator('.left-chat-search-input input[type="text"]').first();
    await input.waitFor({ state: 'visible', timeout: 10_000 });
    await input.click({ force: true });
    await page.waitForTimeout(200);
    // pressSequentially: TextInput's input listener is debounced 800ms; fill() can land before it attaches.
    await input.pressSequentially(query, { delay: 60 });
    await page.waitForTimeout(2_500);
}

async function hasAnyPersonResult(page: Page): Promise<boolean> {
    const items = page.locator('.found-result.contact');
    return await items.first().isVisible({ timeout: 5_000 }).catch(() => false);
}

async function findPersonResultContaining(page: Page, needle: string): Promise<boolean> {
    const lc = needle.toLowerCase();
    await page.locator('.found-result.contact, .search-result-group-header').first()
        .waitFor({ state: 'visible', timeout: 10_000 })
        .catch(() => { /* ignore */ });

    const items = page.locator('.found-result.contact');
    const count = await items.count();
    for (let i = 0; i < count; i++) {
        const text = (await items.nth(i).textContent({ timeout: 1000 }).catch(() => '')) ?? '';
        if (text.toLowerCase().includes(lc)) return true;
    }
    return false;
}

async function expandGlobalIfPossible(page: Page) {
    const showMore = page.locator(
        '.search-result-group-header:has-text("Global search") button:has-text("Show more")',
    ).first();
    if (await showMore.isVisible({ timeout: 1000 }).catch(() => false)) {
        await showMore.click({ force: true });
        await page.waitForTimeout(1_500);
    }
}

describe('global people search', () => {
    let conn: BrowserConnection;
    let page: Page;

    beforeAll(async () => {
        conn = await connectBrowser();
        page = await conn.context.newPage();
        await ensureSignedIn(page);
        await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
        await skipOnboarding(page);
        await page.locator('.left-chat-search-input input[type="text"]').first()
            .waitFor({ state: 'visible', timeout: 20_000 });
        await page.screenshot({ path: screenshot('people-search', '00-ready') });
    }, 90_000);

    afterAll(async () => {
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition -- page may be unset if beforeAll fails
        if (page) {
            await clearSearch(page);
            await page.close();
        }
        if (conn.ownsBrowser) {
            await conn.context.close();
            await conn.browser.close();
        }
    });

    it.each([
        { query: 'rumi',     needle: 'rumi' },
        { query: 'einstein', needle: 'einstein' },
    ])('finds "$needle" when searching for "$query"', async ({ query, needle }) => {
        await clearSearch(page);
        await searchFor(page, query);
        await page.screenshot({ path: screenshot('people-search', `01-${query}-default`) });

        let found = await findPersonResultContaining(page, needle);
        if (!found) {
            await expandGlobalIfPossible(page);
            await page.screenshot({ path: screenshot('people-search', `02-${query}-expanded`) });
            found = await findPersonResultContaining(page, needle);
        }

        if (!found) {
            const visible = await hasAnyPersonResult(page);
            const groupHeader = await page.locator('.search-result-group-header').count();
            throw new Error(
                `No person result containing "${needle}" after searching "${query}". ` +
                `Any person results visible: ${visible}. Group headers rendered: ${groupHeader}.`,
            );
        }

        expect(found).toBe(true);
    }, 45_000);
});
