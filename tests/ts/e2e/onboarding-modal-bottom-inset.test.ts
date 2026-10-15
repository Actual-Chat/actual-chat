/**
 * E2E test: the onboarding modal's gradient reaches the bottom edge of the modal frame, with no strip of
 * the frame's own surface showing under it (#5343). The wide layout is where the shared no-footer body
 * inset used to win over the onboarding override; the narrow layout is checked for the same reason.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/onboarding-modal-bottom-inset.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { BrowserContext, Page } from 'playwright';
import {
    BASE_URL, connectBrowser, dismissCookieConsent, screenshot, waitForAppReady,
    type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e', `5343-onboarding-bottom-${name}`);

const PHONE = '+1 555 555 5554';
const VIEWPORTS = [
    { name: 'narrow', width: 390, height: 844 },
    { name: 'wide', width: 1400, height: 900 },
];

interface BottomMeasure {
    bodyPaddingBottom: string;
    gapBelowStepper: number;
    isNarrow: boolean;
}

describe('onboarding modal bottom inset', () => {
    let conn: BrowserConnection;
    let context: BrowserContext;
    let page: Page;

    beforeAll(async () => {
        conn = await connectBrowser();
        context = await conn.browser.newContext({ ignoreHTTPSErrors: true });
        page = await context.newPage();
        await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
        await waitForAppReady(page);
        await dismissCookieConsent(page);
        await page.evaluate(async (phone) => {
            /* eslint-disable @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-member-access,
               @typescript-eslint/no-unsafe-call, @typescript-eslint/no-unsafe-assignment */
            const debugUI = (window as any).debugUI;
            await debugUI.signIn(phone, { skipOnboarding: false, skipBubbles: false });
            await debugUI.resetOnboarding(true);
            await debugUI.resetBubbles(false);
            /* eslint-enable @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-member-access,
               @typescript-eslint/no-unsafe-call, @typescript-eslint/no-unsafe-assignment */
        }, PHONE);
        await page.waitForTimeout(1_000);
    }, 120_000);

    afterAll(async () => {
        await page.evaluate(async () => {
            /* eslint-disable @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-member-access,
               @typescript-eslint/no-unsafe-call */
            await (window as any).debugUI?.resetOnboarding(false);
            /* eslint-enable @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-member-access,
               @typescript-eslint/no-unsafe-call */
        }).catch(() => { /* ignore */ });
        await page.waitForTimeout(500);
        await context.close().catch(() => { /* ignore */ });
        if (conn.ownsBrowser)
            await conn.browser.close();
    }, 60_000);

    for (const viewport of VIEWPORTS) {
        it(`leaves no frame surface under the first tutorial slide (${viewport.name})`, async () => {
            await page.setViewportSize({ width: viewport.width, height: viewport.height });
            await page.reload({ waitUntil: 'domcontentloaded' });
            await waitForAppReady(page);
            await dismissCookieConsent(page);

            const modal = page.locator('[id^="Modal-OnboardingModal"]').first();
            await modal.waitFor({ state: 'visible', timeout: 30_000 });
            await modal.locator('.onboarding-tutorial.tutorial-1').waitFor({ state: 'visible', timeout: 15_000 });
            await page.waitForTimeout(800);
            await page.screenshot({ path: shot(viewport.name) });

            const measure = await page.evaluate((): BottomMeasure => {
                const frame = document.querySelector('.modal-frame.onboarding-modal')!;
                const body = frame.querySelector('.dialog-body')!;
                const stepper = frame.querySelector('.stepper')!;
                return {
                    bodyPaddingBottom: getComputedStyle(body).paddingBottom,
                    gapBelowStepper: frame.getBoundingClientRect().bottom - stepper.getBoundingClientRect().bottom,
                    isNarrow: document.body.classList.contains('narrow'),
                };
            });
            expect(measure.isNarrow).toBe(viewport.name === 'narrow');
            expect(measure.bodyPaddingBottom).toBe('0px');
            expect(measure.gapBelowStepper).toBe(0);
        }, 90_000);
    }
});
