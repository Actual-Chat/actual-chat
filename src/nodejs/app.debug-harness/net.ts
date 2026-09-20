import { execFileSync } from 'child_process';
import * as fs from 'fs';
import * as path from 'path';

/*
 * Network shaping: routes a slot's nginx traffic through Toxiproxy, which adds latency on
 * the nginx -> Kestrel hop. That hop carries every socket the client opens - including the
 * three inside workers, which a WebSocket patched in the page never sees - plus uploads and
 * media, and it's plain HTTP, so no certificates are involved.
 */

const ToxiproxyUrl = 'http://localhost:8474';
const NginxContainer = 'actual-chat-infra-nginx-1';
// A slot on port P is proxied on P+5, and its HTTP/2 sibling (P+1) on P+6 - see docker-compose.yml
const ProxyPortShift = 5;
const Streams = ['upstream', 'downstream'] as const;

export interface Slot {
    name: string;
    port: number;
    mainRepo: string;
}

export interface Shape {
    // Added in each direction, so a round trip gets twice this
    latencyMs: number;
    jitterMs: number;
}

/** The slot the harness runs from: its name and port come from the working folder's .env. */
export function getSlot(): Slot {
    const env = readEnv(path.resolve(process.cwd(), '.env'));
    const baseUri = env.get('HostSettings__BaseUri') ?? '';
    const name = /^https:\/\/(ws\d+)\.local\.voxt\.ai\/?$/.exec(baseUri)?.[1];
    if (!name)
        throw new Error(
            `net: '${baseUri}' isn't a ws<n> slot. The main repo's port is set in nginx.conf itself, `
            + 'so only slots can be shaped - run the harness from a slot folder.');

    const port = Number(env.get('HostSettings__BasePort'));
    if (!Number.isInteger(port))
        throw new Error('net: HostSettings__BasePort is missing from .env.');

    const gitCommonDir = execFileSync('git', ['rev-parse', '--git-common-dir'], { encoding: 'utf8' }).trim();
    return { name, port, mainRepo: path.dirname(path.resolve(gitCommonDir)) };
}

export async function enableShaping(slot: Slot, shape: Shape): Promise<void> {
    await ensureToxiproxy(slot);
    for (const proxy of getProxies(slot)) {
        await upsertProxy(proxy);
        for (const stream of Streams)
            await upsertLatency(proxy.name, stream, shape);
    }
    writeNginxMap(slot, slot.port + ProxyPortShift);
    reloadNginx();
}

/** Changes the latency on the fly - the proxies must already be up. */
export async function setShape(slot: Slot, shape: Shape): Promise<void> {
    for (const proxy of getProxies(slot))
        for (const stream of Streams)
            await upsertLatency(proxy.name, stream, shape);
}

export async function disableShaping(slot: Slot): Promise<void> {
    writeNginxMap(slot, slot.port);
    reloadNginx();
    if (!await isToxiproxyUp())
        return;

    for (const proxy of getProxies(slot))
        await callToxiproxy('DELETE', `/proxies/${proxy.name}`, undefined, [404]);
}

export async function describeShaping(slot: Slot): Promise<string> {
    const mapPort = readNginxMapPort(slot);
    const lines = [
        `slot ${slot.name}: Kestrel on ${slot.port}, nginx sends it to ${mapPort ?? 'an unknown port'}`
        + (mapPort === slot.port + ProxyPortShift ? ' (through Toxiproxy)' : ''),
    ];
    if (!await isToxiproxyUp()) {
        lines.push('Toxiproxy is not running');
        return lines.join('\n');
    }

    for (const proxy of getProxies(slot)) {
        const toxics = await callToxiproxy('GET', `/proxies/${proxy.name}/toxics`, undefined, [404]);
        const latencies = Array.isArray(toxics)
            ? (toxics as Toxic[]).map(x => `${x.stream} ${x.attributes.latency}±${x.attributes.jitter}ms`)
            : ['no proxy'];
        lines.push(`${proxy.name} ${proxy.listen} -> ${proxy.upstream}: ${latencies.join(', ') || 'no latency'}`);
    }
    return lines.join('\n');
}

// Private methods

function getProxies(slot: Slot): ProxySpec[] {
    const listenPort = slot.port + ProxyPortShift;
    const newProxy = (name: string, listen: number, upstream: number): ProxySpec => ({
        name,
        listen: `0.0.0.0:${listen}`,
        upstream: `host.docker.internal:${upstream}`,
    });
    return [
        newProxy(slot.name, listenPort, slot.port),
        newProxy(`${slot.name}-h2`, listenPort + 1, slot.port + 1),
    ];
}

async function ensureToxiproxy(slot: Slot): Promise<void> {
    if (await isToxiproxyUp())
        return;

    // Run from the main repo, whose .env holds the variables the other services interpolate
    execFileSync('docker', [
        'compose',
        '--project-directory', slot.mainRepo,
        '-f', path.resolve(process.cwd(), 'docker-compose.yml'),
        '--profile', 'harness',
        'up', '-d', 'toxiproxy',
    ], { stdio: 'inherit' });
    const deadline = Date.now() + 30_000;
    while (Date.now() < deadline) {
        if (await isToxiproxyUp())
            return;

        await new Promise(resolve => setTimeout(resolve, 500));
    }
    throw new Error(`net: Toxiproxy didn't answer on ${ToxiproxyUrl} within 30s.`);
}

async function isToxiproxyUp(): Promise<boolean> {
    try {
        const response = await fetch(`${ToxiproxyUrl}/version`);
        return response.ok;
    }
    catch {
        return false;
    }
}

async function upsertProxy(proxy: ProxySpec): Promise<void> {
    const existing = await callToxiproxy('GET', `/proxies/${proxy.name}`, undefined, [404]);
    if (existing)
        await callToxiproxy('POST', `/proxies/${proxy.name}`, { ...proxy, enabled: true });
    else
        await callToxiproxy('POST', '/proxies', { ...proxy, enabled: true });
}

async function upsertLatency(proxyName: string, stream: typeof Streams[number], shape: Shape): Promise<void> {
    const toxicName = `latency-${stream}`;
    const attributes = { latency: shape.latencyMs, jitter: shape.jitterMs };
    const existing = await callToxiproxy('GET', `/proxies/${proxyName}/toxics/${toxicName}`, undefined, [404]);
    if (existing)
        await callToxiproxy('POST', `/proxies/${proxyName}/toxics/${toxicName}`, { attributes });
    else
        await callToxiproxy('POST', `/proxies/${proxyName}/toxics`, {
            name: toxicName,
            type: 'latency',
            stream,
            toxicity: 1,
            attributes,
        });
}

// Returns null for a status listed in allowedStatuses rather than throwing
async function callToxiproxy(
    method: string,
    route: string,
    body?: unknown,
    allowedStatuses: number[] = [],
): Promise<unknown> {
    const response = await fetch(`${ToxiproxyUrl}${route}`, {
        method,
        headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
        body: body === undefined ? undefined : JSON.stringify(body),
    });
    if (allowedStatuses.includes(response.status))
        return null;
    if (!response.ok)
        throw new Error(`net: Toxiproxy ${method} ${route} failed: ${response.status} ${await response.text()}`);

    const text = await response.text();
    return text ? JSON.parse(text) : null;
}

// ws.ps1 writes the same two files, in this exact form, when it assigns a slot its port
function writeNginxMap(slot: Slot, port: number): void {
    const folder = path.join(slot.mainRepo, 'artifacts', 'worktree-ports.d');
    fs.writeFileSync(path.join(folder, `${slot.name}.conf`), `"${slot.name}" ${port};`);
    fs.writeFileSync(path.join(folder, `${slot.name}.h2.conf`), `"${port}" ${port + 1};`);
}

function readNginxMapPort(slot: Slot): number | null {
    const file = path.join(slot.mainRepo, 'artifacts', 'worktree-ports.d', `${slot.name}.conf`);
    if (!fs.existsSync(file))
        return null;

    const match = /"[^"]+"\s+(\d+);/.exec(fs.readFileSync(file, 'utf8'));
    return match ? Number(match[1]) : null;
}

function reloadNginx(): void {
    execFileSync('docker', ['exec', NginxContainer, 'nginx', '-t'], { stdio: 'pipe' });
    execFileSync('docker', ['exec', NginxContainer, 'nginx', '-s', 'reload'], { stdio: 'pipe' });
}

function readEnv(filePath: string): Map<string, string> {
    const env = new Map<string, string>();
    if (!fs.existsSync(filePath))
        return env;

    for (const line of fs.readFileSync(filePath, 'utf8').split(/\r?\n/)) {
        const match = /^\s*([\w.]+)\s*=\s*(.*?)\s*$/.exec(line);
        if (match)
            env.set(match[1], match[2].replace(/^"(.*)"$/, '$1'));
    }
    return env;
}

// Nested types

interface ProxySpec {
    name: string;
    listen: string;
    upstream: string;
}

interface Toxic {
    stream: string;
    attributes: { latency: number; jitter: number };
}
