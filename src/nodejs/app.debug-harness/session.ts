import { chromium, type Browser, type BrowserContext, type Page } from 'playwright';
import {
    BASE_URL,
    clearBrowserCache,
    dismissCookieConsent,
    getLocalHosts,
    skipOnboarding,
    waitForAppReady,
} from '../../../tests/ts/e2e/helpers';
import type { DebugUIRoot } from './debug-ui-api';

export interface BrowserSession {
    browser: Browser;
    context: BrowserContext;
    page: Page;
    port: number;
}

/**
 * Attaches to a Chrome already started by `ai chrome[*N]` on `port`, reusing its first
 * context so the profile's cookies (and therefore the signed-in user) are kept.
 */
export async function openBrowser(port: number): Promise<BrowserSession> {
    let lastError: unknown;
    for (const host of getLocalHosts()) {
        try {
            const browser = await chromium.connectOverCDP(`http://${host}:${port}`, { timeout: 3_000 });
            const contexts = browser.contexts();
            const context = contexts.length > 0 ? contexts[0] : await browser.newContext();
            const pages = context.pages();
            const page = pages.find(p => p.url().startsWith(BASE_URL))
                ?? (pages.length > 0 ? pages[0] : await context.newPage());
            return { browser, context, page, port };
        } catch (e: unknown) {
            lastError = e;
        }
    }
    throw new Error(
        `No Chrome on port ${port} - start one with \`ai chrome*2\`. `
        + `Last error: ${lastError instanceof Error ? lastError.message : String(lastError)}`);
}

/**
 * Signs in through `debugUI.signIn`, which skips the modal flow the e2e helpers walk.
 * Reuses an already signed-in account unless `isFresh` is set, and returns the user id.
 */
export async function signIn(page: Page, phoneOrEmail: string, isFresh = false): Promise<string> {
    // The bundle's hashed name only changes when the server restarts, so a rebuilt
    // bundle keeps being served from cache under the old name until this runs.
    await clearBrowserCache(page);
    await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
    await waitForAppReady(page);
    await dismissCookieConsent(page);
    await waitForDebugUI(page);
    const currentUserId = await getUserId(page);
    if (currentUserId && !currentUserId.startsWith('~')) {
        if (!isFresh)
            return currentUserId;

        await page.evaluate(() => (globalThis as DebugUIRoot).debugUI!.signOut());
        await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
        await waitForAppReady(page);
    }
    const userId = await page.evaluate(async (id: string) => {
        const debugUI = (globalThis as DebugUIRoot).debugUI;
        if (!debugUI)
            throw new Error('window.debugUI is missing - a production build, or the page is not ready.');

        // skipBubbles is left to the caller: DebugUI.SignIn waits a fixed 2s for the new
        // circuit's BubbleHost to register its JS ref, and loses that race often enough
        // to throw. skipOnboarding below clears bubbles from the JS side instead.
        await debugUI.signIn(id, { skipOnboarding: true, skipBubbles: false });
        return debugUI.getUserId();
    }, phoneOrEmail);
    await skipOnboarding(page);
    return userId;
}

/**
 * Waits for `debugUI.fake`, which appears only once Blazor has started - both the
 * DebugUI service and this surface are wired up after `whenBlazorReady`.
 */
export async function waitForDebugUI(page: Page, timeoutMs = 30_000): Promise<void> {
    await page.waitForFunction(
        () => !!(globalThis as DebugUIRoot).debugUI?.fake,
        undefined,
        { timeout: timeoutMs });
}

export function getUserId(page: Page): Promise<string | null> {
    return page.evaluate(() => {
        const debugUI = (globalThis as DebugUIRoot).debugUI;
        return debugUI ? debugUI.getUserId() : null;
    });
}

export async function closeSession(session: BrowserSession): Promise<void> {
    // The browser belongs to the developer, not to us - only the CDP link is dropped.
    await session.browser.close();
}
