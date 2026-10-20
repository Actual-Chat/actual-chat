/**
 * E2E test: one author leaves several reactions on one message.
 *
 * Alice reacts with a thumbs-up from the hover menu, then adds a second emoji from the
 * reaction picker: both badges stay, both marked as hers, and both survive a reload. Bob
 * joins in on the second emoji, and Alice then takes her thumbs-up back - the second emoji
 * keeps both authors.
 *
 * Run:
 *   AC_E2E_SERVER=external npx vitest run tests/ts/e2e/multiple-reactions.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { BrowserContext, Locator, Page } from 'playwright';
import {
    DEFAULT_CHAT_URL, TEST_EMAIL, TEST_EMAIL_2, connectBrowser, entryLidOf, newUserContext, openChat,
    postText, reactWithThumbsUp, screenshot, withUILanguage, type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e-multiple-reactions', name);

const THUMBS_UP_SVG = 'emoji-thumbs-up';

function badges(page: Page, entryLid: number): Locator {
    return page.locator(`[data-chat-entry-id$=":${entryLid}"] .reaction-badge`);
}

function badge(page: Page, entryLid: number, svgName: string): Locator {
    return badges(page, entryLid).filter({ has: page.locator(`img[src*="/${svgName}.svg"]`) }).first();
}

/** Shoots the message row once the reaction animation has played out. */
async function shootMessage(page: Page, entryLid: number, name: string) {
    await page.mouse.move(0, 0);
    await page.waitForTimeout(1_500);
    await page.locator(`[data-chat-entry-id$=":${entryLid}"]`).first().screenshot({ path: shot(name) });
}

/** Waits until the message shows exactly these badges, each marked as own or not. */
async function expectOwnBadges(page: Page, entryLid: number, expected: Record<string, boolean>) {
    await expect.poll(() => badges(page, entryLid).count(), { timeout: 15_000 })
        .toBe(Object.keys(expected).length);
    for (const [svgName, mustBeOwn] of Object.entries(expected))
        await expect.poll(
            () => badge(page, entryLid, svgName).evaluate(el => el.classList.contains('own')).catch(() => false),
            { timeout: 15_000 },
        ).toBe(mustBeOwn);
}

describe('multiple reactions per message', () => {
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

    it('keeps each of an author\'s reactions and toggles them one by one', async () => {
        // arrange
        const text = `mr-${Date.now()}`;
        await openChat(alice);
        await postText(alice, text);
        const lid = await entryLidOf(alice, text);
        const messageUrl = withUILanguage(`${DEFAULT_CHAT_URL}?n=${lid}`);
        // A message still being sent has no hover menu yet
        await openChat(alice, messageUrl);

        // act — a thumbs-up from the hover menu, then the picker's first other emoji
        await reactWithThumbsUp(alice, lid);
        await expectOwnBadges(alice, lid, { [THUMBS_UP_SVG]: true });
        // On a hoverable desktop the "+" next to the badges is hidden; the message menu leads with the picker
        await alice.locator(`[data-chat-entry-id$=":${lid}"] .chat-message`).first().hover();
        const moreButton = alice.locator('.message-hover-menu-new .btn-round:has(.icon-more-vertical)').first();
        await moreButton.waitFor({ state: 'visible', timeout: 5_000 });
        await moreButton.click();
        const choices = alice.locator('.message-menu .reaction-select-reaction');
        await choices.first().waitFor({ state: 'visible', timeout: 10_000 });
        const choice = choices.filter({ hasNot: alice.locator(`img[src*="/${THUMBS_UP_SVG}.svg"]`) }).first();
        const choiceSrc = await choice.locator('img').first().getAttribute('src');
        const second = /\/([^/]+)\.svg$/.exec(choiceSrc ?? '')?.[1] ?? '';
        expect(second, `the picked emoji's svg in "${choiceSrc}"`).not.toBe('');
        await choice.click();

        // assert — the second emoji is added next to the first, not instead of it
        await expectOwnBadges(alice, lid, { [THUMBS_UP_SVG]: true, [second]: true });
        await shootMessage(alice, lid, 'alice-two-reactions');

        await openChat(alice, messageUrl);
        await expectOwnBadges(alice, lid, { [THUMBS_UP_SVG]: true, [second]: true });
        await shootMessage(alice, lid, 'alice-after-reload');

        // act — Bob adds the second emoji too
        await openChat(bob, messageUrl);
        await expectOwnBadges(bob, lid, { [THUMBS_UP_SVG]: false, [second]: false });
        await badge(bob, lid, second).click();

        // assert
        await expectOwnBadges(bob, lid, { [THUMBS_UP_SVG]: false, [second]: true });
        await shootMessage(bob, lid, 'bob-joined');
        await expect.poll(() => badge(alice, lid, second).locator('.c-author').count(), { timeout: 15_000 })
            .toBe(2);

        // act — Alice takes her thumbs-up back
        await badge(alice, lid, THUMBS_UP_SVG).click();

        // assert — only the thumbs-up goes; the second emoji keeps both authors
        await expectOwnBadges(alice, lid, { [second]: true });
        await expect.poll(() => badge(alice, lid, second).locator('.c-author').count(), { timeout: 15_000 })
            .toBe(2);
        await shootMessage(alice, lid, 'alice-removed-thumbs-up');

        await openChat(alice, messageUrl);
        await expectOwnBadges(alice, lid, { [second]: true });
        await expectOwnBadges(bob, lid, { [second]: true });
        await shootMessage(alice, lid, 'alice-final-after-reload');
    }, 300_000);
});
