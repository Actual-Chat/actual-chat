/**
 * E2E test: the X of the left-panel search field closes an empty search, clears a filled one,
 * and survives a press that blurs the input without focusing the button - what WebKit on macOS
 * does, which used to close the search under the cursor and reopen it on mouseup. #4516
 *
 * Run:
 *   npx vitest run tests/ts/e2e/search-close-button.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll, beforeEach } from 'vitest';
import type { Page } from 'playwright';
import {
    BASE_URL, connectBrowser, ensureSignedIn, skipOnboarding,
    screenshot, type BrowserConnection,
} from './helpers';

const searchRoot = (page: Page) => page.locator('.left-chat-search-input').first();
const searchInput = (page: Page) => page.locator('.left-chat-search-input input[type="text"]').first();
const closeButton = (page: Page) => page.locator('.left-chat-search-input .c-close-btn').first();

async function searchState(page: Page) {
    return searchRoot(page).evaluate(root => {
        const input = root.querySelector('input')!;
        return {
            isOpen: root.classList.contains('open'),
            isFocused: document.activeElement === input,
            text: input.value,
        };
    });
}

async function openSearch(page: Page) {
    await searchInput(page).click();
    await expect.poll(async () => (await searchState(page)).isOpen, { timeout: 5_000 }).toBe(true);
}

async function closeSearchIfOpen(page: Page) {
    if (!(await searchState(page)).isOpen)
        return;

    await page.keyboard.press('Escape');
    await expect.poll(async () => (await searchState(page)).isOpen, { timeout: 5_000 }).toBe(false);
}

describe('search close button', () => {
    let conn: BrowserConnection;
    let page: Page;

    beforeAll(async () => {
        conn = await connectBrowser();
        page = await conn.context.newPage();
        await ensureSignedIn(page);
        await page.goto(`${BASE_URL}/chat`, { waitUntil: 'domcontentloaded' });
        await skipOnboarding(page);
        await searchInput(page).waitFor({ state: 'visible', timeout: 30_000 });
    }, 90_000);

    beforeEach(async () => {
        await closeSearchIfOpen(page);
    });

    afterAll(async () => {
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition -- page may be unset if beforeAll fails
        if (page)
            await page.close();

        if (conn.ownsBrowser) {
            await conn.context.close();
            await conn.browser.close();
        }
    });

    it('closes an empty search', async () => {
        await openSearch(page);
        await closeButton(page).click();
        await page.screenshot({ path: screenshot('search-close', '01-empty-after-x') });

        await expect.poll(() => searchState(page), { timeout: 5_000 })
            .toMatchObject({ isOpen: false, isFocused: false });
    });

    it('clears a filled search and closes it on the second press', async () => {
        await openSearch(page);
        await searchInput(page).pressSequentially('abc', { delay: 50 });
        await expect.poll(async () => (await searchState(page)).text, { timeout: 5_000 }).toBe('abc');
        // TextInput reports the text to Blazor 300ms after the last keystroke; an X pressed before
        // that lands on a field Blazor still believes is empty, and closes it.
        await page.waitForTimeout(800);

        await closeButton(page).click();
        await expect.poll(() => searchState(page), { timeout: 5_000 })
            .toMatchObject({ isOpen: true, text: '' });

        await closeButton(page).click();
        await expect.poll(async () => (await searchState(page)).isOpen, { timeout: 5_000 }).toBe(false);
    });

    it('closes when the press blurs the input without focusing the button', async () => {
        await openSearch(page);
        // WebKit on macOS takes focus from the input on a button's mousedown and gives it to nobody,
        // so focusout arrives with relatedTarget = null while the pointer is still down.
        await searchRoot(page).evaluate(root => {
            root.addEventListener('mousedown', e => {
                e.preventDefault();
                root.querySelector('input')!.blur();
            }, { capture: true, once: true });
        });

        const box = (await closeButton(page).boundingBox())!;
        await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
        await page.mouse.down();
        await page.waitForTimeout(300);
        await page.mouse.up();
        await page.screenshot({ path: screenshot('search-close', '02-blurring-press-after-x') });

        await expect.poll(() => searchState(page), { timeout: 5_000 })
            .toMatchObject({ isOpen: false, isFocused: false });
        await page.waitForTimeout(1_000);
        expect((await searchState(page)).isOpen).toBe(false);
    });
});
