/**
 * E2E test: the sign-in code step reaches the keyboard on an iPhone (#5147).
 *
 * The page runs as an iPhone 15 - its user agent, viewport and env(safe-area-inset-*) values - with the
 * numeric keyboard simulated by debugUI. After a wrong code the step shows its tallest state, the error
 * plus the resend button; it must fit the band above the keyboard, and its header must stay put
 * on a screen where it doesn't.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/sign-in-code-step-keyboard.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { BrowserContext, Page } from 'playwright';
import {
    BASE_URL, connectBrowser, dismissCookieConsent, requestEmailCode, screenshot, waitForAppReady, withUILanguage,
    type BrowserConnection,
} from './helpers';
import { emulateSafeAreas, SafeAreas } from './safe-areas';

const Preset = SafeAreas.Presets.iphone15;
const IPhoneUserAgent = 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 '
    + '(KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1';
// Never registered: a wrong code leaves no account behind
const Email = 'test-claude-agent-5147@actual.chat';
// The server sends the next code no sooner than this after the previous one
const ResendTimeout = 150_000;

const shot = (name: string) => screenshot('e2e', `sign-in-code-step-${name}`);

interface StepLayout {
    keyboardTop: number;
    safeAreaBottom: string;
    framePaddingBottom: string;
    bodyPaddingBottom: string;
    bodyBottom: number;
    scrollHeight: number;
    clientHeight: number;
    headerTop: number;
    resendBottom: number;
}

describe('sign-in code step with the keyboard open', () => {
    let conn: BrowserConnection;
    let context: BrowserContext;
    let page: Page;

    beforeAll(async () => {
        conn = await connectBrowser();
        context = await conn.browser.newContext({
            ignoreHTTPSErrors: true,
            locale: 'en-US',
            userAgent: IPhoneUserAgent,
            viewport: Preset.viewport,
            isMobile: true,
            hasTouch: true,
        });
        page = await context.newPage();
        await emulateSafeAreas(page, Preset);
        await page.goto(withUILanguage(BASE_URL), { waitUntil: 'domcontentloaded' });
        await waitForAppReady(page);
        await dismissCookieConsent(page);

        const digits = await requestEmailCode(page, Email);
        for (let i = 0; i < 6; i++) {
            await digits.nth(i).fill('2');
            await page.waitForTimeout(50);
        }
        await page.locator('.totp-input.invalid').waitFor({ state: 'visible', timeout: 20_000 });
        await page.locator('.totp-verifier .btn').waitFor({ state: 'visible', timeout: ResendTimeout });
        await digits.first().focus();
    }, ResendTimeout + 120_000);

    afterAll(async () => {
        await context.close().catch(() => { /* ignore */ });
        if (conn.ownsBrowser)
            await conn.browser.close().catch(() => { /* ignore */ });
    });

    async function showKeyboard(height?: number): Promise<void> {
        await page.evaluate(impl, height ?? null);
        await page.waitForTimeout(300);
        return;

        function impl(heightPx: number | null): void {
            interface DebugUIGlobal {
                debugUI: { showKeyboard: (heightPx: number, durationMs: number) => void };
            }
            const debugUI = (globalThis as unknown as DebugUIGlobal).debugUI;
            // 0.4 is debugUI's numeric keyboard, set here to skip its slide-in
            debugUI.showKeyboard(heightPx ?? Math.round(window.innerHeight * 0.4), 0);
        }
    }

    async function scrollToEnd(): Promise<void> {
        await page.evaluate(() => {
            const stepper = document.querySelector('.sign-in-modal .stepper')!;
            stepper.scrollTop = stepper.scrollHeight;
        });
        await page.waitForTimeout(200);
    }

    async function getLayout(): Promise<StepLayout> {
        return page.evaluate(impl);

        function impl(): StepLayout {
            const get = (selector: string) => document.querySelector<HTMLElement>(selector)!;
            const frame = get('.sign-in-modal');
            const body = get('.sign-in-modal .dialog-body');
            const stepper = get('.sign-in-modal .stepper');
            const rootStyle = getComputedStyle(document.documentElement);
            return {
                keyboardTop: Math.round(parseFloat(rootStyle.getPropertyValue('--modal-vh')) * 100),
                safeAreaBottom: getComputedStyle(body).getPropertyValue('--safe-area-bottom').trim(),
                framePaddingBottom: getComputedStyle(frame).paddingBottom,
                bodyPaddingBottom: getComputedStyle(body).paddingBottom,
                bodyBottom: Math.round(body.getBoundingClientRect().bottom),
                scrollHeight: stepper.scrollHeight,
                clientHeight: stepper.clientHeight,
                headerTop: Math.round(get('.sign-in-modal .stepper-header').getBoundingClientRect().top),
                resendBottom: Math.round(get('.totp-verifier .btn').getBoundingClientRect().bottom),
            };
        }
    }

    it('fits the error and the resend button above the numeric keyboard', async () => {
        // act
        await showKeyboard();
        const layout = await getLayout();
        await page.screenshot({ path: shot('numeric-keyboard') });

        // assert — the bottom inset is applied once, without the home-indicator part the keyboard covers
        expect(layout.safeAreaBottom).toBe('0px');
        expect(layout.framePaddingBottom).toBe('0px');
        expect(layout.bodyPaddingBottom).toBe('16px');
        expect(layout.bodyBottom).toBe(layout.keyboardTop);

        // assert — nothing to scroll, and the last control is above the keyboard
        expect(layout.scrollHeight).toBeLessThanOrEqual(layout.clientHeight);
        expect(layout.resendBottom).toBeLessThanOrEqual(layout.keyboardTop - 16);
    }, 60_000);

    it('keeps the header in place when the step has to scroll', async () => {
        // arrange — a keyboard tall enough to leave the step no room
        await showKeyboard(Math.round(Preset.viewport.height * 0.6));
        const before = await getLayout();

        // act
        await scrollToEnd();
        const after = await getLayout();
        await page.screenshot({ path: shot('tall-keyboard-scrolled') });

        // assert
        expect(before.scrollHeight).toBeGreaterThan(before.clientHeight);
        expect(after.headerTop).toBe(before.headerTop);
        expect(after.resendBottom).toBeLessThanOrEqual(after.keyboardTop - 16);
    }, 60_000);
});
