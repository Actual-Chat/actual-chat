// The desktop rig. Drives real touch gestures into chrome1 over CDP (Input.dispatchTouchEvent reaches
// Chrome's input pipeline on desktop, so a fast drag produces a real compositor fling), records the
// controller frame by frame through the same recorder as the phones, and judges the result against
// the rules. Everything the phones did by hand, repeatable here.
//
//   node rig.mjs <scenario> [port] [mode] [tOffsetBaseline]
//   node rig.mjs all 9222 takeover 1000
import { createRequire } from 'node:module';
import fs from 'node:fs';
const require = createRequire(new URL('../../package.json', import.meta.url));
const WebSocket = require('ws');
const RECORDER = fs.readFileSync(new URL('./recorder.js', import.meta.url), 'utf8');

const scenario = process.argv[2] || 'pull-release';
const PORT = Number(process.argv[3] || 9222);
const mode = process.argv[4] || '';
const NOLOCK = mode === 'nolock';
const TAKEOVER = mode === 'takeover';
const T_OFFSET_BASELINE = Number(process.argv[5] || 0);
if (!Number.isFinite(T_OFFSET_BASELINE)) throw new Error('tOffsetBaseline must be a finite number');
const sleep = ms => new Promise(r => setTimeout(r, ms));

const list = await (await fetch(`http://localhost:${PORT}/json/list`)).json();
const target = list.find(x => x.type === 'page' && (x.url || '').includes('local.voxt.ai'));
if (!target) { console.log('no voxt page on chrome', PORT); process.exit(1); }
const ws = new WebSocket(target.webSocketDebuggerUrl, { perMessageDeflate: false, maxPayload: 256 * 1024 * 1024 });
let id = 0; const pending = new Map();
const send = (m, p = {}) => new Promise((res, rej) => { const i = ++id; pending.set(i, { res, rej }); ws.send(JSON.stringify({ id: i, method: m, params: p })); });
ws.on('message', d => { const m = JSON.parse(d.toString()); if (m.id && pending.has(m.id)) { const h = pending.get(m.id); pending.delete(m.id); m.error ? h.rej(new Error(JSON.stringify(m.error).slice(0, 300))) : h.res(m.result); } });
await new Promise(r => ws.on('open', r));
await send('Runtime.enable'); await send('Page.enable');
await send('Page.bringToFront');
// The chat view is only the touch-scrolling element on a phone-shaped viewport, and an emulation
// override belongs to the session that set it - Chrome drops it when that client detaches, which is
// how a matrix run ends up half-measured on a desktop layout. So this session sets its own.
//   VL_RIG_VIEWPORT=412x915x2.6   (default; VL_RIG_VIEWPORT=off leaves the window alone)
const applyViewport = async () => {
    const value = process.env.VL_RIG_VIEWPORT ?? '412x915x2.6';
    if (value === 'off')
        return null;

    const match = /^(\d+)x(\d+)(?:x([\d.]+))?$/.exec(value);
    if (!match) throw new Error('VL_RIG_VIEWPORT must be <width>x<height>[x<deviceScaleFactor>] or "off"');
    const metrics = {
        width: Number(match[1]),
        height: Number(match[2]),
        deviceScaleFactor: Number(match[3] ?? 0),
        mobile: true,
    };
    await send('Emulation.setDeviceMetricsOverride', metrics);
    return metrics;
};
await applyViewport();
const url = new URL(target.url);
if (NOLOCK) url.searchParams.set('vllock', '0'); else url.searchParams.delete('vllock');
if (TAKEOVER) url.searchParams.set('vltakeover', '1'); else url.searchParams.delete('vltakeover');
if (T_OFFSET_BASELINE !== 0) url.searchParams.set('vltoffset', String(T_OFFSET_BASELINE)); else url.searchParams.delete('vltoffset');
if (url.toString() !== target.url) {
    await send('Page.navigate', { url: url.toString() }); await sleep(6000);
}
else {
    await send('Page.reload', { ignoreCache: true }); await sleep(6000);
}
const ev = async (expression, awaitPromise = false) => {
    const r = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise, timeout: 60000 });
    if (r.exceptionDetails) throw new Error('PAGE ' + JSON.stringify(r.exceptionDetails).slice(0, 400));
    return r.result.value;
};
const foldCheck = T_OFFSET_BASELINE === 0 ? { ok: true } : JSON.parse(await ev(`JSON.stringify((() => {
    const list = document.querySelector('.virtual-list.infinite-list');
    const instance = [...(globalThis.InfiniteList?.instances ?? [])].find(x => x.ref === list);
    if (!instance) return { ok: false, error: 'InfiniteList instance not found' };
    const before = instance.containerRef.getBoundingClientRect().top;
    instance.chainStart += 137;
    instance.writeChainPosition();
    instance.setTOffset(instance.tOffsetBaseline + 137);
    const prepared = instance.containerRef.getBoundingClientRect().top;
    const folded = instance.foldTOffset();
    const after = instance.containerRef.getBoundingClientRect().top;
    const base = instance.scrollController.getDebugState().offset - instance.scrollController.bandOffset;
    return { ok: Math.abs(folded - 137) < 0.1 && Math.abs(instance.tOffset - instance.tOffsetBaseline) < 0.1
        && Math.abs(prepared - before) < 1 && Math.abs(after - before) < 1
        && Math.abs(base + instance.tOffsetBaseline) < 0.1,
        folded, tOffset: instance.tOffset, base, preparedDrift: prepared - before, drift: after - before };
})())`));

// Touch emulation on: Chrome then treats dispatched touch events as a real touch device.
await send('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 1 });
await send('Emulation.setEmitTouchEventsForMouse', { enabled: false });

const box = JSON.parse(await ev(`(() => {
    const l = document.querySelector('.virtual-list.infinite-list');
    const b = l.getBoundingClientRect();
    return JSON.stringify({ x: Math.round(b.left + b.width / 2), top: Math.round(b.top), bottom: Math.round(b.bottom), h: Math.round(b.height) });
})()`));

const touch = (type, y, x = box.x) => send('Input.dispatchTouchEvent', {
    type, touchPoints: type === 'touchEnd' ? [] : [{ x, y, radiusX: 4, radiusY: 4, force: 1 }],
});
// A drag: from y0 to y1 over `frames` frames. Speed comes out of distance / frames.
const drag = async (y0, y1, frames, holdMsAtEnd = 0) => {
    await touch('touchStart', y0);
    for (let i = 1; i <= frames; i++) { await touch('touchMove', y0 + (y1 - y0) * i / frames); await sleep(16); }
    if (holdMsAtEnd) await sleep(holdMsAtEnd);
    await touch('touchEnd', y1);
};
// A fast drag whose moves are fired 12ms apart without waiting for each ack: Chrome drops the fling
// from a synthetic touch whose moves arrive further apart than that.
const fling = async (y0, y1, frames) => {
    await touch('touchStart', y0);
    const acks = [];
    for (let i = 1; i <= frames; i++) { acks.push(touch('touchMove', y0 + (y1 - y0) * i / frames)); await sleep(12); }
    await Promise.all(acks);
    await touch('touchEnd', y1);
};
const swing = async (y0, yOut, yIn, outFrames, inFrames, holdMs = 0) => {
    await touch('touchStart', y0);
    for (let i = 1; i <= outFrames; i++) { await touch('touchMove', y0 + (yOut - y0) * i / outFrames); await sleep(16); }
    if (holdMs) await sleep(holdMs);
    const acks = [];
    for (let i = 1; i <= inFrames; i++) { acks.push(touch('touchMove', yOut + (yIn - yOut) * i / inFrames)); await sleep(12); }
    await Promise.all(acks);
    await touch('touchEnd', yIn);
};
const parkAtBottom = async () => {
    await ev(`(async () => { const l = document.querySelector('.virtual-list.infinite-list');
        const lim = l.scrollController.getEffectiveScrollLimits(); l.scrollTop = lim.max;
        await new Promise(r => setTimeout(r, 500)); return 1; })()`, true);
};
const parkAtTop = async () => {
    await ev(`(async () => { const l = document.querySelector('.virtual-list.infinite-list');
        const lim = l.scrollController.getEffectiveScrollLimits(); l.scrollTop = lim.min;
        await new Promise(r => setTimeout(r, 500)); return 1; })()`, true);
};
// The checker the rig enables only warns to the console - the queryable violation list it used to
// read was removed with virtual-list-debug.ts, and `?.` on the missing call made the judge's
// "violations === 0" pass for free. These are the warnings themselves.
const violationLog = [];
ws.on('message', d => {
    const m = JSON.parse(d.toString());
    if (m.method !== 'Runtime.consoleAPICalled')
        return;

    const text = (m.params.args || []).map(a => a.value ?? a.description ?? '').join(' ');
    if (/model drift|content overflow/i.test(text))
        violationLog.push(text.slice(0, 300));
});

const arm = async () => {
    await send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Escape', code: 'Escape', windowsVirtualKeyCode: 27 });
    await send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Escape', code: 'Escape', windowsVirtualKeyCode: 27 });
    await sleep(50);
    await ev(RECORDER);
    await ev(`globalThis.debugUI?.virtualListDebug?.(true); 1`);
    violationLog.length = 0;
    await ev(`window.__vlt.rows.length = 0; window.__vlt.events.length = 0; 1`);
};
const pull = async () => {
    const data = JSON.parse(await ev(`JSON.stringify({
        rows: window.__vlt.rows,
        events: window.__vlt.events,
    })`));
    data.violations = violationLog.slice();
    return data;
};

const yMid = box.top + box.h / 2;
const scenarios = {
    // Slow pull past the bottom edge, hold, release. Expect: band, then spring home, settle at 0.
    'pull-release': async () => { await parkAtBottom(); await arm(); await drag(yMid + 150, yMid - 150, 30, 300); await sleep(2500); },
    // Fast fling outward at the bottom edge. Expect: lock ends it, band, spring home. Content never steps.
    'throw-out': async () => { await parkAtBottom(); await arm(); await fling(yMid + 200, yMid - 200, 6); await sleep(4000); },
    // The same throw at the top edge, where reverse scrolling and a negative excursion exercise the other sign.
    'throw-top': async () => { await parkAtTop(); await arm(); await fling(yMid - 200, yMid + 200, 6); await sleep(4000); },
    // Pull out slowly, then swing back in fast and release. Expect: it flings INTO the list; no bounce.
    'swing-back': async () => { await parkAtBottom(); await arm(); await swing(yMid + 150, yMid - 150, yMid, 30, 5, 200); await sleep(3000); },
    // Pull out, release, then catch the bounce mid-return and hold. Expect: content does not step at the catch.
    'catch': async () => { await parkAtBottom(); await arm(); await fling(yMid + 150, yMid - 150, 6); await sleep(180); await touch('touchStart', yMid); await sleep(600); await touch('touchEnd', yMid); await sleep(2500); },
    // Catch the bounce and keep pulling in the same gesture. Expect: the lock is gone and the content
    // follows the finger instead of staying parked where it was caught.
    'catch-drag': async () => { await parkAtBottom(); await arm(); await fling(yMid + 150, yMid - 150, 6); await sleep(180); await touch('touchStart', yMid); for (let i = 1; i <= 6; i++) { await touch('touchMove', yMid - 20 * i); await sleep(16); } await touch('touchEnd', yMid - 120); await sleep(2500); },
    // Up-then-down in one gesture from the edge. Expect: behaves like a throw into the list.
    'updown': async () => { await parkAtBottom(); await arm(); await touch('touchStart', yMid); for (let i = 1; i <= 8; i++) { await touch('touchMove', yMid - 25 * i); await sleep(16); } for (let i = 1; i <= 6; i++) { await touch('touchMove', yMid - 200 + 60 * i); await sleep(16); } await touch('touchEnd', yMid + 160); await sleep(3000); },
    // Pull far out and hold still. Expect: content still (brake), no drift, no jitter.
    'brake': async () => { await parkAtBottom(); await arm(); await drag(yMid + 200, yMid - 200, 25, 1200); await sleep(2500); },
    // Five throws in a row without waiting for the return: each catches the previous bounce. Expect: no
    // ratchet - the excursion must not grow across touches, and no gesture starts inside a band.
    'repeat-catch': async () => { await parkAtBottom(); await arm(); for (let i = 0; i < 5; i++) { await drag(yMid + 150, yMid - 150, 8); await sleep(250); } await sleep(3500); },
    // Five fast up-downs from the edge in a row. Expect: each behaves like a throw into the list; the
    // list must actually move away from the edge, not stay pinned there.
    'repeat-updown': async () => { await parkAtBottom(); await arm(); for (let i = 0; i < 5; i++) { await touch('touchStart', yMid); for (let k = 1; k <= 6; k++) { await touch('touchMove', yMid - 30 * k); await sleep(16); } for (let k = 1; k <= 5; k++) { await touch('touchMove', yMid - 180 + 70 * k); await sleep(16); } await touch('touchEnd', yMid + 170); await sleep(700); } await sleep(3500); },
    // A pull that goes out, comes back through the edge under the finger, goes out again, and releases
    // outside. Expect: one continuous band, no inversion, spring home at the end.
    // Control: the same swing-back gesture started 400px INSIDE the band - a plain fling, no overscroll.
    // Its coast is what swing-back and updown are compared against.
    'control-fling': async () => { await ev(`(async () => { const l = document.querySelector('.virtual-list.infinite-list'); const lim = l.scrollController.getEffectiveScrollLimits(); l.scrollTop = lim.max - 400; await new Promise(r => setTimeout(r, 500)); return 1; })()`, true); await arm(); await drag(yMid - 150, yMid + 250, 5); await sleep(2500); },
    // A fling that reaches the edge with the finger already up. Expect: it bounces - the display carries
    // the fling's momentum past the edge, turns, and comes home - and never steps.
    'fling-edge': async () => { await ev(`(async () => { const l = document.querySelector('.virtual-list.infinite-list'); const lim = l.scrollController.getEffectiveScrollLimits(); l.scrollTop = lim.max - 900; await new Promise(r => setTimeout(r, 500)); return 1; })()`, true); await arm(); await fling(yMid + 250, yMid - 250, 5); await sleep(4500); },
    // Models raw scroll movement leaking through the overflow lock. Each leak must be snapped back
    // without moving the content while the transform return continues.
    'native-resume': async () => {
        await parkAtBottom(); await arm(); await fling(yMid + 200, yMid - 200, 6);
        for (const delta of [48, -36]) {
            const found = await ev(`new Promise(resolve => { const started = performance.now(); const tick = () => {
                const l = document.querySelector('.virtual-list.infinite-list');
                if (l?.scrollController.getDebugState().decision === 'transform') resolve(true);
                else if (performance.now() - started > 2000) resolve(false);
                else requestAnimationFrame(tick);
            }; tick(); })`, true);
            if (!found) throw new Error('transform takeover did not start');
            await ev(`(() => { const l = document.querySelector('.virtual-list.infinite-list'); l.scrollTop += ${delta}; return 1; })()`);
            await sleep(50);
        }
        await sleep(3500);
    },
    'cross-and-back': async () => { await parkAtBottom(); await arm(); await touch('touchStart', yMid); for (let k = 1; k <= 10; k++) { await touch('touchMove', yMid - 20 * k); await sleep(16); } for (let k = 1; k <= 14; k++) { await touch('touchMove', yMid - 200 + 20 * k); await sleep(16); } for (let k = 1; k <= 12; k++) { await touch('touchMove', yMid + 80 - 20 * k); await sleep(16); } await touch('touchEnd', yMid - 160); await sleep(2500); },
};

const judge = (name, { rows, events, violations }) => {
    const content = rows.map(r => r.cy);
    const isActive = row => row.phase !== 'in-band' || row.decision !== 'none';
    let worst = 0, worstAt = 0;
    for (let i = 1; i < rows.length; i++) {
        const isEnd = isActive(rows[i - 1]) && !isActive(rows[i]);
        const s = isEnd && i + 1 < rows.length ? Math.abs(content[i + 1] - content[i - 1]) : Math.abs(content[i] - content[i - 1]);
        if (s > worst) { worst = s; worstAt = i; }
    }
    const last = rows[rows.length - 1];
    const expectedBase = -T_OFFSET_BASELINE;
    const baseError = Math.abs(last.base - expectedBase);
    let folds = 0;
    for (let i = 1; i < rows.length; i++) {
        const previousError = Math.abs(rows[i - 1].base - expectedBase);
        const error = Math.abs(rows[i].base - expectedBase);
        if (previousError > 8 && error <= 1 && Math.abs(rows[i].base - rows[i - 1].base) > 8)
            folds++;
    }
    const phases = [...new Set(rows.map(r => r.phase))].join('/');
    const maxOver = Math.max(...rows.map(r => Math.abs(r.drift)));
    const ends = rows.filter((r, i) => i > 0 && isActive(rows[i - 1]) && !isActive(r)).length;
    // jitter: change-of-step during the return
    const eng = []; for (let i = 2; i < rows.length; i++) if (rows[i].phase === 'engaged' && rows[i - 1].phase === 'engaged' && rows[i - 2].phase === 'engaged') eng.push(Math.abs((content[i] - content[i - 1]) - (content[i - 1] - content[i - 2])));
    eng.sort((a, b) => a - b);
    const jerk = eng.length ? eng[Math.floor(eng.length * 0.9)] : 0;
    // The rules, in numbers.
    //   inversions: `visible` opposite in sign to `over` - the band drawn on the wrong side.
    //   debt-starts: a touchstart while in-band with a band transform still standing.
    //   bad-ends: an excursion ending with the band non-zero or the scroll outside the limits.
    //   finger-ignored: while following, content moved less than 40% of the finger.
    let inversions = 0;
    for (let i = 1; i < rows.length; i++) if (rows[i].phase !== 'in-band' && rows[i].vis * rows[i].drift < -1) inversions++;
    let debtStarts = 0;
    for (const e of events) if (e.type === 'touchstart') { const at = rows.findIndex(r => r.t >= e.t); if (at > 0 && rows[at - 1].phase === 'in-band' && Math.abs(rows[at - 1].band) > 1) debtStarts++; }
    let badEnds = 0;
    // At an end the band must be gone and the position legal. The owner's translation may remain.
    for (let i = 1; i < rows.length - 1; i++) if (isActive(rows[i - 1]) && !isActive(rows[i])) { const r = rows[i + 1]; if (Math.abs(r.band) > 1 || r.top < r.min - 1 || r.top > r.max + 1) badEnds++; }
    // A step is band motion no rule explains: under a finger the band moves by at most the
    // resisted share of the scroll delta; engaged it moves by the scroll delta plus the floor's step
    // (<= 6000px/s) or the carry's (<= ~16300px/s) - 280px at 60Hz.
    let ruleSteps = 0;
    for (let i = 1; i < rows.length; i++) {
        const p = rows[i - 1], r = rows[i];
        if (r.phase === 'in-band' || p.phase === 'in-band') continue;
        const dtf = Math.abs(r.band - p.band), ds = Math.abs(r.top - p.top);
        const allowed = r.phase === 'following' ? ds * 0.7 + 1 : ds + 280;
        // The catch write: our own scrollTop write with the transform moved by the same amount in the
        // same frame. Content is untouched by construction; the signed sum tells it from a step - up to
        // one frame of the floor (<= 6000px/s), which can run in the same frame the finger lands.
        const cancelled = Math.abs((r.band - p.band) - (r.top - p.top)) < 12 + 3 * (r.t - p.t);
        if (dtf > allowed + 2 && !cancelled) ruleSteps++;
    }
    // Against what the curve lets through at the depth the gesture started, not against the raw finger:
    // past the ramp only a third of a pull reaches the screen, and that is the resistance, not a fault.
    const slopeAt = over => 1 - Math.min(0.667 * Math.abs(over) / 444, 0.667);
    let ignored = 0;
    { let open = null; for (const e of events) { if (e.type === 'touchstart' && !open) open = { t: e.t, y0: e.y, y1: e.y }; if (open && e.y != null) open.y1 = e.y; if (open && (e.type === 'touchend' || e.type === 'touchcancel')) { const a = rows.findIndex(r => r.t >= open.t), b = rows.findIndex(r => r.t >= e.t); const finger = Math.abs(open.y1 - open.y0); if (a >= 0 && b > a && finger > 60 && Math.abs(content[b] - content[a]) < finger * slopeAt(rows[a].drift) * 0.4) ignored++; open = null; } } }
    let coast = 0;
    { const rel = [...events].reverse().find(e => e.type === 'touchend' || e.type === 'touchcancel'); if (rel) { const a = rows.findIndex(r => r.t >= rel.t); let end = a; for (let i = a; i < rows.length - 1; i++) if (Math.abs(rows[i + 1].top - rows[i].top) > 0.5) end = i + 1; if (a >= 0) coast = rows[end].top - rows[a].top; } }
    console.log(`\n== ${name} ==  ${rows.length} frames, phases ${phases}   coast after release ${Math.round(coast)}px`);
    const violationCodes = violations.map(x => x.code).join(',');
    console.log(`   worst step ${worst.toFixed(1)}px (${rows[worstAt]?.phase})  max|over| ${Math.round(maxOver)}  ended ${last.phase} over=${last.drift} band=${last.band} base=${last.base}  folds ${folds}  handbacks ${ends}  inversions ${inversions}  debt-starts ${debtStarts}  bad-ends ${badEnds}  finger-ignored ${ignored}  jerk ${jerk.toFixed(1)}  violations ${violations.length}${violationCodes ? ` (${violationCodes})` : ''}`);
    fs.writeFileSync(`tmp/traces/rig-${name}.json`, JSON.stringify({ rows, events, violations }));
    return { worst, ended: last.phase, decision: last.decision, over: last.drift, band: last.band, base: last.base, baseError, folds, jerk, ends, inversions, debtStarts, badEnds, ignored, ruleSteps, violations: violations.length };
};

fs.mkdirSync('tmp/traces', { recursive: true });
const names = scenario === 'all'
    ? Object.keys(scenarios).filter(name => TAKEOVER || name !== 'native-resume')
    : [scenario];
const results = {};
for (const n of names) {
    await scenarios[n]();
    results[n] = judge(n, await pull());
}
console.log('\nSUMMARY');
for (const [n, r] of Object.entries(results)) {
    const ok = r.ended === 'in-band' && r.decision === 'none' && Math.abs(r.band) < 1 && Math.abs(r.over) < 1 && r.baseError < 1 && r.inversions === 0 && r.debtStarts === 0 && r.badEnds === 0 && r.ignored === 0 && r.ruleSteps === 0 && r.violations === 0;
    r.ok = ok;
    console.log(`  ${ok ? 'PASS' : 'FAIL'}  ${n.padEnd(15)} ended ${r.ended} band=${r.band} base=${r.base}  folds ${r.folds}  inv ${r.inversions}  debt ${r.debtStarts}  badEnd ${r.badEnds}  ignored ${r.ignored}  ruleSteps ${r.ruleSteps}  violations ${r.violations}`);
}
if (T_OFFSET_BASELINE !== 0)
    console.log(`  ${foldCheck.ok ? 'PASS' : 'FAIL'}  tOffset fold    folded=${foldCheck.folded} base=${foldCheck.base} drift=${foldCheck.drift}`);
ws.close(); process.exit(Object.values(results).every(x => x.ok) && foldCheck.ok ? 0 : 1);
