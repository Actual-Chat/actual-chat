/**
 * E2E test: Cmd+Z (Ctrl+Z off Mac) in the message editor undoes without a stall and keeps working
 * press after press, in Chromium and in WebKit - the engine of Safari and the Apple apps, where the
 * editor's own execCommand calls raise beforeinput too. The WebKit pass is skipped when Playwright's
 * WebKit build isn't installed.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/message-editor-undo.test.ts --config vitest.config.e2e.ts
 */

import fs from 'fs';
import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import { webkit, type Browser, type BrowserContext, type Locator, type Page } from 'playwright';
import { connectBrowser, ensureSignedIn, openChat, screenshot } from './helpers';

type Engine = 'chromium' | 'webkit';

const engines: Engine[] = fs.existsSync(webkit.executablePath()) ? ['chromium', 'webkit'] : ['chromium'];
const maxUndoMs = 1_500;

describe.each(engines)('message editor undo (%s)', engine => {
    const shot = (name: string) => screenshot('e2e', `message-editor-undo-${engine}-${name}`);
    let browser: Browser;
    let context: BrowserContext;
    let ownsBrowser: boolean;
    let page: Page;
    let editor: Locator;

    const editorText = () => editor.evaluate(el =>
        (el as HTMLElement).innerText.replace(/[​\n]/g, '').replace(/ /g, ' '));
    const undo = async () => {
        const startedAt = performance.now();
        await page.keyboard.press('ControlOrMeta+z');
        const text = await editorText();
        return { text, elapsedMs: performance.now() - startedAt };
    };

    beforeAll(async () => {
        if (engine === 'webkit') {
            browser = await webkit.launch({ headless: true });
            context = await browser.newContext({ ignoreHTTPSErrors: true, locale: 'en-US' });
            ownsBrowser = true;
        }
        else {
            const conn = await connectBrowser();
            ({ browser, context, ownsBrowser } = conn);
        }
        page = await context.newPage();
        await ensureSignedIn(page);
        await openChat(page);
        editor = page.locator('#message-input .editor-content[contenteditable="true"]').first();

        // ChatMessageEditor restores per-chat drafts, so a previous run's unsent text would precede ours
        await editor.click({ force: true });
        await page.keyboard.press('ControlOrMeta+a');
        await page.keyboard.press('Backspace');
        await page.waitForTimeout(1_000);
        await editor.click({ force: true });
        await page.keyboard.type('hello', { delay: 60 });
        await page.waitForTimeout(800);
        await page.keyboard.type(' world', { delay: 60 });
        await page.waitForTimeout(800);
        for (let i = 0; i < 5; i++) {
            await page.keyboard.press('Backspace');
            await page.waitForTimeout(80);
        }
        await page.waitForTimeout(500);
    }, 180_000);

    afterAll(async () => {
        await editor.click({ force: true }).catch(() => { /* ignore */ });
        await page.keyboard.press('ControlOrMeta+a').catch(() => { /* ignore */ });
        await page.keyboard.press('Backspace').catch(() => { /* ignore */ });
        await page.close();
        if (ownsBrowser) {
            await context.close();
            await browser.close();
        }
    });

    it('brings deleted text back on the first press, without a stall', async () => {
        expect(await editorText()).toBe('hello ');

        const { text, elapsedMs } = await undo();
        await page.screenshot({ path: shot('first-undo') });

        expect(text.length, 'the first press must undo part of the deletion at least').toBeGreaterThan(6);
        expect('hello world'.startsWith(text), `"${text}" isn't a step back from "hello "`).toBe(true);
        expect(elapsedMs, 'the undo must not block the page').toBeLessThan(maxUndoMs);
    }, 30_000);

    // The first press used WebKit's only native undo step, so these reach the editor only via keydown
    it.runIf(engine === 'webkit')('keeps undoing on every next press', async () => {
        let text = await editorText();
        for (let i = 0; i < 4 && text !== 'hello world'; i++) {
            await page.waitForTimeout(500);
            const result = await undo();
            expect(result.text, 'every press must undo a step').not.toBe(text);
            expect(result.elapsedMs).toBeLessThan(maxUndoMs);
            text = result.text;
        }
        expect(text).toBe('hello world');

        await page.waitForTimeout(500);
        const { text: typedText, elapsedMs } = await undo();
        await page.screenshot({ path: shot('typing-undo') });

        expect('hello'.startsWith(typedText), 'past the deletion, undo must go on into the typing').toBe(true);
        expect(elapsedMs).toBeLessThan(maxUndoMs);
    }, 30_000);
});
