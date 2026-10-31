// Fails on CSS conventions the build can check (docs/ui/components.md):
//
// - Physical sides. `ml-2`, `left-0`, `padding-right`, `text-align: left` stay where they are when the
//   page is mirrored for a right-to-left language; `ms-2`, `start-0`, `padding-inline-end` and
//   `text-align: start` follow it. A rule that really means the physical side - a coordinate set from
//   JS, a transform, a device safe area - says so with a `/* physical: <why> */` comment in its body.
//   A declaration whose value names a left/right safe area is physical by definition and needs none,
//   and so is `left: 50%` in a rule that translates the element back by half its width: it centers.
//
// - `.c-*` reached through a descendant combinator. A `c-` class names a part of its component, so it
//   is selected as a child of what owns it: `.chat-list > .c-title`, never `.chat-list .c-title`, which
//   also matches the `.c-title` of every component nested below.
//
// Usage: node scripts/check-css-conventions.mjs [file.css ...]
//   With no files, every source stylesheet is checked.

import { readFileSync } from 'node:fs';
import { relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import postcss from 'postcss';
import selectorParser from 'postcss-selector-parser';
import { findSourceFiles } from './check-css-selectors.mjs';

const RepoRoot = resolve(fileURLToPath(import.meta.url), '..', '..');

const PhysicalUtility = /^-?(ml|mr|pl|pr|left|right|scroll-m[lr]|scroll-p[lr])-|^(text|float)-(left|right)$|^(rounded|border)-(l|r|tl|tr|bl|br)(-|$)|^origin-(.+-)?(left|right)$|^-?(space|divide)-x-/;
const PhysicalProperty = /^(margin|padding|scroll-margin|scroll-padding)-(left|right)$|^(left|right)$|^border-(left|right)(-|$)|^border-(top|bottom)-(left|right)-radius$/;
const PhysicalMarker = /\/\*\s*physical:/;
const HalfLeft = /^(left-1\/2|left: 50%)$/;
const HalfTranslate = /-translate-x-1\/2|translate(X)?\(-50%|translate:\s*-50%/;

/** Findings for one stylesheet's text: `{ line, message }`. */
export function findViolations(text) {
    const findings = [];
    const root = postcss.parse(text);
    root.walkRules(rule => {
        if (!isInsideKeyframes(rule))
            findDescendantParts(rule, findings);
        if (!PhysicalMarker.test(rule.toString()))
            findPhysicalSides(rule, findings);
    });

    return findings;
}

/** Findings for the given files (all source files when none are given), as formatted lines. */
export function checkFiles(paths = [], repoRoot = RepoRoot) {
    const files = paths.length === 0 ? findSourceFiles(repoRoot) : paths.map(path => resolve(path));
    const lines = [];
    for (const file of files) {
        const path = relative(repoRoot, file).replaceAll('\\', '/');
        for (const { line, message } of findViolations(readFileSync(file, 'utf8')))
            lines.push(`${path}:${line} — ${message}`);
    }

    return lines;
}

// Private methods

function findDescendantParts(rule, findings) {
    selectorParser(selectors => {
        selectors.each(selector => {
            const part = findDescendantPart(selector.nodes);
            if (part)
                findings.push({
                    line: rule.source.start.line,
                    message: `"${selector.toString().trim()}" reaches .${part} through a descendant `
                        + `combinator: select a c- part as a child (>) of what owns it.`,
                });
        });
    }).processSync(rule.selector);
}

// The first c- class that a descendant combinator reaches, looking inside :is() and :where() too
function findDescendantPart(nodes, isAfterDescendant = false) {
    let isDescendant = isAfterDescendant;
    for (const node of nodes) {
        if (node.type === 'combinator') {
            isDescendant = node.value.trim() === '';
            continue;
        }
        if (node.type === 'class' && node.value.startsWith('c-') && isDescendant)
            return node.value;

        if (node.type === 'pseudo' && /^:(is|where)$/.test(node.value))
            for (const inner of node.nodes) {
                const part = findDescendantPart(inner.nodes, isDescendant);
                if (part)
                    return part;
            }
    }

    return null;
}

function findPhysicalSides(rule, findings) {
    const isCentered = HalfTranslate.test(rule.toString());
    const report = (line, what) => {
        if (!(isCentered && HalfLeft.test(what)))
            findings.push({ line, message: physicalMessage(what) });
    };
    rule.walkAtRules('apply', atRule => {
        for (const token of atRule.params.split(/\s+/)) {
            const utility = token.split(':').pop().replace(/^!|!$/g, '');
            if (PhysicalUtility.test(utility))
                report(atRule.source.start.line, utility);
        }
    });
    rule.walkDecls(decl => {
        const isPhysical = PhysicalProperty.test(decl.prop)
            || (/^(text-align|float)$/.test(decl.prop) && /^(left|right)$/.test(decl.value))
            || (decl.prop === 'transform-origin' && /\b(left|right)\b/.test(decl.value));
        if (isPhysical && !/safe-(area-)?(left|right)\b/.test(decl.value))
            report(decl.source.start.line, `${decl.prop}: ${decl.value}`);
    });
}

function physicalMessage(what) {
    return `"${what}" names a physical side: use its start/end form, or mark the rule `
        + `/* physical: <why> */ when it must not mirror.`;
}

function isInsideKeyframes(node) {
    for (let parent = node.parent; parent; parent = parent.parent)
        if (parent.type === 'atrule' && /keyframes$/.test(parent.name))
            return true;

    return false;
}

if (process.argv[1] !== undefined && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const lines = checkFiles(process.argv.slice(2));
    if (lines.length !== 0) {
        console.error(`CSS that breaks the conventions in docs/ui/components.md:\n`);
        console.error(lines.join('\n'));
        console.error(`\n${lines.length} findings`);
        process.exit(1);
    }
}
