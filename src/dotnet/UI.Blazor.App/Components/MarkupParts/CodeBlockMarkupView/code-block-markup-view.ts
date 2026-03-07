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
import { Log } from 'logging';
import { Theme, ThemeInfo } from 'theme';

const { errorLog } = Log.get('CodeBlockMarkupView');

export function highlightCode(pre: HTMLPreElement, languageName: string, code: string) {
    try {
        const codeElement = pre.querySelector('code');
        if (!codeElement)
            return;
        const language = hljs.getLanguage(languageName);
        if (language) {
            codeElement.innerHTML = hljs.highlight(code, { language: languageName }).value;
        } else if (looksLikeTable(code)) {
            codeElement.innerHTML = highlightTableCells(code);
        } else {
            codeElement.innerHTML = hljs.highlightAuto(code).value;
        }
    } catch(e) {
        errorLog?.log(`highlightCode: failed to highlight code`, e);
    }
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
