// Rasterizes the iOS share-extension cat PNGs from the themed kitties SVGs.
// UIKit can't render SVG, so the extension bundles PNGs (see resources/images/converted +
// each project's *.imageset). error-cat is a padded square canvas, so it is cropped to its
// content bounding box; share-cat keeps its authored 210x200. @1x heights match the retired
// art, so the .imageset dimensions and the ScaleAspectFit layout stay the same.
// Run: node scripts/generate-ios-cat-pngs.mjs  (Playwright's Chromium must be installed)
import { chromium } from 'playwright';
import fs from 'fs';
import path from 'path';

const KIT = 'src/nodejs/images/kitties';
const OUT = 'resources/images/converted';

const jobs = [
    { out: 'error-cat-light', svg: 'error-cat.svg',      crop: true,  heights: [169, 338, 507] },
    { out: 'error-cat-dark',  svg: 'error-cat-dark.svg', crop: true,  heights: [169, 338, 507] },
    { out: 'share-cat-light', svg: 'share-cat.svg',      crop: false, sizes: [[210, 200], [420, 400], [630, 600]] },
    { out: 'share-cat-dark',  svg: 'share-cat-dark.svg', crop: false, sizes: [[210, 200], [420, 400], [630, 600]] },
];

fs.mkdirSync(OUT, { recursive: true });
const browser = await chromium.launch();
const ctx = await browser.newContext({ reducedMotion: 'reduce', deviceScaleFactor: 1 });
const page = await ctx.newPage();

for (const job of jobs) {
    const markup = fs.readFileSync(path.join(KIT, job.svg), 'utf8');
    const viewBox = (markup.match(/viewBox="([^"]+)"/)[1]).split(/\s+/).map(Number);
    await page.setContent(`<body style="margin:0;background:transparent"><div id="wrap" style="display:inline-block">${markup}</div></body>`);
    await page.waitForTimeout(100);

    let box = viewBox;
    if (job.crop) {
        const b = await page.evaluate(() => { const g = document.querySelector('#wrap svg').getBBox(); return [g.x, g.y, g.width, g.height]; });
        const pad = Math.max(b[2], b[3]) * 0.02;
        box = [b[0] - pad, b[1] - pad, b[2] + 2 * pad, b[3] + 2 * pad];
    }
    const sizes = job.heights
        ? job.heights.map(h => [Math.round(h * box[2] / box[3]), h])
        : job.sizes;

    for (let i = 0; i < 3; i++) {
        const [w, h] = sizes[i];
        await page.evaluate(({ w, h, vb }) => {
            const s = document.querySelector('#wrap svg');
            s.setAttribute('viewBox', vb.join(' '));
            s.setAttribute('preserveAspectRatio', 'xMidYMid meet');
            s.setAttribute('width', w);
            s.setAttribute('height', h);
            s.style.display = 'block';
        }, { w, h, vb: box });
        const suffix = i === 0 ? '' : `@${i + 1}x`;
        const dst = `${OUT}/${job.out}${suffix}.png`;
        await (await page.$('#wrap svg')).screenshot({ path: dst, omitBackground: true });
        process.stdout.write(`${dst} (${w}x${h}) `);
    }
    console.log();
}
await browser.close();
console.log('done');
