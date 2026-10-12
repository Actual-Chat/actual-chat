/**
 * E2E test: the transcription toggle on the call screen (#4942).
 *
 * Two users are on a call in their peer chat, at a phone-sized viewport. The control bar carries
 * a transcription button on both sides, lit while the call is transcribed. A 1:1 call has no
 * host, so either side may press it: the callee turns transcription off, the caller turns it
 * back on, and each flip shows on both screens and leaves a system line in the chat.
 *
 * At desktop width the call lives in the chat header and has no control bar; the same button is
 * in the Manage row of the right panel's Call tab, and the callee has it there too.
 *
 * Screenshots go to tmp/e2e-call-transcription/.
 *
 * Prerequisites:
 * - Server running (server-loop / run-watch), locally: calls are incomplete UI, which the test
 *   turns on for both accounts - test agents are admins only on a local server.
 *
 * Run:
 *   AC_E2E_SERVER=external npx vitest run tests/ts/e2e/call-transcription-toggle.test.ts --config vitest.config.e2e.ts
 */

import * as fs from 'fs';
import * as path from 'path';
import { describe, it, expect, beforeAll, afterAll, afterEach } from 'vitest';
import type { Page } from 'playwright';
import {
    CALL_CONTROLS, NARROW, WIDE, hangUpIfAny, signInBoth, signOutBoth, startPeerCall, type Users,
} from './peer-call';

const SHOTS_DIR = path.join(process.cwd(), 'tmp', 'e2e-call-transcription');
fs.mkdirSync(SHOTS_DIR, { recursive: true });
const shot = (name: string) => path.join(SHOTS_DIR, `${name}.png`);

const TOGGLE = `${CALL_CONTROLS} .btn-transcription`;
const CALL_TAB = '.chat-side-panel .live-session-member-list';
const MANAGE_BUTTONS = `${CALL_TAB} .c-manage-btn`;
const MANAGE_TOGGLE = `${MANAGE_BUTTONS}:has(.icon-voice-to-text-message)`;

/** Waits for the toggle to show the given state: it follows the server, a round trip after a click. */
async function waitForToggle(page: Page, isOn: boolean) {
    const selector = isOn ? `${TOGGLE}.on` : `${TOGGLE}:not(.on)`;
    await page.locator(selector).first().waitFor({ state: 'visible', timeout: 15_000 });
}

/** Opens the right panel, which shows its Call tab while the chat has a call. */
async function openCallTab(page: Page) {
    await page.locator('i.icon-layout:visible').first().click();
    await page.locator(MANAGE_TOGGLE).first().waitFor({ state: 'visible', timeout: 15_000 });
    // The panel slides in; wait until it stops moving
    await page.waitForTimeout(1_000);
}

async function waitForManageToggle(page: Page, isOn: boolean) {
    const selector = `${MANAGE_TOGGLE}.${isOn ? 'on' : 'off'}`;
    await page.locator(selector).first().waitFor({ state: 'visible', timeout: 15_000 });
}

/**
 * The newest transcription line - the chat stays in the DOM behind the call screen, but as it
 * keeps scrolling it drops the older lines, so they can't be counted.
 */
async function getLastTranscriptionLine(page: Page): Promise<string> {
    const lines = await page.locator('.chat-view').getByText(/turned (on|off) transcription/).allInnerTexts();
    return lines.at(-1) ?? '';
}

describe('call transcription toggle', () => {
    let users: Users | undefined;

    beforeAll(async () => {
        users = await signInBoth(NARROW);
    }, 180_000);

    afterEach(async () => {
        await hangUpIfAny(users?.caller);
        await hangUpIfAny(users?.callee);
    }, 60_000);

    afterAll(async () => {
        await signOutBoth(users);
    });

    it('lets either side of a 1:1 call turn transcription off and on', async () => {
        // arrange
        const { caller, callee } = users!;
        await startPeerCall(caller, callee);
        for (const page of [caller, callee]) {
            await waitForToggle(page, true);
            const isEnabled = await page.locator(TOGGLE).first().isEnabled();
            expect(isEnabled, 'either side may press it').toBe(true);
        }
        await caller.screenshot({ path: shot('caller-on') });

        // act - the callee, who isn't the call's host, turns it off
        await callee.locator(TOGGLE).first().click();

        // assert
        for (const page of [caller, callee])
            await waitForToggle(page, false);
        await expect.poll(() => getLastTranscriptionLine(caller), { timeout: 15_000 })
            .toContain('turned off transcription');
        await caller.screenshot({ path: shot('caller-off') });
        await callee.screenshot({ path: shot('callee-off') });

        // act - the caller turns it back on
        await caller.locator(TOGGLE).first().click();

        // assert
        for (const page of [caller, callee])
            await waitForToggle(page, true);
        await expect.poll(() => getLastTranscriptionLine(caller), { timeout: 15_000 })
            .toContain('turned on transcription');
    }, 180_000);
});

describe('call transcription toggle in the right panel', () => {
    let users: Users | undefined;

    beforeAll(async () => {
        users = await signInBoth(WIDE);
    }, 180_000);

    afterEach(async () => {
        await hangUpIfAny(users?.caller);
        await hangUpIfAny(users?.callee);
    }, 60_000);

    afterAll(async () => {
        await signOutBoth(users);
    });

    it('lets the callee of a 1:1 call turn transcription off and on, and nothing else', async () => {
        // arrange
        const { caller, callee } = users!;
        await startPeerCall(caller, callee);
        for (const page of [caller, callee]) {
            await openCallTab(page);
            await waitForManageToggle(page, true);
        }
        const calleeButtonCount = await callee.locator(MANAGE_BUTTONS).count();
        const callerButtonCount = await caller.locator(MANAGE_BUTTONS).count();
        await callee.screenshot({ path: shot('panel-callee-on') });
        await caller.screenshot({ path: shot('panel-caller-on') });

        // act - the callee, who isn't the call's host, turns it off
        await callee.locator(MANAGE_TOGGLE).first().click();

        // assert
        expect(calleeButtonCount, 'the callee manages transcription only').toBe(1);
        expect(callerButtonCount, 'the host also manages video').toBe(2);
        for (const page of [caller, callee])
            await waitForManageToggle(page, false);
        await expect.poll(() => getLastTranscriptionLine(callee), { timeout: 15_000 })
            .toContain('turned off transcription');
        await callee.screenshot({ path: shot('panel-callee-off') });
        await caller.screenshot({ path: shot('panel-caller-off') });

        // act - the callee turns it back on
        await callee.locator(MANAGE_TOGGLE).first().click();

        // assert
        for (const page of [caller, callee])
            await waitForManageToggle(page, true);
        await expect.poll(() => getLastTranscriptionLine(callee), { timeout: 15_000 })
            .toContain('turned on transcription');
        await callee.screenshot({ path: shot('panel-callee-on-again') });
    }, 180_000);
});
