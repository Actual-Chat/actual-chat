/**
 * E2E test: a long last message edited down to one line pulls a view sitting just above the end to the
 * end, and the next message is then followed (#5361, the shrinking-last-message story).
 *
 * The list clamps the view to the end once, when the render applies the shorter model, and derives the
 * pin from the 200ms scroll settle after that clamp - unless the item's height animation is still
 * running then. With the stock 150ms transition the animation hold (100 + 150 + 60ms) ends just before
 * the settle; a longer transition makes the settle land mid-animation.
 *
 * Run:
 *   AC_E2E_SERVER=external npx vitest run tests/ts/e2e/list-end-pin-shrinking-message.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { BrowserContext, Page } from 'playwright';
import {
    TEST_EMAIL, TEST_EMAIL_2, connectBrowser, newUserContext, openChat, screenshot, type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e-list-end-pin', name);
const StockTransitionMs = 150;

interface ListState {
    isStickyEnd: boolean;
    pinnedEdge: string | null;
    distanceToEnd: number | null;
    hasVeryLastItem: boolean;
}

function editorInput(page: Page) {
    return page.locator('#message-input .editor-content[contenteditable="true"]').first();
}

async function clearEditor(page: Page) {
    const messageInput = editorInput(page);
    await messageInput.click({ force: true });
    await messageInput.evaluate(el => {
        el.innerHTML = '';
        el.dispatchEvent(new Event('input', { bubbles: true }));
    });
}

async function submitEditor(page: Page, expectedText: string) {
    await editorInput(page).dispatchEvent('keypress', {
        key: 'Enter', code: 'Enter', bubbles: true, cancelable: true,
    });
    await page.locator(`.chat-message-markup:has-text("${expectedText}")`).first()
        .waitFor({ state: 'visible', timeout: 15_000 });
}

async function typeAndSend(page: Page, lines: string[]) {
    await clearEditor(page);
    for (let i = 0; i < lines.length; i++) {
        if (i > 0)
            await page.keyboard.press('Shift+Enter');
        await page.keyboard.type(lines[i]);
    }
    await submitEditor(page, lines[0]);
}

async function editLastOwnMessage(page: Page, text: string) {
    await clearEditor(page);
    await page.keyboard.press('ArrowUp');
    await page.locator('.related-chat-entry-panel').first().waitFor({ state: 'visible', timeout: 10_000 });
    await editorInput(page).evaluate(el => {
        el.innerHTML = '';
        el.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await page.keyboard.type(text);
    await submitEditor(page, text);
}

function listState(page: Page): Promise<ListState> {
    return page.locator('.virtual-list.infinite-list').first().evaluate(root => {
        const endAnchor = root.querySelector('.c-end-anchor');
        const bottom = root.getBoundingClientRect().bottom;
        const instances = (globalThis as unknown as { InfiniteList?: { instances: Set<unknown> } })
            .InfiniteList?.instances;
        const instance = instances == null
            ? null
            : [...instances].find(x => (x as { ref: Element }).ref === root) as { pinnedEdge: string | null } | undefined;
        return {
            isStickyEnd: root.hasAttribute('data-sticky-end'),
            pinnedEdge: instance?.pinnedEdge ?? null,
            distanceToEnd: endAnchor == null ? null : endAnchor.getBoundingClientRect().bottom - bottom,
            hasVeryLastItem: root.querySelector('.c-spacer-end')?.getBoundingClientRect().height === 0,
        };
    });
}

async function ensureAtEnd(page: Page) {
    const navigateToEnd = page.locator('.navigate-to-end .btn').first();
    if (await navigateToEnd.isVisible().catch(() => false))
        await navigateToEnd.click();
    await page.locator('.virtual-list.infinite-list[data-sticky-end]').first()
        .waitFor({ state: 'attached', timeout: 10_000 });
}

describe('list end pin after the last message shrinks', () => {
    let conn: BrowserConnection | undefined;
    let aliceCtx: BrowserContext | undefined;
    let bobCtx: BrowserContext | undefined;
    let alice: Page;
    let bob: Page;
    let aliceTransitionMs = 0;

    async function openAliceWithTransition(transitionMs: number) {
        if (aliceTransitionMs === transitionMs)
            return;

        await aliceCtx?.close().catch(() => { /* ignore */ });
        ({ context: aliceCtx, page: alice } = await newUserContext(conn!, TEST_EMAIL));
        if (transitionMs !== StockTransitionMs)
            await aliceCtx.addInitScript((ms: number) => {
                document.addEventListener('DOMContentLoaded', () => {
                    const style = document.createElement('style');
                    style.textContent = '.virtual-list > .c-wrapper > ul.c-virtual-container .item.height-controlled'
                        + ` { transition-duration: ${ms}ms !important; }`;
                    document.head.appendChild(style);
                });
            }, transitionMs);
        await openChat(alice);
        await alice.waitForTimeout(2_000);
        aliceTransitionMs = transitionMs;
    }

    beforeAll(async () => {
        conn = await connectBrowser();
        ({ context: bobCtx, page: bob } = await newUserContext(conn, TEST_EMAIL_2));
        await openChat(bob);
    }, 180_000);

    afterAll(async () => {
        await aliceCtx?.close().catch(() => { /* ignore */ });
        await bobCtx?.close().catch(() => { /* ignore */ });
        if (conn?.ownsBrowser) {
            await conn.context.close().catch(() => { /* ignore */ });
            await conn.browser.close().catch(() => { /* ignore */ });
        }
    });

    it.each([
        { transitionMs: StockTransitionMs, attempt: 1 },
        { transitionMs: StockTransitionMs, attempt: 2 },
        { transitionMs: 300, attempt: 1 },
        { transitionMs: 300, attempt: 2 },
        { transitionMs: 600, attempt: 1 },
        { transitionMs: 600, attempt: 2 },
    ])('should follow the next message after the last one shrinks under a free view (transition $transitionMs ms, attempt $attempt)', async ({ transitionMs, attempt }) => {
        // arrange - Alice is pinned at the end, a long message arrives, she leaves the end by a hair
        const stamp = `${Date.now()}-${attempt}`;
        await openAliceWithTransition(transitionMs);
        await ensureAtEnd(alice);
        const list = alice.locator('.virtual-list.infinite-list').first();
        const box = (await list.boundingBox())!;
        await alice.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
        const longLines = Array.from({ length: 6 }, (_, i) => `shrink-long-${stamp}-line-${i + 1}`);
        await typeAndSend(bob, longLines);
        await alice.waitForTimeout(2_500);
        const afterLong = await listState(alice);
        const transition = await alice.locator('.item.height-controlled').first()
            .evaluate(el => getComputedStyle(el).transitionDuration).catch(() => 'n/a');
        await alice.mouse.wheel(0, -60);
        await alice.waitForTimeout(1_500);
        const afterWheel = await listState(alice);
        await alice.screenshot({ path: shot(`t${transitionMs}-${attempt}-above-end`) });

        // act - the long message shrinks to one line, then one more message arrives
        await editLastOwnMessage(bob, `shrink-short-${stamp}`);
        await alice.waitForTimeout(3_000);
        const afterShrink = await listState(alice);
        await alice.screenshot({ path: shot(`t${transitionMs}-${attempt}-after-shrink`) });
        const text = `shrink-next-${stamp}`;
        await typeAndSend(bob, [text]);
        await alice.waitForTimeout(3_000);
        const followed = await listState(alice);
        const isNextVisible = await alice.locator(`.chat-message-markup:has-text("${text}")`).first()
            .isVisible().catch(() => false);
        await alice.screenshot({ path: shot(`t${transitionMs}-${attempt}-after-next`) });
        console.log(`[transition ${transitionMs}ms #${attempt}, computed ${transition}]`,
            JSON.stringify({ afterLong, afterWheel, afterShrink, followed, isNextVisible }));

        // assert
        expect(afterLong, 'the long message must be followed first').toMatchObject({ isStickyEnd: true });
        expect(afterWheel, 'a wheel up must free the list').toMatchObject({ isStickyEnd: false });
        expect(Math.abs(afterShrink.distanceToEnd!), 'the shrink must carry the view to the end').toBeLessThanOrEqual(4);
        expect(followed, 'a message arriving at a view at the end must be followed').toMatchObject({
            isStickyEnd: true,
        });
        expect(Math.abs(followed.distanceToEnd!)).toBeLessThanOrEqual(4);
        expect(isNextVisible).toBe(true);
    }, 240_000);
});
