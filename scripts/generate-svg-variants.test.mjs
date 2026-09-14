import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { generateSvgVariants, readThemeBackgrounds, remapSvg } from './generate-svg-variants.mjs';

const source = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
  <metadata><palette xmlns="urn:voxt:svg-themes" version="1">
    <color role="outline" light="#aabbcc" dark="#42424d"/>
    <color role="highlight" light="#ffffff" dark="#b6b6cc"/>
    <color role="eye-glint" light="#ffffff" dark="#ffffff" part="eye"/>
  </palette></metadata>
  <defs><linearGradient id="abc"><stop stop-color="#ABC"/>
    <stop offset="1" style="stop-color: #fff; stop-opacity: .5"/>
  </linearGradient></defs>
  <style>#abc { fill: #abc; animation: twitch 5s infinite; }
    @keyframes twitch { 50% { transform: rotate(2deg); } }</style>
  <g id="cat--ear" data-part="ear" data-pivot="10 20" transform="translate(2 3)">
    <path d="M0 0L10 20Z" fill="url(#abc)" stroke="#abc"/>
    <animateTransform attributeName="transform" type="rotate" values="0;2;0" dur="5s"/>
  </g>
  <g data-part="eye"><circle fill="#fff" cx="20" cy="30" r="2"/></g>
  <path fill="#fff" d="M30 40h5v5z"/>
</svg>`;

test('remaps paint and gradient stops without changing geometry or animation references', () => {
    const dark = remapSvg(source);
    assert.match(dark, /stroke="#42424d"/);
    assert.match(dark, /stop-color="#42424d"/);
    assert.match(dark, /stop-color: #b6b6cc; stop-opacity: .5/);
    assert.match(dark, /#abc \{ fill: #42424d; animation: twitch 5s infinite;/);
    assert.match(dark, /fill="url\(#abc\)"/);
    assert.match(dark, /id="cat--ear" data-part="ear" data-pivot="10 20" transform="translate\(2 3\)"/);
    assert.match(dark, /d="M0 0L10 20Z"/);
    assert.match(dark, /values="0;2;0" dur="5s"/);
    assert.match(dark, /transform: rotate\(2deg\)/);
    assert.doesNotMatch(dark, /<palette/);
});

test('applies a part override without recoloring the same paint elsewhere', () => {
    const dark = remapSvg(source);
    assert.match(dark, /data-part="eye"><circle fill="#ffffff"/);
    assert.match(dark, /<path fill="#b6b6cc" d="M30 40h5v5z"/);
});

test('rejects incomplete palettes instead of silently leaving light paint in dark output', () => {
    assert.throws(() => remapSvg(source.replace('stroke="#abc"', 'stroke="#123456"')), /Unmapped paint.*#123456/);
});

test('rejects ambiguous or incompatible palette metadata', () => {
    assert.throws(() => remapSvg(source.replace('version="1"', 'version="2"')), /palette version/);
    assert.throws(
        () => remapSvg(source.replace('</palette>', '<color light="#aabbcc" dark="#000000"/></palette>')),
        /Duplicate palette rule/
    );
    assert.throws(() => remapSvg('<svg xmlns="http://www.w3.org/2000/svg"/>'), /palette metadata/);
});

test('can override gradient paint by ID without modifying its consumers', () => {
    const scoped = source.replace('</palette>', '<color light="#aabbcc" dark="#303038" target="abc"/></palette>');
    const dark = remapSvg(scoped);
    assert.match(dark, /<stop stop-color="#303038"/);
    assert.match(dark, /stroke="#42424d"/);
    assert.match(dark, /fill="url\(#abc\)"/);
});

const fixtureRoot = fileURLToPath(new URL('../tmp/', import.meta.url));

test('resolves theme backgrounds through the app variable cascade', () => {
    const colors =
        ':root { --white: #fff; --surface: #28282e; }' +
        ':root, .theme-light { --background-01: var(--white); }' +
        '.theme-dark { --background-01: var(--surface); }' +
        '.theme-dark { --surface: #303038; }';
    assert.deepEqual(readThemeBackgrounds(colors), { light: '#ffffff', dark: '#303038' });
    assert.throws(
        () => readThemeBackgrounds(colors + '.theme-dark { --surface: var(--background-01); }'),
        /Cyclic theme color/
    );
});

test('generates standalone pairs and detects stale output without writing in check mode', async context => {
    const directory = await mkdtemp(path.join(fixtureRoot, 'svg-variants-'));
    context.after(async () => {
        assert.ok(path.resolve(directory).startsWith(path.resolve(fixtureRoot) + path.sep));
        await rm(directory, { recursive: true });
    });
    await writeFile(path.join(directory, 'sample.svg'), source);
    await assert.rejects(generateSvgVariants({ directory, check: true }), /Stale SVG outputs/);
    assert.deepEqual(await generateSvgVariants({ directory }), { assets: 1, changed: 2 });
    const preview = await readFile(path.join(directory, 'index.html'), 'utf8');
    assert.match(preview, /src="sample.svg"/);
    assert.match(preview, /src="sample-dark.svg"/);
    assert.deepEqual(await generateSvgVariants({ directory, check: true }), { assets: 1, changed: 0 });
    assert.equal(await readFile(path.join(directory, 'sample.svg'), 'utf8'), source);
    await writeFile(path.join(directory, 'sample-dark.svg'), 'stale');
    await assert.rejects(generateSvgVariants({ directory, check: true }), /Stale SVG outputs/);
    assert.equal(await readFile(path.join(directory, 'sample-dark.svg'), 'utf8'), 'stale');
});

test('rejects a misspelled scope instead of silently skipping its override', () => {
    assert.throws(() => remapSvg(source.replace('part="eye"/>', 'part="missing-eye"/>')), /Unknown palette scope/);
});
