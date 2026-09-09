import { beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { build } from 'esbuild';
import type { Page } from 'playwright';
import { connectBrowser, type BrowserConnection } from './helpers';

declare const TestModules: {
    MutationProcessor: typeof import('../../../src/nodejs/src/mutation-processor').MutationProcessor;
    AnimationSync: typeof import('../../../src/nodejs/src/animation-sync').AnimationSync;
};

describe('MutationProcessor', () => {
    let connection: BrowserConnection;
    let page: Page;
    let bundle: string;

    beforeAll(async () => {
        const result = await build({
            stdin: {
                contents: `export { MutationProcessor } from './src/nodejs/src/mutation-processor';
                    export { AnimationSync } from './src/nodejs/src/animation-sync';`,
                resolveDir: process.cwd(),
            },
            bundle: true,
            write: false,
            format: 'iife',
            globalName: 'TestModules',
        });
        bundle = result.outputFiles[0].text;
        connection = await connectBrowser();
        page = await connection.context.newPage();

        return async () => {
            await page.close();
            if (connection.ownsBrowser)
                await connection.browser.close();
        };
    }, 30_000);

    beforeEach(async () => {
        await page.goto('about:blank');
        await page.addScriptTag({ content: bundle });
    });

    it('reads animation phases across nested and sibling additions before writing any', async () => {
        const result = await page.evaluate(async () => {
            const { MutationProcessor, AnimationSync } = TestModules;
            MutationProcessor.start();
            const reads: number[] = [];
            const getStyle = window.getComputedStyle;
            window.getComputedStyle = (element, pseudo) => {
                if (element.hasAttribute('data-test-animation'))
                    reads.push(document.querySelectorAll('[data-anim-synced]').length);

                return getStyle(element, pseudo);
            };
            const parent = document.createElement('div');
            parent.innerHTML = '<div data-anim-sync data-test-animation></div>';
            document.body.appendChild(parent);
            const child = document.createElement('div');
            child.innerHTML = '<svg data-anim-sync data-test-animation></svg>';
            parent.appendChild(child);
            const sibling = document.createElement('div');
            sibling.setAttribute('data-anim-sync', '200');
            sibling.setAttribute('data-test-animation', '');
            document.body.appendChild(sibling);
            await new Promise(resolve => setTimeout(resolve, 0));
            window.getComputedStyle = getStyle;

            return {
                reads,
                ticks: [...document.querySelectorAll('[data-test-animation]')]
                    .map(element => element.getAttribute(AnimationSync.syncedAttribute)),
            };
        });
        expect(result.reads.length).toBeGreaterThanOrEqual(3);
        expect(result.reads.every(count => count === 0)).toBe(true);
        expect(result.ticks).toEqual(['100', '100', '200']);
    });

    it('preserves render script order and same-value idempotence across overlapping additions', async () => {
        const calls = await page.evaluate(async () => {
            const processor = TestModules.MutationProcessor;
            const calls: string[] = [];
            for (const name of ['a', 'b'])
                processor.registerRenderScript(name, (element, value) => calls.push(`${element.id}:${name}:${value}`));

            processor.start();
            const existing = document.createElement('div');
            document.body.appendChild(existing);
            const child = document.createElement('div');
            child.id = 'child';
            child.setAttribute('data-render-script-a', '1');
            child.setAttribute('data-render-script-b', '1');
            existing.appendChild(child);
            const parent = document.createElement('div');
            parent.id = 'parent';
            parent.setAttribute('data-render-script-a', '1');
            parent.setAttribute('data-render-script-b', '1');
            parent.appendChild(child);
            document.body.appendChild(parent);
            await new Promise(resolve => setTimeout(resolve, 0));
            child.setAttribute('data-render-script-a', '1');
            child.setAttribute('data-render-script-b', '2');
            await new Promise(resolve => setTimeout(resolve, 0));

            return calls;
        });
        expect(calls).toEqual(['child:a:1', 'child:b:1', 'parent:a:1', 'parent:b:1', 'child:b:2']);
    });

    it('keeps explicit synchronization within a shadow root and excludes its host', async () => {
        const result = await page.evaluate(() => {
            const host = document.createElement('div');
            host.setAttribute('data-anim-sync', '');
            document.body.appendChild(host);
            const shadow = host.attachShadow({ mode: 'open' });
            shadow.innerHTML = '<svg><path data-anim-sync="200"></path></svg>';
            const first = TestModules.AnimationSync.syncAll(shadow);
            const second = TestModules.AnimationSync.syncAll(shadow);

            return {
                first,
                second,
                host: host.hasAttribute('data-anim-sync'),
                tick: shadow.querySelector('path')?.getAttribute('data-anim-synced'),
            };
        });
        expect(result).toEqual({ first: 1, second: 0, host: true, tick: '200' });
    });


    it('counts a tagged child toward every declaring ancestor and releases it on removal', async () => {
        const result = await page.evaluate(async () => {
            const processor = TestModules.MutationProcessor;
            processor.start();
            document.body.innerHTML = '<div id="outer" data-children="match"><div id="inner" data-children="match">'
                + '<span data-child="match"></span></div></div>';
            await new Promise(resolve => setTimeout(resolve, 0));
            const outer = document.getElementById('outer')!;
            const inner = document.getElementById('inner')!;
            const both = outer.hasAttribute('data-has-match') && inner.hasAttribute('data-has-match');
            inner.firstElementChild!.remove();
            await new Promise(resolve => setTimeout(resolve, 0));

            return { both, remaining: document.querySelectorAll('[data-has-match]').length };
        });
        expect(result).toEqual({ both: true, remaining: 0 });
    });

    it('keeps the marker while any tagged child remains', async () => {
        const result = await page.evaluate(async () => {
            const processor = TestModules.MutationProcessor;
            processor.start();
            document.body.innerHTML = '<div id="box" data-children="match">'
                + '<span id="a" data-child="match"></span><span id="b" data-child="match"></span></div>';
            await new Promise(resolve => setTimeout(resolve, 0));
            const box = document.getElementById('box')!;
            const states = [box.hasAttribute('data-has-match')];
            document.getElementById('a')!.remove();
            await new Promise(resolve => setTimeout(resolve, 0));
            states.push(box.hasAttribute('data-has-match'));
            document.getElementById('b')!.remove();
            await new Promise(resolve => setTimeout(resolve, 0));
            states.push(box.hasAttribute('data-has-match'));

            return states;
        });
        expect(result).toEqual([true, true, false]);
    });

    it('re-binds a child when its data-child attribute changes', async () => {
        const result = await page.evaluate(async () => {
            const processor = TestModules.MutationProcessor;
            processor.start();
            document.body.innerHTML = '<div id="box" data-children="one two"><span id="c" data-child="one"></span></div>';
            await new Promise(resolve => setTimeout(resolve, 0));
            const box = document.getElementById('box')!;
            const before = { one: box.hasAttribute('data-has-one'), two: box.hasAttribute('data-has-two') };
            document.getElementById('c')!.setAttribute('data-child', 'two');
            await new Promise(resolve => setTimeout(resolve, 0));

            return { before, after: { one: box.hasAttribute('data-has-one'), two: box.hasAttribute('data-has-two') } };
        });
        expect(result).toEqual({ before: { one: true, two: false }, after: { one: false, two: true } });
    });

    it('processes DOM and script attributes created by a render script during presence updates', async () => {
        const result = await page.evaluate(async () => {
            const processor = TestModules.MutationProcessor;
            const calls: string[] = [];
            processor.registerRenderScript('first', () => {
                calls.push('first');
                const late = document.createElement('div');
                late.setAttribute('data-children', 'match');
                late.setAttribute('data-render-script-second', '1');
                late.innerHTML = '<span data-child="match"></span>';
                document.body.appendChild(late);
            });
            processor.registerRenderScript('second', () => calls.push('second'));
            document.body.innerHTML = '<div data-children="match" id="initial"></div>';
            processor.start();
            document.getElementById('initial')!.innerHTML = '<div data-child="match" data-render-script-first="1"></div>';
            await new Promise(resolve => setTimeout(resolve, 0));

            return { calls, containers: document.querySelectorAll('[data-has-match]').length };
        });
        expect(result).toEqual({ calls: ['first', 'second'], containers: 2 });
    });
});
