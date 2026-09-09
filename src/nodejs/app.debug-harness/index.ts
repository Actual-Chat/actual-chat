import * as fs from 'fs';
import * as path from 'path';
import { attachFilePath, getLastMessageText, getPeerChatId, openChat, sendMessage } from './chat';
import { closeSession, openBrowser, signIn, type BrowserSession } from './session';

const DefaultPhoneA = '+1 555 555 5550';
const DefaultPhoneB = '+1 555 555 5551';

const usage = `
Usage: npm run harness -- <scenario> [options]

Scenarios:
  send-message   --text <text> [--chat <chatId>]
  attach-image   --file <path> [--chat <chatId>]
  two-users      [--text <text>] [--file <path>]

Options:
  --port <n>     CDP port of the first Chrome (default 9222)
  --port2 <n>    CDP port of the second Chrome (default 9223)
  --user <id>    phone or email for the first user (default "${DefaultPhoneA}")
  --user2 <id>   phone or email for the second user (default "${DefaultPhoneB}")
  --chat <id>    chat to open; two-users derives the peer chat when omitted
  --fresh        sign out first instead of reusing the signed-in account
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
