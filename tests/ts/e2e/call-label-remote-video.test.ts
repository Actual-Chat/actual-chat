/**
 * E2E test: on a phone, the label of a video that has just started keeps clear of the call's footer
 * buttons (#5303).
 *
 * The caller is an iPhone 15 - its viewport, real env(safe-area-inset-*) values and corner radius - on a
 * call with a peer. The peer's video fills the caller's screen when it starts, and its label has to be
 * laid out around the footer buttons right then, not after the next layout change: it must neither
 * overlap the buttons nor leave the safe area, with the peer's video alone and with the caller's own
 * camera on, whichever came first, in speaker and in grid view, and after the bars were hidden by a tap
 * and brought back. Once with a short name on the label and once with a name long enough to be lifted
 * above the buttons.
 *
 * Screenshots go to tmp/e2e-call-label-remote-video/.
 *
 * Prerequisites:
 * - Server running (server-loop / run-watch), locally - see peer-call.ts.
 *
 * Run:
 *   AC_E2E_SERVER=external npx vitest run tests/ts/e2e/call-label-remote-video.test.ts --config vitest.config.e2e.ts
 */

import * as fs from 'fs';
import * as path from 'path';
import { describe, it, expect, beforeAll, afterAll, afterEach } from 'vitest';
import type { Page } from 'playwright';
import { BASE_URL, skipOnboarding, withUILanguage } from './helpers';
import {
    CALL_CONTROLS, CALL_SCREEN, OWN_VIDEO, REMOTE_VIDEO, hangUpIfAny, signInBoth, signOutBoth, startPeerCall,
    type Users,
} from './peer-call';
import { emulateSafeAreas, SafeAreas } from './safe-areas';
import { setGallery } from './video-call';

const SHOTS_DIR = path.join(process.cwd(), 'tmp', 'e2e-call-label-remote-video');
fs.mkdirSync(SHOTS_DIR, { recursive: true });
const shot = (name: string) => path.join(SHOTS_DIR, `${name}.png`);

const Phone = SafeAreas.Presets.iphone15;
// The label caps a name at 16 characters: this one is cut there, and still too wide to fit beside the buttons
const Names = { short: 'Bo', long: 'Bartholomew Fitzgerald-Montgomery' };

/** Renames the default avatar - the name on the user's label - and returns the name it had. */
async function setDefaultAvatarName(page: Page, name: string): Promise<string> {
    await page.goto(withUILanguage(`${BASE_URL}/settings`), { waitUntil: 'domcontentloaded' });
    await skipOnboarding(page);
    await page.locator('.settings-modal').waitFor({ state: 'visible', timeout: 15_000 });
    const accountTab = page.locator('text=Your Account').first();
    if (await accountTab.isVisible({ timeout: 3_000 }).catch(() => false))
        await accountTab.click();
    const defaultAvatarButtons = page.locator(':has(> button.selected:has(i.icon-star))').first();
    const editButton = defaultAvatarButtons.locator('button:has(i.icon-edit)');
    await editButton.waitFor({ state: 'visible', timeout: 10_000 });
    await editButton.click();
    const modal = page.locator('.edit-avatar-modal');
    await modal.waitFor({ state: 'visible', timeout: 5_000 });
    const nameInput = modal.locator('input#avatar-editor-name, input[id*="name" i]').first();
    await nameInput.waitFor({ state: 'visible', timeout: 5_000 });
    const oldName = await nameInput.inputValue();
    // Ctrl+A doesn't reliably select in this input; select() does, and typing replaces the selection
    await nameInput.click();
    await nameInput.selectText();
    await nameInput.pressSequentially(name, { delay: 20 });
    await nameInput.press('Tab');
    await modal.locator('button:has-text("Save")').first().click();
    await modal.waitFor({ state: 'hidden', timeout: 10_000 });
    return oldName;
}

async function expectLabelsClearOfFooter(page: Page, isGrid: boolean, labelCount: number, what: string) {
    await setGallery(page, isGrid);
    const labels = page.locator(`${CALL_SCREEN} .video-participant-label:visible`);
    await expect.poll(() => labels.count(), { timeout: 15_000, message: what }).toBe(labelCount);
    // Polled: the call screen settles its labels around the footer buttons after a layout change
    await expect.poll(impl, { timeout: 10_000, message: `${what}, ${isGrid ? 'grid' : 'speaker'} view` })
        .toEqual([]);

    // What's wrong with each visible label, with the inline style the layout gave it, e.g.
    // `Bo: overlaps the footer buttons (style: 'max-width: 300px;')`
    async function impl(): Promise<string[]> {
        const { labels, buttons } = await page.evaluate(selector => {
            const toBox = (r: DOMRect) => ({ x: r.x, y: r.y, width: r.width, height: r.height });
            // The central control cluster, as call-screen.ts flows the labels around it
            const buttonRects = [...document.querySelectorAll(
                `${selector} .call-screen-footer > :not(.c-side):not(.btn-chat), `
                + `${selector} .call-screen-footer > .c-side > :not(.btn-incut)`)]
                .map(el => el.getBoundingClientRect())
                .filter(r => r.width > 0 && r.height > 0);
            const left = Math.min(...buttonRects.map(r => r.left));
            const top = Math.min(...buttonRects.map(r => r.top));
            const right = Math.max(...buttonRects.map(r => r.right));
            const bottom = Math.max(...buttonRects.map(r => r.bottom));
            return {
                labels: [...document.querySelectorAll(`${selector} .video-participant-label`)]
                    .map(label => ({
                        text: label.textContent.trim(),
                        box: toBox(label.getBoundingClientRect()),
                        style: (label as HTMLElement).style.cssText,
                    }))
                    .filter(label => label.box.width > 0),
                buttons: buttonRects.length > 0 ? { x: left, y: top, width: right - left, height: bottom - top } : null,
            };
        }, CALL_SCREEN);
        return labels.flatMap(({ text, box, style }) => {
            const problems = SafeAreas.getProblems(box, Phone);
            if (buttons
                && box.x < buttons.x + buttons.width && box.x + box.width > buttons.x
                && box.y < buttons.y + buttons.height && box.y + box.height > buttons.y)
                problems.push('overlaps the footer buttons');
            return problems.map(problem => `${text}: ${problem} (style: '${style}')`);
        });
    }
}

/** A tap on the video hides the bars, the next brings them back; returns once they have slid back in. */
async function hideAndShowBars(page: Page) {
    const video = page.locator(`${CALL_SCREEN} .remote-video-container.item-focused`).first();
    const isFooterSettled = () => page.evaluate(selector => {
        const screen = document.querySelector(selector);
        const footer = screen?.querySelector('.call-screen-footer');
        return screen != null && footer != null && !screen.hasAttribute('data-bars-hidden')
            && ['none', 'matrix(1, 0, 0, 1, 0, 0)'].includes(getComputedStyle(footer).transform);
    }, CALL_SCREEN);
    await video.click({ position: { x: 50, y: 300 } });
    await expect.poll(() => page.locator(`${CALL_SCREEN}[data-bars-hidden]`).count(), { timeout: 5_000 }).toBe(1);
    await page.waitForTimeout(400);
    await video.click({ position: { x: 50, y: 300 } });
    await expect.poll(isFooterSettled, { timeout: 5_000 }).toBe(true);
}

async function expectLabelText(page: Page, name: string) {
    const label = page.locator(`${CALL_SCREEN} .video-participant-label:visible > span`).first();
    await expect.poll(() => label.textContent(), { timeout: 15_000 }).toBe(name);
}

describe('call participant label of a video that has just started, on a phone', () => {
    let users: Users;
    let originalCalleeName: string;

    beforeAll(async () => {
        users = await signInBoth(Phone.viewport, async page => {
            await emulateSafeAreas(page, Phone);
            // Headless Chromium reports a desktop device; the call screen's phone layout needs body.device-mobile
            await page.addInitScript(() => {
                Object.defineProperty(navigator, 'userAgentData', { get: () => ({ mobile: true }) });
            });
        });
        originalCalleeName = await setDefaultAvatarName(users.callee, Names.short);
    }, 180_000);

    afterEach(async () => {
        await hangUpIfAny(users.caller);
        await hangUpIfAny(users.callee);
    }, 30_000);

    afterAll(async () => {
        if (originalCalleeName)
            await setDefaultAvatarName(users.callee, originalCalleeName).catch((e: unknown) => console.log('restore:', e));
        await signOutBoth(users);
    }, 60_000);

    for (const kind of ['short', 'long'] as const) {
        const name = Names[kind];

        it(`${kind} name: the peer's video starts first`, async () => {
            // arrange - the caller has no camera; the peer's video is the first on the screen
            const { caller, callee } = users;
            if (kind === 'long')
                await setDefaultAvatarName(callee, name);
            await startPeerCall(caller, callee);
            await caller.locator(CALL_CONTROLS).first().waitFor({ state: 'visible', timeout: 30_000 });

            // act - the peer turns the camera on
            await callee.locator(`${CALL_CONTROLS} .btn-video-toggle`).first().click();
            await caller.locator(REMOTE_VIDEO).first().waitFor({ state: 'visible', timeout: 30_000 });

            // assert - incoming video only, as it came up: no layout change in between (a single tile
            // has no grid view to switch to)
            await expectLabelText(caller, name);
            await expectLabelsClearOfFooter(caller, false, 1, `${kind} name, incoming video only`);
            await caller.screenshot({ path: shot(`${kind}-1-incoming`) });

            // act - the bars go and come back: the label is laid out against the footer that slid back in
            await hideAndShowBars(caller);

            // assert
            await expectLabelsClearOfFooter(caller, false, 1, `${kind} name, bars back`);
            await caller.screenshot({ path: shot(`${kind}-1b-incoming-bars-back`) });

            // act - the caller's camera joins
            await caller.locator(`${CALL_CONTROLS} .btn-video-toggle`).first().click();
            await caller.locator(OWN_VIDEO).first().waitFor({ state: 'visible', timeout: 30_000 });

            // assert - two videos
            await expectLabelsClearOfFooter(caller, false, 1, `${kind} name, own camera joined`);
            await caller.screenshot({ path: shot(`${kind}-2-both-speaker`) });
            await expectLabelsClearOfFooter(caller, true, 2, `${kind} name, own camera joined`);
            await caller.screenshot({ path: shot(`${kind}-3-both-grid`) });
        }, 240_000);

        it(`${kind} name: the peer's video joins the caller's own`, async () => {
            // arrange - the caller's own camera fills the phone's screen
            const { caller, callee } = users;
            await startPeerCall(caller, callee);
            await caller.locator(CALL_CONTROLS).first().waitFor({ state: 'visible', timeout: 30_000 });
            await caller.locator(`${CALL_CONTROLS} .btn-video-toggle`).first().click();
            await caller.locator(OWN_VIDEO).first().waitFor({ state: 'visible', timeout: 30_000 });

            // act - the peer turns the camera on: the big tile is theirs now, and so is the label
            await callee.locator(`${CALL_CONTROLS} .btn-video-toggle`).first().click();
            await caller.locator(REMOTE_VIDEO).first().waitFor({ state: 'visible', timeout: 30_000 });

            // assert - the peer's video as it came up, then in grid view
            await expectLabelText(caller, name);
            await expectLabelsClearOfFooter(caller, false, 1, `${kind} name, incoming video joined`);
            await caller.screenshot({ path: shot(`${kind}-4-joined-speaker`) });
            await expectLabelsClearOfFooter(caller, true, 2, `${kind} name, incoming video joined`);
            await caller.screenshot({ path: shot(`${kind}-5-joined-grid`) });
        }, 240_000);
    }
});
