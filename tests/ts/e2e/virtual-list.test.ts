import { beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { build } from 'esbuild';
import type { Page } from 'playwright';
import { connectBrowser, type BrowserConnection } from './helpers';

declare const ListTest: typeof import('./virtual-list-fixture');

describe('InfiniteList navigation', () => {
    let connection: BrowserConnection;
    let page: Page;
    let bundle: string;

    beforeAll(async () => {
        const result = await build({
            entryPoints: ['tests/ts/e2e/virtual-list-fixture.ts'],
            bundle: true,
            write: false,
            format: 'iife',
            globalName: 'ListTest',
            plugins: [{
                name: 'test-host',
                setup(builder) {
                    builder.onResolve({ filter: /\/browser-info$/ }, () => ({
                        path: 'browser-info', namespace: 'test-host',
                    }));
                    builder.onLoad({ filter: /.*/, namespace: 'test-host' }, () => ({
                        contents: "export const BrowserInfo = { hostKind: 'Server' };",
                    }));
                },
            }],
        });
        bundle = result.outputFiles[0].text;
        connection = await connectBrowser();
        page = await connection.context.newPage();
        page.on('pageerror', error => console.error(error.message));
        await page.route('http://virtual-list.test/', route => route.fulfill({
            contentType: 'text/html', body: '<!doctype html><html><body></body></html>',
        }));
        return async () => {
            await page.close();
            if (connection.ownsBrowser)
                await connection.browser.close();
        };
    }, 30_000);

    beforeEach(async () => {
        await page.goto('http://virtual-list.test/');
        await page.addScriptTag({ content: bundle });
    });

    it('should keep the latest item in place after an older pending navigation settles', async () => {
        // arrange
        const result = await page.evaluate(async () => {
            const fixture = ListTest.createList();
            await ListTest.nextFrame();
            const targets: string[] = [];
            const scrollToItem = fixture.list['scrollToItem'].bind(fixture.list) as
                (item: HTMLElement, position: ScrollLogicalPosition, isSmooth: boolean) => void;
            fixture.list['scrollToItem'] = (item, position, isSmooth) => {
                targets.push(item.dataset.key!);
                scrollToItem(item, position, isSmooth);
            };
            fixture.list['stability'].holdAnimation('delayed-height', 300);
            fixture.navigate('4');
            fixture.navigate('30');
            const positions: number[] = [];

            // act
            const until = performance.now() + 600;
            while (performance.now() < until) {
                await ListTest.nextFrame();
                positions.push(fixture.position('30'));
            }
            fixture.list.dispose();
            return { positions, targets };
        });

        // assert
        expect(result.targets).toEqual([]);
        expect(result.positions.length).toBeGreaterThan(3);
        expect(Math.max(...result.positions) - Math.min(...result.positions)).toBeLessThanOrEqual(1);
    });

    it.each([0, 150])('should cancel pending navigation on user scroll after %d ms', async delayMs => {
        // arrange
        await page.mouse.move(200, 300);
        await page.evaluate(async delayMs => {
            const fixture = ListTest.createList();
            fixture.navigate('15');
            if (delayMs > 0)
                await new Promise(resolve => setTimeout(resolve, delayMs));
            fixture.list['stability'].holdAnimation('delayed-height', 1000);
            fixture.navigate('4');
        }, delayMs);

        // act
        await page.mouse.wheel(0, 400);
        await page.waitForTimeout(150);
        const before = await page.locator('[data-key="15"]').evaluate(e => e.getBoundingClientRect().top);
        await page.waitForTimeout(1100);
        const after = await page.locator('[data-key="15"]').evaluate(e => e.getBoundingClientRect().top);

        // assert
        expect(Math.abs(after - before)).toBeLessThanOrEqual(1);
    });

    it('should retain pending navigation across a programmatic scroll correction', async () => {
        // arrange
        const target = await page.evaluate(async () => {
            const fixture = ListTest.createList();
            fixture.navigate('15');
            await ListTest.nextFrame();
            fixture.list['stability'].holdAnimation('delayed-height', 300);
            fixture.navigate('4');

            // act
            fixture.list['setScrollOffset'](fixture.list['scrollOffset'] + 100);
            await ListTest.nextFrame();
            const pending = fixture.list['pendingJump'];
            fixture.list.dispose();
            return pending?.target;
        });

        // assert
        expect(target).toMatchObject({ kind: 'key', key: '4' });
    });

    it('should retain pending navigation across a resize clamp', async () => {
        // arrange
        const result = await page.evaluate(async () => {
            const fixture = ListTest.createList();
            fixture.navigate('1');
            await new Promise(resolve => setTimeout(resolve, 150));
            fixture.list['stability'].holdAnimation('delayed-height', 700);
            fixture.navigate('4');
            const before = fixture.root.scrollTop;

            // act
            fixture.root.style.height = '700px';
            await new Promise(resolve => setTimeout(resolve, 100));
            const result = { delta: fixture.root.scrollTop - before, target: fixture.list['pendingJump']?.target };
            fixture.list.dispose();
            return result;
        });

        // assert
        expect(result.delta).toBeGreaterThan(90);
        expect(result.target).toMatchObject({ kind: 'key', key: '4' });
    });

    it('should execute a newer navigation that replaces a waiting one', async () => {
        // arrange
        const result = await page.evaluate(async () => {
            const fixture = ListTest.createList();
            fixture.list['stability'].holdAnimation('delayed-height', 300);
            fixture.navigate('4');

            // act
            fixture.navigate('15');
            await new Promise(resolve => setTimeout(resolve, 450));
            const top = fixture.position('15') - fixture.root.getBoundingClientRect().top;
            fixture.list.dispose();
            return top;
        });

        // assert
        expect(result).toBeGreaterThan(0);
        expect(result).toBeLessThan(600);
    });
});
