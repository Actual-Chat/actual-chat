/**
 * E2E test: Back in the onboarding modal returns to a step completed earlier in the same run
 * instead of bouncing forward and skipping it (#4684).
 *
 * Signs in with a predefined phone, so the phone step is already completed and the run
 * goes tutorials → email → avatar → permissions → languages.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/onboarding-back-navigation.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { BrowserContext, Locator, Page } from 'playwright';
import {
    BASE_URL, connectBrowser, dismissCookieConsent, screenshot, waitForAppReady,
    type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e', `4684-onboarding-back-${name}`);

const PHONE = '+1 555 555 5554';

describe('onboarding Back', () => {
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
               @typescript-eslint/no-unsafe-call */
            const debugUI = (window as any).debugUI;
            await debugUI.signIn(phone, { skipOnboarding: false, skipBubbles: false });
            debugUI.resetOnboarding(true);
            /* eslint-enable @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-member-access,
               @typescript-eslint/no-unsafe-call */
        }, PHONE);
        await page.waitForTimeout(1_000);
        await page.reload({ waitUntil: 'domcontentloaded' });
        await waitForAppReady(page);
        await dismissCookieConsent(page);
        await page.evaluate(() => {
            /* eslint-disable @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-member-access,
               @typescript-eslint/no-unsafe-call */
            (window as any).debugUI.resetBubbles(false);
            /* eslint-enable @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-member-access,
               @typescript-eslint/no-unsafe-call */
        });
    }, 120_000);

    afterAll(async () => {
        await page.evaluate(() => {
            /* eslint-disable @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-member-access,
               @typescript-eslint/no-unsafe-call */
            (window as any).debugUI?.resetOnboarding(false);
            /* eslint-enable @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-member-access,
               @typescript-eslint/no-unsafe-call */
        }).catch(() => { /* ignore */ });
        await page.waitForTimeout(500);
        await context.close().catch(() => { /* ignore */ });
        if (conn.ownsBrowser)
            await conn.browser.close();
    }, 60_000);

    it('returns to a step completed earlier in the same run', async () => {
        const modal = page.locator('[id^="Modal-OnboardingModal"]').first();
        await modal.waitFor({ state: 'visible', timeout: 30_000 });

        const tutorialNext = modal.locator('.tutorial-btn').first();
        const email = modal.locator('.onboarding-footer .btn-cancel').first();
        await expect.poll(async () => {
            if (await tutorialNext.isVisible().catch(() => false))
                await tutorialNext.click();
            return tutorialNext.isVisible();
        }, { timeout: 20_000, interval: 500 }).toBe(false);
        await page.screenshot({ path: shot('after-tutorials') });

        const avatar = modal.locator('.avatar-step');
        if (!await avatar.isVisible().catch(() => false))
            await email.click();
        await avatar.waitFor({ state: 'visible', timeout: 10_000 });
        const name = avatar.locator('input#name');
        if (!(await name.inputValue()).trim()) {
            await name.pressSequentially('Onboarding Tester', { delay: 20 });
            await name.press('Tab');
        }
        await clickNext(modal);

        const permissions = modal.locator('.permissions-step');
        await permissions.waitFor({ state: 'visible', timeout: 10_000 });
        await clickNext(modal);

        const languages = modal.locator('.languages-step');
        await languages.waitFor({ state: 'visible', timeout: 10_000 });
        await page.screenshot({ path: shot('languages') });

        await clickBack(modal);
        // The bug showed the step for a single render, then bounced forward again
        await page.waitForTimeout(1_500);
        await page.screenshot({ path: shot('back-to-permissions') });
        expect(await permissions.isVisible()).toBe(true);
        expect(await languages.isVisible()).toBe(false);

        await clickBack(modal);
        await avatar.waitFor({ state: 'visible', timeout: 10_000 });
        await page.screenshot({ path: shot('back-to-avatar') });

        await clickNext(modal);
        await permissions.waitFor({ state: 'visible', timeout: 10_000 });
        await page.waitForTimeout(1_500);
        await page.screenshot({ path: shot('next-to-permissions') });
        expect(await permissions.isVisible()).toBe(true);
    }, 120_000);
});

async function clickNext(modal: Locator) {
    await modal.locator('.onboarding-footer .btn-primary').first().click();
}

async function clickBack(modal: Locator) {
    await modal.locator('.onboarding-footer .btn-back').first().click();
}
