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

import { signIn } from './auth.js';
import { Metrics, printReport } from './metrics.js';
import { runSignalRConsumer, runSignalRProducer } from './signalr-runner.js';
import { discoverStreams, runRpcConsumer, runRpcProducer } from './rpc-runner.js';

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
    const get = (short: string, long: string): string | undefined => {
        const shortPrefix = `-${short}:`;
        const longPrefix = `-${long}:`;
        for (let i = argv.length - 1; i >= 0; i--) {
            const a = argv[i];
            if (a.startsWith(shortPrefix)) return a.slice(shortPrefix.length);
            if (a.startsWith(longPrefix)) return a.slice(longPrefix.length);
        }
        return undefined;
    };
    const flag = (...names: string[]): boolean =>
        argv.some((a) => names.includes(a));

    return {
        chatCount: parseInt(get('c', 'chats') ?? '10', 10),
        streamsPerChat: parseInt(get('s', 'streams') ?? '6', 10),
        consumersPerChat: parseInt(get('n', 'consumers') ?? '6', 10),
        baseUrl: get('u', 'url') ?? 'https://local.voxt.ai',
        durationSec: parseInt(get('d', 'duration') ?? '30', 10),
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
    const rpcWsUrl = `${args.baseUrl.replace(/^http/, 'ws')}/rpc/ws`;
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
        rpcWsUrl,
        email: 'test-videoload@actual.chat',
        totp: 111111,
    });

    // --- Shared state ---
    const metrics = new Metrics();
    const abortController = new AbortController();
    const abort = abortController.signal;

    process.on('SIGINT', () => {
        console.log('\nSIGINT — stopping…');
        abortController.abort();
    });

    // --- Producers ---
    console.log(`Starting ${totalStreams} producers…`);
    const producerTasks: Promise<void>[] = [];
    for (let ci = 0; ci < args.chatCount; ci++) {
        for (let pi = 0; pi < args.streamsPerChat; pi++) {
            producerTasks.push(
                args.useRpc
                    ? runRpcProducer({ rpcWsUrl, sessionId, metrics, abort }, ci, pi, chatIds[ci])
                    : runSignalRProducer({ hubUrl, sessionToken, metrics, abort }, ci, pi, chatIds[ci]),
            );
        }
    }

    // --- Discover streams ---
    console.log('Waiting for streams to appear…');
    const chatStreams = await discoverStreams(
        { rpcWsUrl, sessionId, abort },
        chatIds,
        args.streamsPerChat,
        45_000,
    );
    console.log(`All ${totalStreams} streams discovered across ${args.chatCount} chats.`);

    // --- Consumers ---
    console.log(`Starting ${totalPulls} consumer pulls…`);
    const consumerTasks: Promise<void>[] = [];
    for (let ci = 0; ci < args.chatCount; ci++) {
        const streams = chatStreams[ci] ?? [];
        for (let cons = 0; cons < args.consumersPerChat; cons++) {
            for (let si = 0; si < streams.length; si++) {
                if (si === cons) continue; // consumer N skips stream N
                metrics.initConsumer(ci, cons, si);
                const streamId = streams[si];
                consumerTasks.push(
                    args.useRpc
                        ? runRpcConsumer({ rpcWsUrl, sessionId, metrics, abort }, ci, cons, si, streamId)
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
