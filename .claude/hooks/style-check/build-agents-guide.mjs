// Builds docs/CODING_STYLE.AGENTS.md — the style guide minus what style-check.mjs checks itself.
// `--check` verifies the committed file is up to date instead of writing it; the Pester tests run that.
import { readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const SOURCE = 'docs/CODING_STYLE.md';
const TARGET = 'docs/CODING_STYLE.AGENTS.md';
const BEGIN = '<!-- script-checked:begin -->';
const END = '<!-- script-checked:end -->';

const HEADER = `<!--
Generated from CODING_STYLE.md by .claude/hooks/style-check/build-agents-guide.mjs — do not edit.
Regenerate: node .claude/hooks/style-check/build-agents-guide.mjs
-->

> The style guide minus the rules \`.claude/hooks/style-check/style-check.mjs\` checks on every edit.
> They are gone on purpose: the script counts characters and positions exactly and reports them the
> moment the file is written, so a second opinion on them is only ever a worse one. What is left here
> is yours to check, all of it.

`;

export function stripScriptChecked(text) {
    const lines = text.split('\n');
    const kept = [];
    let dropping = false;
    for (const [index, line] of lines.entries()) {
        const trimmed = line.trim();
        if (trimmed === BEGIN) {
            if (dropping)
                throw new Error(`${BEGIN} inside another one at line ${index + 1}`);

            dropping = true;
            continue;
        }
        if (trimmed === END) {
            if (!dropping)
                throw new Error(`${END} without a matching begin at line ${index + 1}`);

            dropping = false;
            continue;
        }
        if (!dropping)
            kept.push(line);
    }
    if (dropping)
        throw new Error(`${BEGIN} is never closed`);

    return collapseBlankRuns(dropEmptyHeadings(kept)).join('\n');
}

// A heading whose whole body was script-checked would otherwise stay behind with nothing under it.
// One followed by a deeper heading still has content, so only a same-or-higher one makes it empty
function dropEmptyHeadings(lines) {
    const level = line => /^(#{1,6}) /.exec(line)?.[1]?.length ?? 0;
    const kept = [];
    for (let i = 0; i < lines.length; i++) {
        const own = level(lines[i]);
        if (own === 0 || isFence(lines, i)) {
            kept.push(lines[i]);
            continue;
        }

        let next = i + 1;
        while (next < lines.length && lines[next].trim() === '')
            next++;
        if (next < lines.length && level(lines[next]) !== 0 && level(lines[next]) <= own) {
            i = next - 1;
            continue;
        }

        kept.push(lines[i]);
    }

    return kept;
}

function collapseBlankRuns(lines) {
    const kept = [];
    for (let i = 0; i < lines.length; i++) {
        const isBlank = lines[i].trim() === '';
        if (isBlank && !isFence(lines, i) && kept.at(-1)?.trim() === '')
            continue;

        kept.push(lines[i]);
    }

    return kept;
}

// A `#` or a blank line inside a fenced block is content, not markup
function isFence(lines, index) {
    let open = false;
    for (let i = 0; i < index; i++) {
        if (/^\s*```/.test(lines[i]))
            open = !open;
    }

    return open;
}

function main() {
    const built = HEADER + stripScriptChecked(readFileSync(SOURCE, 'utf8'));
    if (process.argv.includes('--check')) {
        if (readFileSync(TARGET, 'utf8') === built) {
            console.log(`${TARGET} is up to date`);
            process.exit(0);
        }

        console.error(`${TARGET} is stale — run: node .claude/hooks/style-check/build-agents-guide.mjs`);
        process.exit(1);
    }

    writeFileSync(TARGET, built);
    console.log(`Wrote ${TARGET}`);
}

// Importing the module — the tests do — must not rewrite the guide
if (process.argv[1] !== undefined && resolve(process.argv[1]) === fileURLToPath(import.meta.url))
    main();
