/**
 * E2E test: wheeling back to the end of a chat, slowly or as a fling, re-engages the sticky edge, so
 * the next message is followed (#5336).
 *
 * Run:
 *   AC_E2E_SERVER=external npx vitest run tests/ts/e2e/sticky-edge-after-scroll.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { BrowserContext, Page } from 'playwright';
import {
    TEST_EMAIL, TEST_EMAIL_2, connectBrowser, newUserContext, openChat, screenshot, type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e-sticky-edge', name);

interface ListState {
    isStickyEnd: boolean;
    distanceToEnd: number | null;
    hasVeryLastItem: boolean;
}

async function typeAndSend(page: Page, text: string) {
    const messageInput = page.locator('#message-input .editor-content[contenteditable="true"]').first();
    await messageInput.click({ force: true });
    await messageInput.evaluate(el => {
        el.innerHTML = '';
        el.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await page.keyboard.type(text);
    await messageInput.dispatchEvent('keypress', {
        key: 'Enter', code: 'Enter', bubbles: true, cancelable: true,
    });
    await page.locator(`.chat-message-markup:has-text("${text}")`).first()
        .waitFor({ state: 'visible', timeout: 15_000 });
}

function listState(page: Page): Promise<ListState> {
    return page.locator('.virtual-list.infinite-list').first().evaluate(root => {
        const endAnchor = root.querySelector('.c-end-anchor');
        const bottom = root.getBoundingClientRect().bottom;
        return {
            isStickyEnd: root.hasAttribute('data-sticky-end'),
            distanceToEnd: endAnchor == null ? null : endAnchor.getBoundingClientRect().bottom - bottom,
            hasVeryLastItem: root.querySelector('.c-spacer-end')?.getBoundingClientRect().height === 0,
        };
    });
}

describe('sticky edge after scrolling to the end', () => {
    let conn: BrowserConnection;
    let aliceCtx: BrowserContext;
    let bobCtx: BrowserContext;
    let alice: Page;
    let bob: Page;

    beforeAll(async () => {
        conn = await connectBrowser();
        ({ context: aliceCtx, page: alice } = await newUserContext(conn, TEST_EMAIL));
        ({ context: bobCtx, page: bob } = await newUserContext(conn, TEST_EMAIL_2));
    }, 180_000);

    afterAll(async () => {
        await aliceCtx.close().catch(() => { /* ignore */ });
        await bobCtx.close().catch(() => { /* ignore */ });
        if (conn.ownsBrowser) {
            await conn.context.close().catch(() => { /* ignore */ });
            await conn.browser.close().catch(() => { /* ignore */ });
        }
    });

    it.each([
        { name: 'slow wheel', deltaY: 100, pauseMs: 40 },
        { name: 'fling', deltaY: 700, pauseMs: 16 },
    ])('should follow new messages after wheeling back to the end ($name)', async ({ deltaY, pauseMs }) => {
        // arrange - Alice is pinned at the end, leaves it, and messages arrive while she is away
        const stamp = Date.now();
        await openChat(bob);
        await openChat(alice);
        await alice.waitForTimeout(2_000);
        // A chat with unread messages opens on the "New messages" line, which is not the end
        const navigateToEnd = alice.locator('.navigate-to-end .btn').first();
        if (await navigateToEnd.isVisible().catch(() => false)) {
            await navigateToEnd.click();
            await alice.locator('.virtual-list.infinite-list[data-sticky-end]').first()
                .waitFor({ state: 'attached', timeout: 10_000 });
        }
        const list = alice.locator('.virtual-list.infinite-list').first();
        const box = (await list.boundingBox())!;
        await alice.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
        expect(await listState(alice)).toMatchObject({ isStickyEnd: true });
        for (let i = 0; i < 20; i++) {
            await alice.mouse.wheel(0, -200);
            await alice.waitForTimeout(16);
        }
        await alice.waitForTimeout(1_000);
        expect(await listState(alice)).toMatchObject({ isStickyEnd: false });
        for (let i = 0; i < 3; i++)
            await typeAndSend(bob, `sticky-away-${stamp}-${i}`);
        await alice.waitForTimeout(2_000);

        // act - wheel until the scroll position stops changing, which is the end once the tail has
        // loaded, then a message arrives
        let lastTop = -1;
        let stillCount = 0;
        for (let i = 0; i < 400 && stillCount < 8; i++) {
            await alice.mouse.wheel(0, deltaY);
            await alice.waitForTimeout(pauseMs);
            const top = await list.evaluate(e => e.scrollTop);
            stillCount = top === lastTop ? stillCount + 1 : 0;
            lastTop = top;
        }
        await alice.waitForTimeout(1_500);
        const reached = await listState(alice);
        await alice.screenshot({ path: shot('reached-end') });
        const text = `sticky-new-${stamp}`;
        await typeAndSend(bob, text);
        await alice.waitForTimeout(3_000);
        const followed = await listState(alice);
        const isNewVisible = await alice.locator(`.chat-message-markup:has-text("${text}")`).first()
            .isVisible().catch(() => false);
        await alice.screenshot({ path: shot('after-post') });

        // assert
        expect(reached, 'reaching the end by wheel must pin the list').toMatchObject({
            isStickyEnd: true, hasVeryLastItem: true,
        });
        expect(Math.abs(reached.distanceToEnd!)).toBeLessThanOrEqual(4);
        expect(followed, 'a message arriving at a pinned list must be followed').toMatchObject({
            isStickyEnd: true,
        });
        expect(Math.abs(followed.distanceToEnd!)).toBeLessThanOrEqual(4);
        expect(isNewVisible).toBe(true);
    }, 240_000);
});
