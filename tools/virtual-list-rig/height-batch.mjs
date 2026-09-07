import fs from 'node:fs';
import { chromium } from 'playwright';

const port = Number(process.argv[2] ?? 9333);
const browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`);
try {
    const pages = browser.contexts().flatMap(context => context.pages());
    const page = pages.find(page => /https:\/\/(local\.voxt\.ai|0\.0\.0\.1)\//.test(page.url()));
    if (!page)
        throw new Error('Open a Voxt chat and scroll into history before running this test.');

    const result = await page.evaluate(async () => {
        const list = [...globalThis.InfiniteList.instances].find(list =>
            !list.ref.closest('.c-layer.outgoing') && list.ref.classList.contains('chat-view'));
        const view = list.ref.getBoundingClientRect();
        const center = (view.top + view.bottom) / 2;
        const watched = list.items.find(item => {
            const rect = item.ref.getBoundingClientRect();
            return rect.top <= center && rect.bottom > center
                && getComputedStyle(item.ref).position !== 'sticky';
        });
        const above = list.items.filter(item => {
            const state = list.heights.states.get(item.key);
            return state?.isControlled && item.ref.getBoundingClientRect().bottom < view.top - 100
                && getComputedStyle(item.ref).position !== 'sticky';
        }).slice(0, 2);
        if (!watched || above.length !== 2 || list.pinnedEdge !== null)
            throw new Error('Need an unpinned list with two controlled items above the viewport.');

        const states = above.map(item => list.heights.states.get(item.key));
        const originalPadding = states.map(state => state.contentRef.style.paddingBottom);
        const padding = states.map(state => parseFloat(getComputedStyle(state.contentRef).paddingBottom) || 0);
        const samples = [];
        const started = performance.now();
        const sample = phase => {
            const index = list.indexByKey.get(watched.key);
            const offset = list.items.slice(0, index).reduce((sum, item) => sum + item.height + list.rowGap, 0);
            samples.push({
                phase,
                time: performance.now() - started,
                position: watched.ref.getBoundingClientRect().top + list.ref.scrollTop,
                offsetError: offset - list.offsets[index],
                heights: states.map(state => state.applied),
            });
        };
        const record = phase => new Promise(resolve => {
            const until = performance.now() + 300;
            const tick = () => {
                sample(phase);
                if (performance.now() < until)
                    requestAnimationFrame(tick);
                else
                    resolve();
            };
            requestAnimationFrame(tick);
        });
        try {
            sample('before');
            states[0].contentRef.style.paddingBottom = `${padding[0] + 20}px`;
            states[1].contentRef.style.paddingBottom = `${padding[1] + 92}px`;
            await record('growth');
            states.forEach((state, index) => state.contentRef.style.paddingBottom = originalPadding[index]);
            await record('shrink');
        }
        finally {
            states.forEach((state, index) => state.contentRef.style.paddingBottom = originalPadding[index]);
        }
        return { watched: watched.key, changed: above.map(item => item.key), samples };
    });
    const before = result.samples[0];
    const growth = result.samples.filter(sample => sample.phase === 'growth').at(-1);
    const deltas = growth.heights.map((height, index) => height - before.heights[index]);
    const maxDisplacement = Math.max(...result.samples.map(sample => Math.abs(sample.position - before.position)));
    const maxOffsetError = Math.max(...result.samples.map(sample => Math.abs(sample.offsetError)));
    fs.mkdirSync('tmp/traces', { recursive: true });
    fs.writeFileSync('tmp/traces/height-batch.json', JSON.stringify(result, null, 2));
    console.log({ watched: result.watched, changed: result.changed, deltas, maxDisplacement, maxOffsetError });
    if (deltas.some((delta, index) => Math.abs(delta - [20, 92][index]) > 1))
        throw new Error('The real ResizeObserver did not apply both requested height changes.');

    if (maxDisplacement > 1 || maxOffsetError > 1)
        throw new Error('Offscreen height changes displaced the visible item or left stale offsets.');
}
finally {
    await browser.close();
}
