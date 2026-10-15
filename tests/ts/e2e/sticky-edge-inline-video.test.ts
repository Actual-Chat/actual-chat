/**
 * E2E test: the sticky edge engages at the end of a chat whose tail is a live call block (#5336):
 * two users on a video call with the video inline, so the chat list is a short strip under it.
 * Prerequisites: a local server (calls are incomplete UI, which the test turns on for both accounts).
 *
 * Run:
 *   AC_E2E_SERVER=external npx vitest run tests/ts/e2e/sticky-edge-inline-video.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { Page } from 'playwright';
import { screenshot } from './helpers';
import { WIDE, hangUpIfAny, signInBoth, signOutBoth, type Users } from './peer-call';
import { openCallChat, startCamera, startRecording } from './video-call';

const LIST = '.chat-view.virtual-list';

interface ListState {
    isStickyEnd: boolean;
    distanceToEnd: number;
    hasVeryLastItem: boolean;
    chainEnd: number;
    isNavigateToEndShown: boolean;
}

function listState(page: Page): Promise<ListState> {
    return page.evaluate(selector => {
        const root = document.querySelector<HTMLElement>(selector)!;
        interface Instance { ref: HTMLElement; chainEnd: number; renderState: { hasVeryLastItem: boolean } }
        const cls = (globalThis as unknown as { InfiniteList: { instances: Set<Instance> } }).InfiniteList;
        const list = [...cls.instances].find(x => x.ref === root)!;
        const endAnchor = root.querySelector('.c-end-anchor')!;
        const navigateToEnd = document.querySelector<HTMLElement>('.navigate-to-end');
        return {
            isStickyEnd: root.hasAttribute('data-sticky-end'),
            distanceToEnd: endAnchor.getBoundingClientRect().bottom - root.getBoundingClientRect().bottom,
            hasVeryLastItem: list.renderState.hasVeryLastItem,
            chainEnd: list.chainEnd,
            isNavigateToEndShown: navigateToEnd?.offsetParent != null,
        };
    }, LIST);
}

describe('sticky edge under an inline video call', () => {
    const shot = (name: string) => screenshot('e2e-sticky-inline', name);
    let users: Users;
    const driftWarnings: string[] = [];

    beforeAll(async () => {
        users = await signInBoth(WIDE);
        users.caller.on('console', message => {
            if (message.text().includes('model drift'))
                driftWarnings.push(message.text());
        });
    }, 240_000);

    afterAll(async () => {
        await hangUpIfAny(users.caller);
        await hangUpIfAny(users.callee);
        await signOutBoth(users);
    }, 120_000);

    it('should pin to the end and follow the transcript after wheeling back', async () => {
        // arrange - a video call with its transcript growing in the live block at the chat's tail
        const alice = users.caller;
        const bob = users.callee;
        await openCallChat(bob);
        await openCallChat(alice);
        await alice.waitForTimeout(2_000);
        // A chat with unread messages opens on the "New messages" line, which is not the end
        const navigateToEnd = alice.locator('.navigate-to-end .btn').first();
        if (await navigateToEnd.isVisible().catch(() => false)) {
            await navigateToEnd.click();
            await alice.locator('.chat-view.virtual-list[data-sticky-end]').first()
                .waitFor({ state: 'attached', timeout: 10_000 });
        }
        await startRecording(bob);
        await startRecording(alice);
        await startCamera(bob);
        await startCamera(alice);
        await alice.locator('.call-screen.has-video:not(.expanded)').first()
            .waitFor({ state: 'visible', timeout: 60_000 });
        await alice.locator('.call-screen .remote-video-container').first()
            .waitFor({ state: 'visible', timeout: 60_000 });
        await alice.evaluate(() => (window as unknown as { debugUI: { virtualListDebug(enable: boolean): void } })
            .debugUI.virtualListDebug(true));
        await alice.waitForTimeout(8_000);
        await alice.screenshot({ path: shot('in-call') });
        const inCall = await listState(alice);
        const list = alice.locator(LIST).first();
        const box = (await list.boundingBox())!;
        await alice.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
        for (let i = 0; i < 10; i++) {
            await alice.mouse.wheel(0, -120);
            await alice.waitForTimeout(40);
        }
        await alice.waitForTimeout(1_000);
        const away = await listState(alice);

        // act - wheel back to the end, then let the transcript grow
        let lastTop = -1;
        let stillCount = 0;
        for (let i = 0; i < 200 && stillCount < 8; i++) {
            await alice.mouse.wheel(0, 120);
            await alice.waitForTimeout(40);
            const top = await list.evaluate(e => e.scrollTop);
            stillCount = top === lastTop ? stillCount + 1 : 0;
            lastTop = top;
        }
        await alice.waitForTimeout(1_500);
        const back = await listState(alice);
        await alice.screenshot({ path: shot('back-at-end') });
        await expect.poll(async () => (await listState(alice)).chainEnd, { timeout: 60_000, interval: 1_000 })
            .toBeGreaterThan(back.chainEnd);
        await alice.waitForTimeout(1_500);
        const followed = await listState(alice);
        await alice.screenshot({ path: shot('followed') });

        // assert
        expect(inCall, 'a chat opened at its end stays pinned when the call starts').toMatchObject({
            isStickyEnd: true, hasVeryLastItem: true, isNavigateToEndShown: false,
        });
        expect(Math.abs(inCall.distanceToEnd)).toBeLessThanOrEqual(4);
        expect(away.isStickyEnd).toBe(false);
        expect(back, 'reaching the end by wheel must pin the list').toMatchObject({
            isStickyEnd: true, hasVeryLastItem: true, isNavigateToEndShown: false,
        });
        expect(Math.abs(back.distanceToEnd)).toBeLessThanOrEqual(4);
        expect(followed, 'the growing transcript must be followed').toMatchObject({ isStickyEnd: true });
        expect(Math.abs(followed.distanceToEnd)).toBeLessThanOrEqual(4);
        expect(driftWarnings, 'every pixel of the live block is inside an item').toEqual([]);
    }, 300_000);
});
