// TypeScript port of App.VideoLoadTest — measures push/pull video throughput
// and latency via the same wire protocols the browser uses (SignalR
// /api/hub/streams or Fusion RPC /rpc/ws) so we can compare TS client numbers
// directly against the C# harness results.
//
// Usage (from repo root, after `npm install`):
//   npx tsx src/nodejs/app.video-load-test/index.ts [-c:10] [-s:6] [-n:6] \
//       [-u:https://local.voxt.ai] [-d:30] [-rpc]
//
// Flags mirror the C# App.VideoLoadTest:
//   -c:N   chat count (default 10; uses hard-coded chat IDs)
//   -s:N   streams per chat (default 6)
//   -n:N   consumers per chat (default 6)
//   -u:URL base URL (default https://local.voxt.ai)
//   -d:SEC test duration (default 30)
//   -rpc   use Fusion RPC transport (default: SignalR)

// --- Dev-only TLS bypass -------------------------------------------------
// local.voxt.ai uses a self-signed / mkcert dev cert that Node's default CA
// bundle does not trust. Without this, Node's `ws` package refuses the TLS
// handshake and the RPC sign-in hangs forever waiting for a WebSocket that
// silently failed to open. We bypass TLS verification only for obviously-dev
// hosts; anything else keeps normal verification.
// NEVER enable this in production code — this process is a load-test harness
// exclusively.
const DEV_HOST_PATTERNS = ['local.voxt.ai', 'localhost', '127.0.0.1'];
const _rawBase = process.argv.find((a) => a.startsWith('-u:') || a.startsWith('-url:'))
    ?? process.argv.find((a) => a.startsWith('--url='))
    ?? 'https://local.voxt.ai';
const _baseForTls = _rawBase.replace(/^(-u:|-url:|--url=)/, '');
if (_baseForTls.startsWith('http://') ||
    DEV_HOST_PATTERNS.some((h) => _baseForTls.includes(h))) {
    process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';
    console.warn('[load-test] NODE_TLS_REJECT_UNAUTHORIZED=0 — dev cert bypass active');
}

// --- Silent-error surfacing ----------------------------------------------
// The RPC client's run loop swallows most connection errors into an
// exponential backoff; without these, TLS / WS / dispatch failures hang
// without any output. Print them so we can see what's wrong.
process.on('unhandledRejection', (reason) => {
    console.error('[unhandledRejection]', reason);
});
process.on('uncaughtException', (err) => {
    console.error('[uncaughtException]', err);
});

import { signIn } from './auth.js';
import { Metrics, printReport } from './metrics.js';
import { FrameConfig } from './frame-gen.js';
import { runSignalRConsumer, runSignalRProducer } from './signalr-runner.js';
import {
    createRpcHarnessBundle,
    closeRpcHarnessBundle,
    discoverStreams,
    runRpcConsumer,
    runRpcProducer,
    type RpcHarnessBundle,
} from './rpc-runner.js';

// Chat IDs copied verbatim from src/dotnet/App.VideoLoadTest/Program.cs — the
// user created these once locally; do not regenerate per run.
const DEFAULT_CHAT_IDS: readonly string[] = [
    'zqMxFSJWkS',
    'weFGfFJNgy',
    'uKWaKUGZmv',
    'xPUTIYhnMJ',
    'Q5wNxJIeVD',
    '3USxtLtliz',
    'pzOCDCChRR',
    'D0S4FShrsu',
    'mMOf4Lj0gw',
    'NcnBmEfc5e',
];

interface CliArgs {
    chatCount: number;
    streamsPerChat: number;
    consumersPerChat: number;
    baseUrl: string;
    durationSec: number;
    useRpc: boolean;
}

function parseArgs(argv: readonly string[]): CliArgs {
    // Accept all four common forms for every flag so copy/paste and shell
    // quirks (looking at you, PowerShell splitting `-c:5` into `-c:` and
    // `5`) don't silently give us NaN configs:
    //   -c:5      short-colon
    //   -c=5      short-equals
    //   -c 5      short-space   (two tokens)
    //   --chats=5 long-equals
    //   --chats 5 long-space
    const get = (short: string, long: string): string | undefined => {
        const prefixes = [
            `-${short}:`, `-${short}=`,
            `--${long}=`, `--${long}:`, `-${long}:`, `-${long}=`,
        ];
        for (let i = argv.length - 1; i >= 0; i--) {
            const a = argv[i];
            for (const prefix of prefixes) {
                if (a.startsWith(prefix)) {
                    const tail = a.slice(prefix.length);
                    // Joined form: "-c:5" → "5". Bare prefix (PowerShell
                    // splits on colon): "-c:" + next arg → "-c:" then "5".
                    if (tail.length > 0) return tail;
                    if (i + 1 < argv.length) return argv[i + 1];
                    return undefined;
                }
            }
            if (a === `-${short}` || a === `--${long}`) {
                return i + 1 < argv.length ? argv[i + 1] : undefined;
            }
        }
        return undefined;
    };
    const parseIntOr = (value: string | undefined, fallback: number, name: string): number => {
        if (value === undefined) return fallback;
        const n = parseInt(value, 10);
        if (Number.isNaN(n)) {
            console.error(`[load-test] Could not parse ${name}=${JSON.stringify(value)}; using default ${fallback}`);
            return fallback;
        }
        return n;
    };
    const flag = (...names: string[]): boolean =>
        argv.some((a) => names.includes(a));

    return {
        chatCount: parseIntOr(get('c', 'chats'), 10, '-c/--chats'),
        streamsPerChat: parseIntOr(get('s', 'streams'), 6, '-s/--streams'),
        consumersPerChat: parseIntOr(get('n', 'consumers'), 6, '-n/--consumers'),
        baseUrl: get('u', 'url') ?? 'https://local.voxt.ai',
        durationSec: parseIntOr(get('d', 'duration'), 30, '-d/--duration'),
        useRpc: flag('-rpc', '--rpc'),
    };
}

async function main(): Promise<void> {
    const args = parseArgs(process.argv.slice(2));
    if (args.chatCount > DEFAULT_CHAT_IDS.length) {
        throw new Error(
            `chatCount=${args.chatCount} exceeds the ${DEFAULT_CHAT_IDS.length} hard-coded chat IDs. ` +
            'Add more IDs to DEFAULT_CHAT_IDS or pass a smaller -c:N.');
    }
    const chatIds = DEFAULT_CHAT_IDS.slice(0, args.chatCount);

    const hubUrl = `${args.baseUrl}/api/hub/streams`;
    const apiUrl = `${args.baseUrl.replace(/^http/, 'ws')}/rpc/ws`;
    const mode = args.useRpc ? 'Fusion RPC' : 'SignalR';
    const totalStreams = args.chatCount * args.streamsPerChat;
    const pullsPerChat = args.consumersPerChat * (args.streamsPerChat - 1);
    const totalPulls = args.chatCount * pullsPerChat;

    console.log(`Video Load Test [${mode}]: ` +
        `${args.chatCount} chats × ${args.streamsPerChat} streams × ${args.consumersPerChat} consumers`);
    console.log(`  ${totalStreams} total streams, ${totalPulls} total pulls (${pullsPerChat} per chat)`);
    console.log(`  Base URL: ${args.baseUrl}, Duration: ${args.durationSec}s`);

    // --- Authentication ---
    const { sessionId, sessionToken } = await signIn({
        apiUrl,
        email: 'test-videoload@actual.chat',
        totp: 111111,
    });

    // --- Shared state ---
    // Size the typed arrays with 50% headroom over the nominal 30 fps run so
    // a producer that briefly runs ahead of schedule (or a slightly long test
    // duration) never drops samples on the floor.
    const nominalFramesPerStream = args.durationSec * 30;
    const metrics = new Metrics({
        chatCount: args.chatCount,
        streamsPerChat: args.streamsPerChat,
        consumersPerChat: args.consumersPerChat,
        maxFramesPerStream: Math.ceil(nominalFramesPerStream * 1.5) + 64,
        frameDurationTicks: FrameConfig.FrameDurationTicks,
        maxLatencySamplesPerConsumer: Math.ceil(nominalFramesPerStream * 1.5) + 64,
    });
    const abortController = new AbortController();
    const abort = abortController.signal;

    process.on('SIGINT', () => {
        console.log('\nSIGINT — stopping…');
        abortController.abort();
    });

    // --- Per-chat RPC harness bundles ---
    // One shared `RpcClientPeer` / WebSocket per chat — mirrors how a real
    // browser client would connect (each open chat room is its own logical
    // peer). With 300 pulls on a single shared peer the server's per-peer
    // outbound channel became the bottleneck (p50 ~5 s behind real time);
    // splitting per chat gives the server N parallel peer loops to drain
    // into, and gives libuv multiple sockets to batch reads from.
    let rpcBundles: RpcHarnessBundle[] = [];
    if (args.useRpc) {
        rpcBundles = await Promise.all(
            chatIds.map(() => createRpcHarnessBundle({ apiUrl, sessionId, abort })),
        );
    }

    // --- Producers ---
    console.log(`Starting ${totalStreams} producers…`);
    const producerTasks: Promise<void>[] = [];
    for (let ci = 0; ci < args.chatCount; ci++) {
        for (let pi = 0; pi < args.streamsPerChat; pi++) {
            producerTasks.push(
                args.useRpc
                    ? runRpcProducer(rpcBundles[ci], { apiUrl, sessionId, metrics, abort }, ci, pi, chatIds[ci])
                    : runSignalRProducer({ hubUrl, sessionToken, metrics, abort }, ci, pi, chatIds[ci]),
            );
        }
    }

    // --- Discover streams ---
    console.log('Waiting for streams to appear…');
    // Discovery path is RPC-only; reuse chat 0's bundle if we have one,
    // otherwise build a throwaway bundle just for discovery.
    const discoveryBundle = rpcBundles[0]
        ?? await createRpcHarnessBundle({ apiUrl, sessionId, abort });
    const chatStreams = await discoverStreams(
        discoveryBundle,
        { apiUrl, sessionId, abort },
        chatIds,
        args.streamsPerChat,
        45_000,
    );
    if (rpcBundles.length === 0) closeRpcHarnessBundle(discoveryBundle);
    console.log(`All ${totalStreams} streams discovered across ${args.chatCount} chats.`);

    // --- Consumers ---
    console.log(`Starting ${totalPulls} consumer pulls…`);
    const consumerTasks: Promise<void>[] = [];
    for (let ci = 0; ci < args.chatCount; ci++) {
        const streams = chatStreams[ci] ?? [];
        for (let cons = 0; cons < args.consumersPerChat; cons++) {
            for (let si = 0; si < streams.length; si++) {
                if (si === cons) continue; // consumer N skips stream N
                const streamId = streams[si];
                consumerTasks.push(
                    args.useRpc
                        ? runRpcConsumer(rpcBundles[ci], { apiUrl, sessionId, metrics, abort }, ci, cons, si, streamId)
                        : runSignalRConsumer({ hubUrl, sessionToken, metrics, abort }, ci, cons, si, streamId),
                );
            }
        }
    }

    // --- Run for duration ---
    console.log(`Running for ${args.durationSec}s… (Ctrl+C to stop early)`);
    await new Promise<void>((resolve) => {
        const timer = setTimeout(() => resolve(), args.durationSec * 1_000);
        abort.addEventListener('abort', () => { clearTimeout(timer); resolve(); }, { once: true });
    });

    console.log('Stopping…');
    abortController.abort();
    await Promise.allSettled([...producerTasks, ...consumerTasks]);

    for (const b of rpcBundles) closeRpcHarnessBundle(b);

    // --- Report ---
    printReport({
        mode,
        durationSec: args.durationSec,
        chatCount: args.chatCount,
        streamsPerChat: args.streamsPerChat,
        consumersPerChat: args.consumersPerChat,
        metrics,
    });
}

main().catch((err: unknown) => {
    console.error(err);
    process.exit(1);
});
