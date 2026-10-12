import { spawnSync } from 'node:child_process';
import { appendFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const SKIPPED = ['Generated', 'obj', 'bin', 'artifacts', 'node_modules'];
const CHECKED_EXTENSIONS = ['.cs', '.ts', '.razor', '.css'];

const CONTROL_FLOW = String.raw`(?:return|throw|break|continue|goto|yield)\b`;
const SAME_LINE_CONTROL_FLOW = new RegExp(
    String.raw`^\s*(?:(?:if|else if|for|foreach|while)\s*\(.*\)|else|(?:case\b.*|default)\s*:)\s*${CONTROL_FLOW}`);
const CONTROL_FLOW_STATEMENT = new RegExp(String.raw`^\s*${CONTROL_FLOW}`);
const BLANK_LINE_EXEMPT_NEXT = /^\s*(?:\}|#|case\b|default\s*:|else\b|catch\b|finally\b|\)|,)/;
const DECLARATION_BRACE = new RegExp(
    String.raw`^\s*(?:(?:public|private|protected|internal|override)\s)[^=;]*\)\s*(?:where[^{]*)?\{\s*$`);
const TYPE_BRACE = /^\s*[\w\s]*\b(?:class|record|struct|interface|enum)\b[^=;]*\{\s*$/;
const ATTRIBUTE_WITH_MEMBER = /^\s*(\[[^\]]+\])\s+\S/;
const USING_DIRECTIVE = /^\s*using\s+[\w.@=\s]+;\s*$/;
const NAMESPACE_LINE = /^\s*namespace\s+[\w.]+\s*(\{)?\s*$/;

export function isCheckedFile(filePath) {
    if (!filePath)
        return false;

    const normalized = filePath.replaceAll('\\', '/');
    if (normalized.endsWith('.g.cs'))
        return false;
    if (normalized.split('/').some(part => SKIPPED.includes(part)))
        return false;

    return CHECKED_EXTENSIONS.some(extension => normalized.endsWith(extension));
}

// `touch tmp/style-check/log-inputs` to capture what the hook actually carries; nothing is logged without it
async function readHookInput() {
    const chunks = [];
    for await (const chunk of process.stdin)
        chunks.push(chunk);

    const raw = Buffer.concat(chunks).toString('utf8');
    if (existsSync(join('tmp', 'style-check', 'log-inputs')))
        appendFileSync(join('tmp', 'style-check', 'inputs.jsonl'), `${raw}\n`);

    return JSON.parse(raw);
}

function logError(error) {
    try {
        mkdirSync('tmp/style-check', { recursive: true });
        appendFileSync('tmp/style-check/errors.log', `${new Date().toISOString()} ${error?.stack ?? error}\n`);
    }
    catch {
        // Nothing left to do: the hook must not fail the tool call it follows
    }
}

// The pre-edit content comes with the hook input, so the check needs nothing of its own to compare against
export function beforeText(input) {
    const response = input?.tool_response ?? {};

    return typeof response.originalFile === 'string' ? response.originalFile : '';
}

async function main() {
    const input = await readHookInput();
    const path = input?.tool_input?.file_path;
    if (!isCheckedFile(path) || !existsSync(path))
        return;

    const text = readFileSync(path, 'utf8');
    const ranges = widenRanges(changedRanges(beforeText(input), text), text.split('\n').length);
    if (ranges.length === 0)
        return;

    const findings = checkMechanical({ path, text, ranges, limit: maxLineLength(dirname(resolve(path))) });
    if (findings.length === 0)
        return;

    const header = 'Style check — fix these in the lines you just changed '
        + '(docs/CODING_STYLE.md; the rest of the file is out of scope):';
    const body = findings.map(finding => `- ${path}:${finding.line} — ${finding.message}`).join('\n');
    process.stdout.write(JSON.stringify({
        hookSpecificOutput: {
            hookEventName: 'PostToolUse',
            additionalContext: `${header}\n${body}`,
        },
    }));
}

export function changedRanges(before, after) {
    if (before === after)
        return [];

    const beforeLines = before.split('\n');
    const afterLines = after.split('\n');
    let prefix = 0;
    while (prefix < beforeLines.length && prefix < afterLines.length && beforeLines[prefix] === afterLines[prefix])
        prefix++;
    let suffix = 0;
    while (suffix < beforeLines.length - prefix
        && suffix < afterLines.length - prefix
        && beforeLines[beforeLines.length - 1 - suffix] === afterLines[afterLines.length - 1 - suffix])
        suffix++;

    // One contiguous region: exact when the trimmed middle is small, an over-approximation otherwise
    const singleRegion = [{ start: prefix + 1, end: Math.max(prefix + 1, afterLines.length - suffix) }];
    const untouched = prefix + suffix;
    if (untouched >= Math.max(beforeLines.length, afterLines.length) - 1)
        return singleRegion;

    return gitRanges(before, after) ?? singleRegion;
}

function gitRanges(before, after) {
    const dir = mkdtempSync(join(tmpdir(), 'style-check-'));
    try {
        const beforePath = join(dir, 'before');
        const afterPath = join(dir, 'after');
        writeFileSync(beforePath, before);
        writeFileSync(afterPath, after);
        const result = spawnSync('git', ['diff', '--no-index', '--unified=0', beforePath, afterPath], {
            encoding: 'utf8',
        });
        if (result.error || result.status > 1)
            return null;

        const ranges = [];
        for (const line of (result.stdout ?? '').split('\n')) {
            const match = /^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@/.exec(line);
            if (!match)
                continue;

            const start = Number(match[1]);
            const count = match[2] === undefined ? 1 : Number(match[2]);
            ranges.push(count === 0
                ? { start: Math.max(1, start), end: Math.max(1, start) }
                : { start, end: start + count - 1 });
        }

        return ranges;
    }
    finally {
        rmSync(dir, { recursive: true, force: true });
    }
}

export function widenRanges(ranges, lineCount, by = 3) {
    const widened = ranges
        .map(range => ({ start: Math.max(1, range.start - by), end: Math.min(lineCount, range.end + by) }))
        .sort((left, right) => left.start - right.start);
    const merged = [];
    for (const range of widened) {
        const last = merged.at(-1);
        if (last && range.start <= last.end + 1)
            last.end = Math.max(last.end, range.end);
        else
            merged.push({ ...range });
    }

    return merged;
}

export function inRanges(ranges, line) {
    return ranges.some(range => line >= range.start && line <= range.end);
}

export function maxLineLength(startDir = process.cwd()) {
    let dir = resolve(startDir);
    for (;;) {
        const candidate = join(dir, '.editorconfig');
        if (existsSync(candidate)) {
            const match = /^\s*max_line_length\s*=\s*(\d+)/m.exec(readFileSync(candidate, 'utf8'));
            if (match)
                return Number(match[1]);
        }

        const parent = dirname(dir);
        if (parent === dir)
            return 120;

        dir = parent;
    }
}

export function checkMechanical({ path, text, ranges, limit }) {
    const normalized = path.replaceAll('\\', '/');
    const isCSharp = normalized.endsWith('.cs');
    const isScript = isCSharp || normalized.endsWith('.ts');
    const lines = text.split('\n');
    const findings = [];
    const add = (line, message) => findings.push({ line, message });
    let namespaceLine = 0;
    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        const number = i + 1;
        if (isCSharp && namespaceLine === 0 && NAMESPACE_LINE.test(line))
            namespaceLine = number;
        if (!inRanges(ranges, number))
            continue;

        if (line.length > limit && !line.includes('://'))
            add(number, `${line.length} chars, max ${limit}`);
        if (isScript && SAME_LINE_CONTROL_FLOW.test(line))
            add(number, 'control-flow statement must be on its own line');
        if (isScript && CONTROL_FLOW_STATEMENT.test(line) && line.trimEnd().endsWith(';')) {
            const next = lines[i + 1];
            const afterNext = lines[i + 2];
            const isGuardRun = next !== undefined
                && /^\s*if\s*\(.*\)\s*$/.test(next)
                && afterNext !== undefined
                && CONTROL_FLOW_STATEMENT.test(afterNext);
            if (next !== undefined && next.trim() !== '' && !BLANK_LINE_EXEMPT_NEXT.test(next) && !isGuardRun)
                add(number, 'a control-flow statement is followed by a blank line');
        }
        if (isCSharp && (DECLARATION_BRACE.test(line) || TYPE_BRACE.test(line)))
            add(number, 'opening brace of a class, method or constructor goes on the next line');
        if (isCSharp && /\bvolatile\b/.test(line) && !line.trimStart().startsWith('//'))
            add(number, 'use Volatile.Read/Write instead of the volatile modifier');
        if (isCSharp && namespaceLine !== 0 && number > namespaceLine && USING_DIRECTIVE.test(line))
            add(number, 'using directives go above the namespace declaration');
        if (isCSharp && NAMESPACE_LINE.test(line) && !line.trimEnd().endsWith(';'))
            add(number, 'use a file-scoped namespace');

        const attribute = ATTRIBUTE_WITH_MEMBER.exec(line);
        if (isCSharp && attribute && attribute[1].length > 70)
            add(number, `attribute is ${attribute[1].length} chars, over 70 — put it on its own line`);
    }

    return findings.sort((left, right) => left.line - right.line);
}

// Importing the module — the tests do — must not run the hook or exit the process
if (process.argv[1] !== undefined && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    try {
        await main();
    }
    catch (error) {
        logError(error);
    }
    process.exit(0);
}
