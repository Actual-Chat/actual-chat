// TODO: Fix ESLint errors
import hljs from 'highlight.js/lib/core';
import { LanguageFn } from 'highlight.js';
import bash from 'highlight.js/lib/languages/bash';
import javascript from 'highlight.js/lib/languages/javascript';
import typescript from 'highlight.js/lib/languages/typescript';
import json from 'highlight.js/lib/languages/json';
import xml from 'highlight.js/lib/languages/xml';
import yaml from 'highlight.js/lib/languages/yaml';
import css from 'highlight.js/lib/languages/css';
import python from 'highlight.js/lib/languages/python';
import go from 'highlight.js/lib/languages/go';
import rust from 'highlight.js/lib/languages/rust';
import java from 'highlight.js/lib/languages/java';
import kotlin from 'highlight.js/lib/languages/kotlin';
import c from 'highlight.js/lib/languages/c';
import cpp from 'highlight.js/lib/languages/cpp';
import csharp from 'highlight.js/lib/languages/csharp';
import { getLogs } from 'logging';
import { Theme, ThemeInfo } from 'theme';

const { errorLog } = getLogs('CodeBlockMarkupView');

export function highlightCode(root: HTMLElement, languageName: string, code: string) {
    const pre = root.querySelector('pre');
    if (!pre)
        return;
    const codeElement = pre.querySelector('code');
    if (!codeElement)
        return;

    let primaryLanguage = languageName;
    try {
        const language = hljs.getLanguage(languageName);
        if (language) {
            codeElement.innerHTML = hljs.highlight(code, { language: languageName }).value;
        } else if (looksLikeTable(code)) {
            codeElement.innerHTML = highlightTableCells(code);
            primaryLanguage = '';
        } else {
            const result = hljs.highlightAuto(code);
            codeElement.innerHTML = result.value;
            primaryLanguage = result.language ?? '';
        }
    } catch (e) {
        errorLog?.log(`highlightCode: failed to highlight code`, e);
    }

    updateLanguageLabel(root, codeElement, primaryLanguage);
    setupWrapToggle(root);
}

function looksLikeTable(code: string): boolean {
    const lines = code.split('\n').filter(l => l.trim().length > 0);
    if (lines.length < 2)
        return false;
    const pipeLines = lines.filter(l => l.trimStart().startsWith('|'));
    return pipeLines.length >= lines.length / 2;
}

function escapeHtml(text: string): string {
    return text
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

function highlightTableCells(code: string): string {
    const sepPattern = /^\s*\|[-:|\s]+\|\s*$/;
    return code.split('\n').map(line => {
        if (!line.includes('|') || sepPattern.test(line))
            return escapeHtml(line);
        // Split by | keeping structure: empty | cell1 | cell2 | empty
        const parts = line.split('|');
        return parts.map((cell, i) => {
            if (i === 0 || i === parts.length - 1)
                return escapeHtml(cell);
            const result = hljs.highlightAuto(cell);
            return result.relevance >= 1 ? result.value : escapeHtml(cell);
        }).join('|');
    }).join('\n');
}

function updateLanguageLabel(root: HTMLElement, codeElement: HTMLElement, primaryLanguage: string) {
    const label = root.querySelector('.code-block-language');
    if (!label)
        return;
    const langs = computeLanguageDistribution(codeElement, primaryLanguage);
    label.textContent = formatLanguageLabel(langs);
}

function computeLanguageDistribution(codeElement: HTMLElement, primaryLanguage: string): { name: string, length: number }[] {
    const counts = new Map<string, number>();
    const walk = (node: Node, lang: string) => {
        if (node.nodeType === Node.TEXT_NODE) {
            const len = node.textContent?.length ?? 0;
            if (lang && len > 0)
                counts.set(lang, (counts.get(lang) ?? 0) + len);

            return;
        }
        if (node.nodeType !== Node.ELEMENT_NODE)
            return;

        const el = node as HTMLElement;
        let nextLang = lang;
        for (const cls of Array.from(el.classList)) {
            if (cls.startsWith('language-')) {
                nextLang = cls.substring('language-'.length);
                break;
            }
        }
        for (const child of Array.from(el.childNodes))
            walk(child, nextLang);
    };
    walk(codeElement, primaryLanguage);
    return Array.from(counts.entries())
        .map(([name, length]) => ({ name, length }))
        .sort((a, b) => b.length - a.length);
}

function formatLanguageLabel(langs: { name: string, length: number }[]): string {
    if (langs.length === 0)
        return 'Plain text';
    const display = (lang: string) => hljs.getLanguage(lang)?.name ?? lang;
    if (langs.length === 1)
        return display(langs[0].name);
    if (langs.length === 2)
        return `${display(langs[0].name)}, ${display(langs[1].name)}`;
    return `${display(langs[0].name)}, ${display(langs[1].name)} + other`;
}

function setupWrapToggle(root: HTMLElement) {
    const button = root.querySelector<HTMLButtonElement>('.code-block-wrap-toggle');
    if (!button || button.dataset.wired === '1')
        return;
    button.dataset.wired = '1';
    button.addEventListener('click', () => {
        const isOn = root.classList.toggle('wrap');
        button.classList.toggle('on', isOn);
    });
}

function applyTheme(themeInfo: ThemeInfo){
    if (themeInfo.currentTheme === 'light') {
        // @ts-expect-error intentional
        void import('highlight.js/styles/intellij-light.css');
    } else {
        // @ts-expect-error intentional
        void import('highlight.js/styles/atom-one-dark.css');
    }
}

function init() {
    hljs.registerLanguage('bash', bash as unknown as LanguageFn); // TODO: remove workaround in case fixed in hljs
    hljs.registerLanguage('javascript', javascript);
    hljs.registerLanguage('typescript', typescript);
    hljs.registerLanguage('json', json);
    hljs.registerLanguage('xml', xml);
    hljs.registerLanguage('yaml', yaml);
    hljs.registerLanguage('css', css);
    hljs.registerLanguage('python', python as unknown as LanguageFn); // TODO: remove workaround in case fixed in hljs
    hljs.registerLanguage('go', go);
    hljs.registerLanguage('rust', rust);
    hljs.registerLanguage('java', java);
    hljs.registerLanguage('kotlin', kotlin);
    hljs.registerLanguage('c', c);
    hljs.registerLanguage('cpp', cpp);
    hljs.registerLanguage('csharp', csharp);

    if (Theme.info)
        applyTheme(Theme.info);
    Theme.changed.add(applyTheme);
}

init();
