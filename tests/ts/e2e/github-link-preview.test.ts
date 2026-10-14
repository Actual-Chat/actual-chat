/**
 * E2E test: a GitHub pull request, issue, commit or repository link renders as a card built from
 * GitHub's API record, not from the page's Open Graph image (#5291).
 *
 * Prerequisites:
 * - Server running (server-loop / run-watch), with internet access: the server asks api.github.com.
 *   Without MediaSettings:GithubApiKey the requests are anonymous, which public repositories allow.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/github-link-preview.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { Page } from 'playwright';
import {
    clearBrowserCache, connectBrowser, ensureSignedIn, openChat, postMessage, screenshot,
    type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e', name);

// A preview is kept per URL, so every link is a new one on each run: the server then has to build its card
const RUN_ID = Date.now();
const GITHUB_LINKS = [
    {
        name: 'pull-request',
        url: `https://github.com/dotnet/runtime/pull/1?t=${RUN_ID}`,
        repo: 'dotnet/runtime',
        title: 'WIP: repo consolidation scouting kick-off - make clr build locally on Windows',
        number: '#1',
        chips: ['master ← '],
        author: 'trylek',
    },
    {
        name: 'issue',
        url: `https://github.com/dotnet/runtime/issues/3?t=${RUN_ID}`,
        repo: 'dotnet/runtime',
        title: 'Define a root README.md',
        number: '#3',
        chips: ['in-pr'],
        author: 'ViktorHofer',
    },
    {
        name: 'commit',
        url: `https://github.com/dotnet/runtime/commit/8858705338c9ca66897e84632a08738c66b4985d?t=${RUN_ID}`,
        repo: 'dotnet/runtime',
        title: '[runtime] Arm64: Add AddAcross Codegen and Tests. (#1093)',
        number: null,
        chips: ['8858705'],
        author: 'TamarChristinaArm',
    },
    {
        name: 'repository',
        url: `https://github.com/dotnet/runtime?t=${RUN_ID}`,
        repo: null,
        title: 'dotnet/runtime',
        number: null,
        chips: ['C#', 'main'],
        author: 'dotnet',
    },
];

describe('github link preview', () => {
    let conn: BrowserConnection;
    let page: Page;
    const messages = new Map<string, string>();

    beforeAll(async () => {
        conn = await connectBrowser();
        page = await conn.context.newPage();
        await clearBrowserCache(page);
        await ensureSignedIn(page);
    }, 120_000);

    afterAll(async () => {
        await page.close();
        if (conn.ownsBrowser) {
            await conn.context.close();
            await conn.browser.close();
        }
    });

    for (const link of GITHUB_LINKS) {
        it(`shows a card for a ${link.name} link`, async () => {
            // arrange
            await openChat(page);
            const text = `GitHub ${link.name} ${RUN_ID}`;
            messages.set(link.name, text);

            // act
            const message = await postMessage(page, `${text} ${link.url}`, text);

            // assert
            const card = message.locator('.github-link-preview').first();
            await card.waitFor({ state: 'visible', timeout: 60_000 });
            expect(await card.getAttribute('href')).toBe(link.url);
            expect(await card.getAttribute('class')).toContain(link.name);
            expect((await card.locator('.c-title').innerText()).replace(/\s+/g, ' ').trim()).toContain(link.title);
            if (link.number != null)
                expect((await card.locator('.c-number').innerText()).trim()).toBe(link.number);
            else
                expect(await card.locator('.c-number').count()).toBe(0);
            if (link.repo != null)
                expect((await card.locator('.c-repo').innerText()).trim()).toBe(link.repo);
            else
                expect(await card.locator('.c-repo').count()).toBe(0);
            const facts = (await card.locator('.c-facts').innerText()).replace(/\s+/g, ' ');
            for (const chip of link.chips)
                expect(facts).toContain(chip);
            expect(await card.locator('.c-by').innerText()).toContain(link.author);
            expect((await card.locator('.c-domain').innerText()).trim()).toBe('github.com');
            const avatar = card.locator('.c-icon image-skeleton.c-avatar');
            await avatar.waitFor({ state: 'visible', timeout: 30_000 });
            await expect.poll(
                () => avatar.evaluate(el => {
                    const img = el.querySelector<HTMLImageElement>('img.image');
                    return img?.complete && img.naturalWidth > 0;
                }),
                { timeout: 30_000 },
            ).toBe(true);
            await expect.poll(async () => (await card.locator('.c-icon .c-badge').boundingBox())?.width ?? 0)
                .toBeGreaterThan(0);

            await card.screenshot({ path: shot(`github-link-${link.name}-wide`) });
        }, 120_000);
    }

    it('shows all four cards on a wide screen', async () => {
        // act
        await openChat(page);

        // assert
        await page.locator('.github-link-preview').last().waitFor({ state: 'visible', timeout: 30_000 });
        await page.screenshot({ path: shot('github-link-wide') });
    }, 60_000);

    it('fits the cards into a narrow screen', async () => {
        // arrange
        const viewportToRestore = page.viewportSize();
        await page.setViewportSize({ width: 390, height: 844 });
        try {
            // act
            await openChat(page);

            // assert
            for (const link of GITHUB_LINKS) {
                const text = messages.get(link.name)!;
                const message = page.locator(`.message-wrapper:has(.chat-message-markup:has-text("${text}"))`).first();
                await message.scrollIntoViewIfNeeded();
                const card = message.locator('.github-link-preview').first();
                await card.waitFor({ state: 'visible', timeout: 30_000 });
                // The list re-lays out after the resize, so the card can drop out between the wait and the read
                await expect.poll(
                    async () => (await card.boundingBox())?.width ?? 0,
                    { timeout: 15_000 },
                ).toBeGreaterThan(0);
                const box = (await card.boundingBox())!;
                expect(box.x + box.width).toBeLessThanOrEqual(390);
                await card.screenshot({ path: shot(`github-link-${link.name}-narrow`) });
            }
            await page.screenshot({ path: shot('github-link-narrow') });
        } finally {
            if (viewportToRestore)
                await page.setViewportSize(viewportToRestore);
        }
    }, 120_000);
});
