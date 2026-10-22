/**
 * E2E test: the content tabs of the chat's right panel (Media, Files, Links) show skeletons while
 * their lists load. The skeletons live in the list's spacers, around a chain placed mid-way into a
 * 4M px scroll space - so the list has to scroll there before its first item arrives.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/right-panel-tab-skeletons.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, beforeAll, afterAll, expect } from 'vitest';
import type { Page, WebSocketRoute } from 'playwright';
import {
    connectBrowser, ensureSignedIn, isVisibleWithin, openChat, screenshot, type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e-tab-skeletons', name);

/** Holds the server's render batches on the Blazor circuit from the first one that matches, while
 *  everything else on it goes through - which is what a slow data load looks like to the page. */
class ServerRenderHold {
    private readonly _held: { route: WebSocketRoute; message: string | Buffer }[] = [];
    private _mustHold: ((message: string | Buffer) => boolean) | null = null;

    public async attach(page: Page): Promise<void> {
        await page.routeWebSocket(/\/_blazor/, route => {
            const server = route.connectToServer();
            server.onMessage(message => {
                const isHeld = this._mustHold != null
                    && message.includes('JS.RenderBatch')
                    && (this._held.length > 0 || this._mustHold(message));
                if (isHeld)
                    this._held.push({ route, message });
                else
                    route.send(message);
            });
        });
    }

    public holdFrom(mustHold: (message: string | Buffer) => boolean): void {
        this._mustHold = mustHold;
    }

    public get heldCount(): number {
        return this._held.length;
    }

    public release(): void {
        this._mustHold = null;
        for (const { route, message } of this._held.splice(0))
            route.send(message);
    }
}

interface TabCase {
    name: string;
    title: string;
    /** Matches the first render batch carrying the tab's data, never the one with its skeletons */
    dataPattern: RegExp;
    skeleton: string;
    /** The list's JS side is attached, which is when its skeletons used to go off screen */
    readySelector: string;
    loaded: string;
}

const listReady = (cls: string) => `.chat-side-panel .${cls}[data-is-empty] > .c-wrapper[style*="height"]`;

// Only the content lists: they render their skeletons first and fetch their rows in a later batch,
// every time. Members, Threads and Call compute their content in one go, which renders in the same
// batch as the tab once the server has it cached - leaving no loading to hold.
const tabs: TabCase[] = [
    {
        name: 'media',
        title: 'Media',
        dataPattern: /c-group-header|No media yet/,
        skeleton: '.content-list-grid .c-media-skeleton > div',
        readySelector: listReady('content-list-grid'),
        loaded: '.chat-side-panel .content-list-grid li.item',
    },
    {
        name: 'files',
        title: 'Files',
        dataPattern: /c-group-header|No files yet/,
        skeleton: '.content-list-rows .c-file-skeleton',
        readySelector: listReady('content-list-rows'),
        loaded: '.chat-side-panel .content-list-rows li.item',
    },
    {
        name: 'links',
        title: 'Links',
        dataPattern: /c-group-header|No links yet/,
        skeleton: '.content-list-links .c-link-skeleton',
        readySelector: listReady('content-list-links'),
        loaded: '.chat-side-panel .content-list-links li.item',
    },
];

describe('right panel content tab skeletons', () => {
    let conn: BrowserConnection;
    let page: Page;
    const hold = new ServerRenderHold();

    beforeAll(async () => {
        conn = await connectBrowser();
        page = await conn.context.newPage();
        await hold.attach(page);
        await ensureSignedIn(page);
        await openChat(page);
        // The toggle is there only while the panel is closed - and the panel stays open across page loads
        const panelToggle = page.locator('button.btn-h:has(i.icon-layout)').first();
        if (await isVisibleWithin(panelToggle, 3_000))
            await panelToggle.evaluate(el => (el as HTMLElement).click());
        // A closed panel is still rendered, just off screen
        await page.waitForFunction(() => {
            const rect = document.querySelector('.chat-side-panel .tab-panel-tabs')?.getBoundingClientRect();
            return rect != null && rect.width > 0 && rect.left >= 0 && rect.right <= window.innerWidth + 1;
        }, undefined, { timeout: 15_000 });
        // Start away from the tabs under test, whichever one the panel remembers
        await page.locator('.chat-side-panel .btn-tab:has-text("Members")').first()
            .evaluate(el => (el as HTMLElement).click());
        await page.locator('.chat-side-panel .author-list-tab .avatar-badge').first()
            .waitFor({ state: 'visible', timeout: 15_000 });
    }, 120_000);

    afterAll(async () => {
        hold.release();
        await page.close();
        if (conn.ownsBrowser) {
            await conn.context.close();
            await conn.browser.close();
        }
    });

    it.each(tabs)('shows skeletons on the $title tab until its content arrives', async tab => {
        // arrange
        const tabButton = page.locator(`.chat-side-panel .btn-tab:has-text("${tab.title}")`).first();
        await tabButton.waitFor({ state: 'attached', timeout: 15_000 });
        hold.holdFrom(message => tab.dataPattern.test(message.toString()));

        // act - open the tab and look at it while it waits for its data
        await tabButton.evaluate(el => (el as HTMLElement).click());
        await page.locator(tab.readySelector).first().waitFor({ state: 'attached', timeout: 15_000 });
        // The tab swap waits for the content, which a loading tab doesn't have: its display-delay
        // backstop ends the wait, but that arrives as a render batch held behind the data. Visually the
        // backstop only drops the hold attribute, so do that here.
        await page.waitForTimeout(500);
        await page.evaluate(() => document
            .querySelectorAll('.chat-side-panel .content-swap[data-swap-hold]')
            .forEach(host => host.removeAttribute('data-swap-hold')));
        await page.waitForTimeout(1_000);
        const heldCount = hold.heldCount;
        await page.screenshot({ path: shot(`${tab.name}-loading`) });
        const visibleSkeletons = await page.evaluate(impl, tab.skeleton);
        hold.release();
        await page.locator(tab.loaded).first().waitFor({ state: 'visible', timeout: 30_000 });
        await page.waitForTimeout(500);
        await page.screenshot({ path: shot(`${tab.name}-loaded`) });

        // assert
        expect(heldCount).toBeGreaterThan(0);
        expect(visibleSkeletons).toBeGreaterThan(0);

        /** How many of the skeletons the user sees: the topmost element at each one's centre is the
         *  skeleton itself, past clipping, scrolling and the tab swap. Runs in the page. */
        function impl(selector: string): number {
            return Array.from(document.querySelectorAll<HTMLElement>(`.chat-side-panel ${selector}`))
                .filter(skeleton => {
                    const rect = skeleton.getBoundingClientRect();
                    if (rect.height <= 0)
                        return false;

                    const x = rect.left + rect.width / 2;
                    const hit = document.elementFromPoint(x, rect.top + rect.height / 2);
                    return hit != null && skeleton.contains(hit);
                })
                .length;
        }
    }, 60_000);
});
