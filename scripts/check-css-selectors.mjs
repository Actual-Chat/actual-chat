// Fails when a custom CSS rule names, in its selector, a class that many other rules @apply.
//
// Tailwind treats every rule whose selector mentions `.hidden` as a part of the `hidden` utility.
// So for each rule that says `@apply hidden`, it re-emits that custom rule too, with `.hidden`
// replaced by the applying rule's whole selector. One selector naming `.hidden` becomes one copy
// per `@apply hidden` in the code base - 182 at the time of writing - and the copies are valid CSS,
// so nothing complains. Twice that produced hundreds of `:has()` selectors WebKit cannot scope,
// which made every DOM insertion re-match the whole document (#5261).
//
// A class applied by fewer than MinApplyCount rules is not reported: a handful of copies of a short
// rule costs nothing, and `i.animate-spin` is an ordinary thing to write. The check runs over the
// whole code base on every build, so a rule starts failing the build the day its class crosses the
// line.
//
// Usage: node scripts/check-css-selectors.mjs [file.css ...]
//   With no files, every source stylesheet is checked. With files, only those are reported on;
//   the set of applied utilities always comes from the whole code base.

import { readdirSync, readFileSync } from 'node:fs';
import { join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import postcss from 'postcss';

const RepoRoot = resolve(fileURLToPath(import.meta.url), '..', '..');
const SourceRoots = ['src/nodejs/styles', 'src/dotnet'];
const SkippedDirs = new Set(['dist', 'wwwroot', 'bin', 'obj', 'node_modules', 'artifacts']);
const ClassToken = /\.(-?(?:\\.|[A-Za-z_])(?:\\.|[\w-])*)/g;

export const MinApplyCount = 10;

/** Every source stylesheet of the web bundle, as absolute paths. */
export function findSourceFiles(repoRoot = RepoRoot) {
    const files = [];
    for (const root of SourceRoots)
        walk(join(repoRoot, root));

    return files.sort();

    function walk(dir) {
        for (const entry of readdirSync(dir, { withFileTypes: true })) {
            if (entry.isDirectory()) {
                if (!SkippedDirs.has(entry.name))
                    walk(join(dir, entry.name));
            }
            else if (entry.name.endsWith('.css'))
                files.push(join(dir, entry.name));
        }
    }
}

/** Utility name -> the number of `@apply`s that name it, across the given CSS texts. */
export function collectAppliedUtilities(texts) {
    const applied = new Map();
    for (const text of texts) {
        postcss.parse(text).walkAtRules('apply', atRule => {
            for (const token of atRule.params.split(/\s+/)) {
                const name = toUtilityName(token);
                if (name)
                    applied.set(name, (applied.get(name) ?? 0) + 1);
            }
        });
    }

    return applied;
}

/**
 * The selectors of `text` that name a utility applied by at least `minApplyCount` rules. A selector
 * that is nothing but the class (`.animate-pulse`, or `.theme-light` in `:root, .theme-light`) is a
 * definition and is left alone, and so is everything inside `@layer`, where the code base declares
 * its own utilities.
 */
export function findUtilitySelectors(text, applied, minApplyCount = MinApplyCount) {
    const findings = [];
    postcss.parse(text).walkRules(rule => {
        if (isInside(rule, 'layer') || isInside(rule, 'keyframes'))
            return;

        const selectorList = rule.raws.selector?.raw ?? rule.selector;
        for (const { selector, lineOffset } of splitSelectorList(selectorList)) {
            for (const className of new Set(classNamesOf(selector))) {
                const applyCount = applied.get(className);
                if (applyCount >= minApplyCount && selector !== `.${className}`)
                    findings.push({ line: rule.source.start.line + lineOffset, selector, className, applyCount });
            }
        }
    });

    return findings;
}

export function describeFinding(finding) {
    const { selector, className, applyCount } = finding;
    const times = applyCount === 1 ? '1 rule' : `${applyCount} rules`;

    return `"${selector}" names .${className}, which ${times} @apply: the build re-emits this rule once for each `
        + `of them, with .${className} replaced by that rule's selector. Use a state class or a data- attribute of `
        + `your own instead.`;
}

/** The applied utilities of the whole code base - what a single file is checked against. */
export function readAppliedUtilities(repoRoot = RepoRoot) {
    return collectAppliedUtilities(findSourceFiles(repoRoot).map(file => readFileSync(file, 'utf8')));
}

/** Findings for the given files (all source files when none are given), as formatted lines. */
export function checkFiles(paths = [], repoRoot = RepoRoot) {
    const applied = readAppliedUtilities(repoRoot);
    const checkedFiles = paths.length === 0 ? findSourceFiles(repoRoot) : paths.map(path => resolve(path));
    const lines = [];
    for (const file of checkedFiles) {
        const path = relative(repoRoot, file).replaceAll('\\', '/');
        for (const finding of findUtilitySelectors(readFileSync(file, 'utf8'), applied))
            lines.push(`${path}:${finding.line} — ${describeFinding(finding)}`);
    }

    return lines;
}

// Private methods

function toUtilityName(token) {
    // "!hidden", "hidden!" and the "!important" flag; "md:hover:hidden" applies `hidden` too
    const bare = token.replace(/^!|!$/g, '');
    if (bare === '' || bare === 'important')
        return '';

    let depth = 0;
    let start = 0;
    for (let i = 0; i < bare.length; i++) {
        const c = bare[i];
        if (c === '[')
            depth++;
        else if (c === ']')
            depth--;
        else if (c === ':' && depth === 0)
            start = i + 1;
    }

    return bare.slice(start);
}

function isInside(node, atRuleName) {
    for (let parent = node.parent; parent; parent = parent.parent)
        if (parent.type === 'atrule' && parent.name === atRuleName)
            return true;

    return false;
}

// Splits on top-level commas, keeping how many lines precede each selector
function splitSelectorList(selectorList) {
    const items = [];
    let depth = 0;
    let start = 0;
    for (let i = 0; i <= selectorList.length; i++) {
        const c = selectorList[i];
        if (c === '(' || c === '[')
            depth++;
        else if (c === ')' || c === ']')
            depth--;
        if (i < selectorList.length && !(c === ',' && depth === 0))
            continue;

        const raw = selectorList.slice(start, i);
        const leading = raw.length - raw.trimStart().length;
        const lineOffset = (selectorList.slice(0, start + leading).match(/\n/g) ?? []).length;
        const selector = raw.trim().replace(/\s+/g, ' ');
        if (selector !== '')
            items.push({ selector, lineOffset });
        start = i + 1;
    }

    return items;
}

function classNamesOf(selector) {
    // Attribute selectors and strings hold dots that are not classes: [href$=".pdf"]
    const bare = selector.replace(/\[[^\]]*\]/g, '').replace(/(["'])(?:\\.|(?!\1).)*\1/g, '');

    return [...bare.matchAll(ClassToken)].map(match => match[1].replace(/\\(.)/g, '$1'));
}

if (process.argv[1] !== undefined && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const lines = checkFiles(process.argv.slice(2));
    if (lines.length !== 0) {
        console.error(`CSS selectors that name an @apply'd utility class (docs/ui/components.md):\n`);
        console.error(lines.join('\n\n'));
        process.exit(1);
    }
}
