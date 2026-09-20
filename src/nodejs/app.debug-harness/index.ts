import * as fs from 'fs';
import * as path from 'path';
import { attachFilePath, getLastMessageText, getPeerChatId, openChat, sendMessage } from './chat';
import { DefaultClips, enableMicrophone, say, startRecording, stopRecording } from './microphone';
import { describeShaping, disableShaping, enableShaping, getSlot, setShape } from './net';
import type { DebugUIRoot } from './debug-ui-api';
import { getRecorderState } from './page-state';
import { closeSession, openBrowser, signIn, type BrowserSession } from './session';

const DefaultPhoneA = '+1 555 555 5550';
const DefaultPhoneB = '+1 555 555 5551';
// Longer than the VAD's longest pause, so the last utterance is closed into an entry
const TrailingSilenceMs = 4_000;

const usage = `
Usage: npm run harness -- <scenario> [options]

Scenarios:
  send-message   --text <text> [--chat <chatId>]
  attach-image   --file <path> [--chat <chatId>]
  two-users      [--text <text>] [--file <path>]
  two-speakers   [--rounds <n>] [--gap <ms>] [--overlap <ms>] [--clips <a,b,...>] [--language <code>]
                 Both users record; the fake microphone makes them talk in turns
  net on         [--latency <ms>] [--jitter <ms>]   route this slot through Toxiproxy
  net set        [--latency <ms>] [--jitter <ms>]   change the latency on the fly
  net off                                           route it straight to Kestrel again
  net status

Options:
  --port <n>     CDP port of the first Chrome (default 9222)
  --port2 <n>    CDP port of the second Chrome (default 9223)
  --user <id>    phone or email for the first user (default "${DefaultPhoneA}")
  --user2 <id>   phone or email for the second user (default "${DefaultPhoneB}")
  --chat <id>    chat to open; two-users derives the peer chat when omitted
  --fresh        sign out first instead of reusing the signed-in account
  --rounds <n>   two-speakers: turns per speaker (default 3)
  --gap <ms>     two-speakers: silence between turns (default 800)
  --overlap <ms> two-speakers: start each turn this long before the previous one ends
  --clips <list> two-speakers: clip names, cycled (default ${Object.keys(DefaultClips).join(',')})
  --language <code> two-speakers: the chat's transcription language (default ru-RU)
  --latency <ms> net: delay added in each direction, so a round trip gets twice it (default 150)
  --jitter <ms>  net: random +- added to the latency (default 0)
`;

void main();

// Private methods

async function main(): Promise<void> {
    const args = parseArgs(process.argv.slice(2));
    const scenario = args.positional[0];
    if (!scenario || args.flags.has('help')) {
        console.log(usage);
        process.exit(scenario ? 0 : 1);
    }
    const sessions: BrowserSession[] = [];
    try {
        switch (scenario) {
        case 'send-message':
            await runSendMessage(args, sessions);
            break;
        case 'attach-image':
            await runAttachImage(args, sessions);
            break;
        case 'two-users':
            await runTwoUsers(args, sessions);
            break;
        case 'two-speakers':
            await runTwoSpeakers(args, sessions);
            break;
        case 'net':
            await runNet(args);
            break;
        default:
            console.error(`Unknown scenario: ${scenario}`);
            console.log(usage);
            process.exit(1);
        }
    } finally {
        for (const session of sessions)
            await closeSession(session);
    }
}

async function runSendMessage(args: Args, sessions: BrowserSession[]): Promise<void> {
    const text = args.options.get('text') ?? `harness ${new Date().toISOString()}`;
    const session = await open(args, sessions, 'port', 'user', 9222, DefaultPhoneA);
    await openChat(session.page, requireOption(args, 'chat'));
    await sendMessage(session.page, text);
    await session.page.waitForTimeout(1_000);
    console.log(`last message: ${await getLastMessageText(session.page)}`);
}

async function runAttachImage(args: Args, sessions: BrowserSession[]): Promise<void> {
    const file = path.resolve(requireOption(args, 'file'));
    if (!fs.existsSync(file))
        throw new Error(`No such file: ${file}`);

    const session = await open(args, sessions, 'port', 'user', 9222, DefaultPhoneA);
    await openChat(session.page, requireOption(args, 'chat'));
    await attachFilePath(session.page, file);
    await sendMessage(session.page, args.options.get('text') ?? '');
    console.log(`attached ${path.basename(file)}`);
}

async function runTwoUsers(args: Args, sessions: BrowserSession[]): Promise<void> {
    const a = await open(args, sessions, 'port', 'user', 9222, DefaultPhoneA);
    const b = await open(args, sessions, 'port2', 'user2', 9223, DefaultPhoneB);
    const chatId = args.options.get('chat') ?? getPeerChatId(a.userId, b.userId);
    console.log(`users: ${a.userId} / ${b.userId}, chat: ${chatId}`);
    await openChat(a.page, chatId);
    await openChat(b.page, chatId);

    const text = args.options.get('text') ?? `harness ${new Date().toISOString()}`;
    await sendMessage(a.page, `${text} (from A)`);
    await sendMessage(b.page, `${text} (from B)`);
    const file = args.options.get('file');
    if (file) {
        await attachFilePath(a.page, path.resolve(file));
        await sendMessage(a.page, 'with an attachment');
    }
    await b.page.waitForTimeout(1_000);
    console.log(`B sees: ${await getLastMessageText(b.page)}`);
}

async function runTwoSpeakers(args: Args, sessions: BrowserSession[]): Promise<void> {
    const rounds = Number(args.options.get('rounds') ?? 3);
    const gapMs = Number(args.options.get('gap') ?? 800);
    const overlapMs = Number(args.options.get('overlap') ?? 0);
    const clips = (args.options.get('clips') ?? Object.keys(DefaultClips).join(',')).split(',');
    const unknownClip = clips.find(x => !(x in DefaultClips));
    if (unknownClip)
        throw new Error(`Unknown clip '${unknownClip}' - pick from ${Object.keys(DefaultClips).join(', ')}.`);

    const a = await open(args, sessions, 'port', 'user', 9222, DefaultPhoneA);
    const b = await open(args, sessions, 'port2', 'user2', 9223, DefaultPhoneB);
    const chatId = args.options.get('chat') ?? getPeerChatId(a.userId, b.userId);
    console.log(`users: ${a.userId} / ${b.userId}, chat: ${chatId}`);
    await openChat(a.page, chatId);
    await openChat(b.page, chatId);
    await ensureCanRecord(a, b);
    // The clips are Russian, and language detection guesses badly on short utterances
    const language = args.options.get('language') ?? 'ru-RU';
    for (const page of [a.page, b.page])
        await page.evaluate(
            ({ chat, lang }) => (globalThis as DebugUIRoot).debugUI!.setChatLanguage(chat, lang),
            { chat: chatId, lang: language });
    const durations = await enableMicrophone(a.page);
    await enableMicrophone(b.page);

    // Recording is billed per second, so it's stopped on Ctrl+C too, not only in finally
    const stopBoth = () => Promise.all([stopRecording(a.page), stopRecording(b.page)]);
    const onInterrupt = () => void stopBoth().finally(() => process.exit(130));
    process.once('SIGINT', onInterrupt);
    try {
        await startRecording(a.page);
        await startRecording(b.page);
        const speeches: Promise<number>[] = [];
        for (let turn = 0; turn < rounds * 2; turn++) {
            const speaker = turn % 2 === 0 ? a : b;
            const clip = clips[turn % clips.length];
            console.log(`${speaker === a ? 'A' : 'B'} says '${clip}'`);
            const speech = say(speaker.page, clip);
            // Awaited below; an unhandled rejection meanwhile would exit past finally with the mic on
            speech.catch(() => undefined);
            speeches.push(speech);
            const clipMs = durations.get(clip)! * 1000;
            await speaker.page.waitForTimeout(overlapMs > 0 ? Math.max(0, clipMs - overlapMs) : clipMs + gapMs);
        }
        await Promise.all(speeches);
        await a.page.waitForTimeout(TrailingSilenceMs);
    }
    finally {
        process.off('SIGINT', onInterrupt);
        await stopBoth();
    }
    console.log(`done: ${rounds * 2} utterances in ${chatId}`);
}

/**
 * A peer chat strips audio until the recipient adds the sender to contacts or replies,
 * so two fresh users get no recorder until each has written once.
 */
async function ensureCanRecord(a: BrowserSession, b: BrowserSession): Promise<void> {
    const pages = [a.page, b.page];
    const hasRecorder = () => Promise.all(pages.map(async x => await getRecorderState(x) !== 'no-btn'));
    if ((await hasRecorder()).every(x => x))
        return;

    console.log('no recorder yet - exchanging a text message to unlock audio');
    await sendMessage(a.page, 'harness: hi from A');
    await sendMessage(b.page, 'harness: hi from B');
    await Promise.all(pages.map(x => x.locator('.rec-btn').waitFor({ timeout: 15_000 })));
}

async function runNet(args: Args): Promise<void> {
    const command = args.positional[1] ?? 'status';
    const slot = getSlot();
    const shape = {
        latencyMs: Number(args.options.get('latency') ?? 150),
        jitterMs: Number(args.options.get('jitter') ?? 0),
    };
    switch (command) {
    case 'on':
        await enableShaping(slot, shape);
        break;
    case 'set':
        await setShape(slot, shape);
        break;
    case 'off':
        await disableShaping(slot);
        break;
    case 'status':
        break;
    default:
        throw new Error(`Unknown net command: ${command} - use on, set, off or status.`);
    }
    console.log(await describeShaping(slot));
}

async function open(
    args: Args,
    sessions: BrowserSession[],
    portOption: string,
    userOption: string,
    defaultPort: number,
    defaultUser: string,
): Promise<BrowserSession & { userId: string }> {
    const port = Number(args.options.get(portOption) ?? defaultPort);
    const session = await openBrowser(port);
    sessions.push(session);
    const userId = await signIn(session.page, args.options.get(userOption) ?? defaultUser, args.flags.has('fresh'));
    return { ...session, userId };
}

function requireOption(args: Args, name: string): string {
    const value = args.options.get(name);
    if (!value)
        throw new Error(`--${name} is required.`);

    return value;
}

function parseArgs(argv: string[]): Args {
    const positional: string[] = [];
    const options = new Map<string, string>();
    const flags = new Set<string>();
    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        if (!arg.startsWith('--')) {
            positional.push(arg);
            continue;
        }
        const name = arg.slice(2);
        if (i + 1 >= argv.length || argv[i + 1].startsWith('--')) {
            flags.add(name);
            continue;
        }
        options.set(name, argv[i + 1]);
        i++;
    }
    return { positional, options, flags };
}

// Nested types

interface Args {
    positional: string[];
    options: Map<string, string>;
    flags: Set<string>;
}
