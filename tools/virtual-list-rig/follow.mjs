// The follow's write path, measured against the one it replaced. docs/virtual-list.md §4.7 records,
// from a device, that driving the scroll position from JavaScript at frame rate is visibly jittery on
// Android - and the pinned-edge follow writes `scrollTop` on every frame the edge moves. This drives
// the same correction, 2px per frame, as a scroll and then as a transform, and records the only honest
// measure of what the user sees: a real item's position on screen, sampled every frame (§5).
//
// Chrome shows no difference. The measurement that matters is this one against a phone - attach to the
// device over CDP (see the virtual-list-debug skill) and pass its port.
//
//   node tools/virtual-list-rig/follow.mjs [port=9223] [seconds=6]
import { createRequire } from 'node:module';
const require = createRequire(new URL('../../package.json', import.meta.url));
const WebSocket = require('ws');
const PORT = Number(process.argv[2] || 9223);
const SECONDS = Number(process.argv[3] || 6);

const tabs = await (await fetch(`http://localhost:${PORT}/json/list`)).json();
const target = tabs.find(x => x.type === 'page' && (x.url || '').includes('local.voxt.ai'));
if (!target) { console.log('no voxt page on chrome', PORT); process.exit(1); }
const ws = new WebSocket(target.webSocketDebuggerUrl, { perMessageDeflate: false, maxPayload: 256 * 1024 * 1024 });
let id = 0; const pending = new Map();
const send = (m, p = {}) => new Promise((res, rej) => { const i = ++id; pending.set(i, { res, rej }); ws.send(JSON.stringify({ id: i, method: m, params: p })); });
ws.on('message', d => { const m = JSON.parse(d.toString()); if (m.id && pending.has(m.id)) { const h = pending.get(m.id); pending.delete(m.id); m.error ? h.rej(new Error(JSON.stringify(m.error))) : h.res(m.result); } });
await new Promise(r => ws.on('open', r));
await send('Runtime.enable');
const ev = async (expression, awaitPromise = false) => {
    const r = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise, timeout: 180000 });
    if (r.exceptionDetails) throw new Error('PAGE ' + JSON.stringify(r.exceptionDetails).slice(0, 400));
    return r.result.value;
};

// Parked mid-band so neither path runs into a limit; the item being watched is re-resolved by key,
// because a correction this long outlives the window the loader keeps.
const drive = (mode, seconds) => `new Promise(resolve => {
    const list = document.querySelector('.virtual-list.infinite-list');
    const instance = [...globalThis.InfiniteList.instances].find(x => x.ref === list);
    const container = instance.containerRef;
    const step = 2;
    const rows = [];
    const limits = instance.scrollController.getEffectiveScrollLimits();
    list.scrollTop = (limits.min + limits.max) / 2;
    const startTop = list.scrollTop;
    let transform = 0;
    let key = null;
    const started = performance.now();
    const tick = time => {
        const viewTop = list.getBoundingClientRect().top;
        if (key == null || !instance.indexByKey.has(key)) {
            const items = [...list.querySelectorAll('.item[data-key]')];
            const watched = items.find(x => x.getBoundingClientRect().top > viewTop + 150);
            key = watched ? watched.dataset.key : null;
        }
        const ref = key == null ? null : list.querySelector('.item[data-key="' + key + '"]');
        rows.push({ t: time, y: ref ? ref.getBoundingClientRect().top - viewTop : null, key });
        if ('${mode}' === 'scroll')
            instance.scrollController.followBy(step);
        else
            container.style.transform = 'translate3d(0, ' + (transform -= step) + 'px, 0)';
        if (performance.now() - started < ${seconds * 1000}) {
            requestAnimationFrame(tick);
            return;
        }
        container.style.transform = '';
        list.scrollTop = startTop;
        resolve(JSON.stringify(rows));
    };
    requestAnimationFrame(tick);
})`;

const report = (label, rows) => {
    const moves = [];
    for (let i = 1; i < rows.length; i++) {
        const previous = rows[i - 1];
        const row = rows[i];
        const dt = row.t - previous.t;
        if (row.y == null || previous.y == null || row.key !== previous.key || dt <= 0 || dt > 40)
            continue;

        moves.push(Math.abs(row.y - previous.y));
    }
    const jerks = [];
    for (let i = 1; i < moves.length; i++)
        jerks.push(Math.abs(moves[i] - moves[i - 1]));

    jerks.sort((a, b) => a - b);
    const at = q => jerks.length ? jerks[Math.min(jerks.length - 1, Math.floor(jerks.length * q))] : 0;
    const mean = moves.reduce((sum, x) => sum + x, 0) / (moves.length || 1);
    console.log(`${label}: ${moves.length} frames, mean step ${mean.toFixed(2)}px (want 2.00), `
        + `still frames ${moves.filter(x => x === 0).length}, step-to-step change `
        + `p50 ${at(0.5).toFixed(2)} p90 ${at(0.9).toFixed(2)} max ${(jerks.at(-1) ?? 0).toFixed(2)}px`);
};

if (!await ev(`!!document.querySelector('.virtual-list.infinite-list')`)) {
    console.log('no infinite list on the page');
    process.exit(1);
}
report('scrollTop per frame', JSON.parse(await ev(drive('scroll', SECONDS), true)));
await new Promise(r => setTimeout(r, 1500));
report('transform per frame', JSON.parse(await ev(drive('transform', SECONDS), true)));
ws.close(); process.exit(0);
