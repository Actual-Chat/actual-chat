// Soak: a long random sequence of real gestures at the bottom edge on chrome2, judged continuously
// against the rules. Single scenarios pass; what broke on the phones was state leaking from one
// gesture into the next, and only a long mixed run finds that.
//   node soak.mjs [gestures=60] [port=9223] [mode] [tOffsetBaseline]
import { createRequire } from 'node:module';
import fs from 'node:fs';
const require = createRequire(new URL('../../package.json', import.meta.url));
const WebSocket = require('ws');
const RECORDER = fs.readFileSync(new URL('./recorder.js', import.meta.url), 'utf8');
const COUNT = Number(process.argv[2] || 60);
const PORT = Number(process.argv[3] || 9223);
const mode = process.argv[4] || '';
const T_OFFSET_BASELINE = Number(process.argv[5] || 0);
if (!Number.isFinite(T_OFFSET_BASELINE)) throw new Error('tOffsetBaseline must be a finite number');
const sleep = ms => new Promise(r => setTimeout(r, ms));
// Deterministic pseudo-random so a failing soak can be re-run.
let seed = 12345; const rnd = () => (seed = (seed * 1103515245 + 12345) & 0x7fffffff) / 0x7fffffff;
const pick = a => a[Math.floor(rnd() * a.length)];

const list = await (await fetch(`http://localhost:${PORT}/json/list`)).json();
const target = list.find(x => x.type === 'page' && (x.url || '').includes('local.voxt.ai'));
const ws = new WebSocket(target.webSocketDebuggerUrl, { perMessageDeflate: false, maxPayload: 512 * 1024 * 1024 });
let id = 0; const pending = new Map();
const send = (m, p = {}) => new Promise((res, rej) => { const i = ++id; pending.set(i, { res, rej }); ws.send(JSON.stringify({ id: i, method: m, params: p })); });
ws.on('message', d => { const m = JSON.parse(d.toString()); if (m.id && pending.has(m.id)) { const h = pending.get(m.id); pending.delete(m.id); m.error ? h.rej(new Error(JSON.stringify(m.error).slice(0, 200))) : h.res(m.result); } });
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
if (mode === 'takeover') url.searchParams.set('vltakeover', '1'); else url.searchParams.delete('vltakeover');
if (T_OFFSET_BASELINE !== 0) url.searchParams.set('vltoffset', String(T_OFFSET_BASELINE)); else url.searchParams.delete('vltoffset');
if (url.toString() !== target.url) { await send('Page.navigate', { url: url.toString() }); await sleep(6000); }
else { await send('Page.reload', { ignoreCache: true }); await sleep(6000); }
await send('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 1 });
const ev = async (e, aw = false) => { const r = await send('Runtime.evaluate', { expression: e, returnByValue: true, awaitPromise: aw, timeout: 60000 }); if (r.exceptionDetails) throw new Error(JSON.stringify(r.exceptionDetails).slice(0, 300)); return r.result.value; };
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
const box = JSON.parse(await ev(`(()=>{const b=document.querySelector('.virtual-list.infinite-list').getBoundingClientRect();return JSON.stringify({x:Math.round(b.left+b.width/2),y:Math.round(b.top+b.height/2)})})()`));
const touch = (type, y) => send('Input.dispatchTouchEvent', { type, touchPoints: type === 'touchEnd' ? [] : [{ x: box.x, y, radiusX: 4, radiusY: 4, force: 1 }] });
const moves = async (ys, gap = 12) => { const acks = []; for (const y of ys) { acks.push(touch('touchMove', y)); await sleep(gap); } await Promise.all(acks); };

await ev(`(()=>{const l=document.querySelector('.virtual-list.infinite-list');l.scrollTop=l.scrollController.getEffectiveScrollLimits().max;return 1})()`);
await sleep(600);
await ev(RECORDER); await ev(`window.__vlt.rows.length=0; window.__vlt.events.length=0; 1`);
await ev(`globalThis.debugUI?.virtualListDebug?.(true); 1`);
// The checker only warns to the console; the list it used to expose went with virtual-list-debug.ts,
// and `?.` on the missing call made "violations === 0" pass for free.
const violationLog = [];
ws.on('message', d => {
    const m = JSON.parse(d.toString());
    if (m.method !== 'Runtime.consoleAPICalled')
        return;

    const text = (m.params.args || []).map(a => a.value ?? a.description ?? '').join(' ');
    if (/model drift|content overflow/i.test(text))
        violationLog.push(text.slice(0, 300));
});
const y = box.y;
const gestures = {
    pullHoldRelease: async () => { await touch('touchStart', y + 100); await moves(Array.from({ length: 20 }, (_, i) => y + 100 - 12 * (i + 1))); await sleep(200 + rnd() * 600); await touch('touchEnd', y - 140); },
    throwOut: async () => { await touch('touchStart', y + 150); await moves(Array.from({ length: 5 }, (_, i) => y + 150 - 70 * (i + 1))); await touch('touchEnd', y - 200); },
    upDown: async () => { await touch('touchStart', y); await moves(Array.from({ length: 6 }, (_, i) => y - 30 * (i + 1))); await moves(Array.from({ length: 5 }, (_, i) => y - 180 + 70 * (i + 1))); await touch('touchEnd', y + 170); },
    swingIn: async () => { await touch('touchStart', y - 100); await moves(Array.from({ length: 5 }, (_, i) => y - 100 + 80 * (i + 1))); await touch('touchEnd', y + 300); },
    tap: async () => { await touch('touchStart', y); await sleep(60); await touch('touchEnd', y); },
    slowBack: async () => { await touch('touchStart', y - 100); await moves(Array.from({ length: 25 }, (_, i) => y - 100 + 8 * (i + 1)), 20); await touch('touchEnd', y + 100); },
    catchAndHold: async () => { await touch('touchStart', y); await sleep(500 + rnd() * 500); await touch('touchEnd', y); },
};
const names = Object.keys(gestures);
const log = [];
for (let i = 0; i < COUNT; i++) {
    const n = pick(names);
    log.push(n);
    await send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Escape', code: 'Escape', windowsVirtualKeyCode: 27 });
    await send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Escape', code: 'Escape', windowsVirtualKeyCode: 27 });
    await sleep(50);
    await gestures[n]();
    await sleep(pick([50, 120, 250, 500, 900, 1600]));
    // Every gesture must be able to reach the band: whenever the list is idle and away from the edge,
    // put it back at the edge first. Written only in-band with nothing moving, so it is a legal write.
    if (i % 2 === 1) { await ev(`(()=>{const l=document.querySelector('.virtual-list.infinite-list');const sc=l.scrollController;const s=sc.getDebugState();const lim=sc.getEffectiveScrollLimits();if(s.phase==='in-band'&&Math.abs(l.scrollTop-lim.max)>5){l.scrollTop=lim.max;}return 1})()`); await sleep(500); }
}
await sleep(3000);
const { rows, events } = JSON.parse(await ev(`JSON.stringify({
    rows: window.__vlt.rows,
    events: window.__vlt.events,
})`));
const violations = violationLog.slice();
fs.mkdirSync('tmp/traces', { recursive: true }); fs.writeFileSync('tmp/traces/soak.json', JSON.stringify({ rows, events, violations, log }));
// judge
let inversions = 0, debtStarts = 0, badEnds = 0, ruleSteps = 0, stuckFrames = 0;
const isActive = row => row.phase !== 'in-band' || row.decision !== 'none';
for (let i = 1; i < rows.length; i++) if (rows[i].phase !== 'in-band' && rows[i].vis * rows[i].drift < -1) inversions++;
for (const e of events) if (e.type === 'touchstart') { const at = rows.findIndex(r => r.t >= e.t); if (at > 0 && rows[at - 1].phase === 'in-band' && Math.abs(rows[at - 1].band) > 1) debtStarts++; }
for (let i = 1; i < rows.length - 1; i++) if (isActive(rows[i - 1]) && !isActive(rows[i])) { const r = rows[i + 1]; if (Math.abs(r.band) > 1 || r.top < r.min - 1 || r.top > r.max + 1) badEnds++; }
for (let i = 1; i < rows.length; i++) { const p = rows[i - 1], r = rows[i]; if (r.phase === 'in-band' || p.phase === 'in-band') continue; const dtf = Math.abs(r.band - p.band), ds = Math.abs(r.top - p.top); const allowed = r.phase === 'following' ? ds * 0.7 + 1 : ds + 280; const cancelled = Math.abs((r.band - p.band) - (r.top - p.top)) < 12 + 3 * (r.t - p.t); if (dtf > allowed + 2 && !cancelled) ruleSteps++; }
// stuck: engaged for > 3s continuously
{ let run = 0; for (const r of rows) { if (r.phase === 'engaged') run++; else { if (run > 200) stuckFrames += run; run = 0; } } }
const maxTf = Math.max(...rows.map(r => Math.abs(r.tf)));
const maxBand = Math.max(...rows.map(r => Math.abs(r.band)));
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
console.log(`soak: ${COUNT} gestures, ${rows.length} frames`);
console.log(`  inversions ${inversions}  debt-starts ${debtStarts}  bad-ends ${badEnds}  rule-steps ${ruleSteps}  stuck-frames ${stuckFrames}  max|band| ${Math.round(maxBand)}  max|tf| ${Math.round(maxTf)}  final ${last.phase} band=${last.band} base=${last.base} over=${last.drift}  folds ${folds}  violations ${violations.length}`);
const engagedFrames = rows.filter(r => r.phase !== 'in-band').length;
console.log(`  band engaged on ${engagedFrames} frames`);
if (T_OFFSET_BASELINE !== 0)
    console.log(`  ${foldCheck.ok ? 'PASS' : 'FAIL'} tOffset fold: folded=${foldCheck.folded} base=${foldCheck.base} drift=${foldCheck.drift}`);
const passed = inversions + debtStarts + badEnds + ruleSteps + stuckFrames + violations.length === 0
    && last.phase === 'in-band'
    && last.decision === 'none'
    && Math.abs(last.band) < 1
    && baseError < 1
    && engagedFrames > 200
    && foldCheck.ok;
console.log(`  ${passed ? 'PASS' : 'FAIL'}`);
console.log('  sequence:', log.join(' '));
ws.close(); process.exit(passed ? 0 : 1);
