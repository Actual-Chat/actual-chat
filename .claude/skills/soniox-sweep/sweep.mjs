// Reports, and optionally purges, the Soniox artifacts left by offline transcription.
// See SKILL.md - in particular, do not raise the request rate.
const args = process.argv.slice(2);
const flag = (name, fallback) => {
    const i = args.indexOf(name);
    return i >= 0 ? args[i + 1] : fallback;
};

const keyFile = flag('--key-file');
const fs = await import('node:fs');
if (keyFile && !fs.existsSync(keyFile)) {
    console.error(`No key file at ${keyFile}. Ask for the path - never fall back to the other environment's key.`);
    process.exit(1);
}
const key = keyFile ? fs.readFileSync(keyFile, 'utf8').trim() : process.env.CoreSettings__SonioxKey;
if (!key) {
    console.error('No Soniox key: pass --key-file <path> or set CoreSettings__SonioxKey.');
    process.exit(1);
}

const isPurge = args.includes('--purge');
const olderThan = flag('--older-than', '1h');
const hours = parseFloat(olderThan) * (olderThan.endsWith('m') ? 1 / 60 : 1);
if (!(hours > 0))
    throw new Error(`Bad --older-than: ${olderThan}. Use e.g. 15m or 1h.`);

const cutoff = new Date(Date.now() - hours * 3600_000).toISOString();
const base = 'https://api.soniox.com/v1';
const sleep = ms => new Promise(r => setTimeout(r, ms));

// ~500 requests/minute is the organization-wide limit and live transcription spends from the
// same budget, so every call goes through one pacer at 200/min. Do not raise this.
const minIntervalMs = 300;
let nextAt = 0;

async function call(method, path) {
    for (let attempt = 0; ; attempt++) {
        const wait = nextAt - Date.now();
        if (wait > 0)
            await sleep(wait);
        nextAt = Date.now() + minIntervalMs;
        const r = await fetch(`${base}/${path}`, { method, headers: { Authorization: `Bearer ${key}` } });
        if (r.status !== 429 || attempt >= 10)
            return r;
        const delay = Math.min(60_000, 10_000 * 2 ** Math.min(attempt, 2));
        console.log(`  429, backing off ${delay / 1000}s`);
        nextAt = Date.now() + delay;
    }
}

async function listAll(kind) {
    const out = [];
    let cursor = null;
    do {
        const q = new URLSearchParams({ limit: '1000' });
        if (cursor)
            q.set('cursor', cursor);
        const r = await call('GET', `${kind}?${q}`);
        if (!r.ok)
            throw new Error(`GET ${kind}: ${r.status} ${await r.text()}`);
        const d = await r.json();
        out.push(...d[kind]);
        cursor = d.next_page_cursor;
    } while (cursor);
    return out;
}

function summarize(kind, all) {
    const stale = all.filter(x => x.created_at < cutoff);
    console.log(`\n${kind}: ${all.length} total, ${stale.length} older than ${olderThan} (${cutoff})`);
    if (!all.length)
        return stale;
    const dates = all.map(x => x.created_at).sort();
    console.log(`  oldest ${dates[0]}   newest ${dates[dates.length - 1]}`);
    const count = f => all.reduce((m, x) => (m[f(x)] = (m[f(x)] || 0) + 1, m), {});
    if (kind === 'transcriptions')
        console.log('  status:', JSON.stringify(count(x => x.status)));
    const byDay = Object.entries(count(x => x.created_at.slice(0, 10))).sort();
    console.log('  by day:', JSON.stringify(Object.fromEntries(byDay)));
    return stale;
}

async function purge(kind, stale) {
    let ok = 0;
    let failed = 0;
    for (const item of stale) {
        const r = await call('DELETE', `${kind}/${item.id}`);
        if (r.ok || r.status === 404) {
            if (++ok % 200 === 0)
                console.log(`  ${ok}/${stale.length} deleted`);
        }
        else {
            failed++;
            if (failed <= 5)
                console.log(`  ! ${kind}/${item.id}: ${r.status} ${(await r.text()).slice(0, 100)}`);
        }
    }
    console.log(`${kind}: deleted ${ok}, failed ${failed}`);
}

console.log(`Soniox ${isPurge ? 'purge' : 'report'} - key from ${keyFile ?? 'CoreSettings__SonioxKey'}`);
if (isPurge)
    console.log(`Pacing at ${Math.round(60_000 / minIntervalMs)} requests/minute.`);

// Transcriptions first - deleting one cascades to the file it holds.
for (const kind of ['transcriptions', 'files']) {
    const stale = summarize(kind, await listAll(kind));
    if (isPurge && stale.length)
        await purge(kind, stale);
}
if (isPurge) {
    console.log('\n--- after ---');
    for (const kind of ['transcriptions', 'files'])
        console.log(`${kind}: ${(await listAll(kind)).length} left`);
}
