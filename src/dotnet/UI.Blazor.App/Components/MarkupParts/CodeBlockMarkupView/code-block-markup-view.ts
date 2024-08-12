import hljs from 'highlight.js/lib/core';
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
        const language = hljs.getLanguage(languageName);
        if (language) {
            codeElement.innerHTML = hljs.highlight(language.name, code).value;
        } else {
            codeElement.innerHTML = hljs.highlightAuto(code).value;
        }
    } catch(e) {
        errorLog?.log(`highlightCode: failed to highlight code`, e);
    }
}

function applyTheme(themeInfo: ThemeInfo){
    if (themeInfo.currentTheme === 'light') {
        // @ts-ignore
        void import('highlight.js/styles/intellij-light.css');
    } else {
        // @ts-ignore
        void import('highlight.js/styles/atom-one-dark.css');
    }
}

function init() {
    hljs.registerLanguage('bash', bash);
    hljs.registerLanguage('javascript', javascript);
    hljs.registerLanguage('typescript', typescript);
    hljs.registerLanguage('json', json);
    hljs.registerLanguage('xml', xml);
    hljs.registerLanguage('yaml', yaml);
    hljs.registerLanguage('css', css);
    hljs.registerLanguage('python', python);
    hljs.registerLanguage('go', go);
    hljs.registerLanguage('rust', rust);
    hljs.registerLanguage('java', java);
    hljs.registerLanguage('kotlin', kotlin);
    hljs.registerLanguage('c', c);
    hljs.registerLanguage('cpp', cpp);
    hljs.registerLanguage('csharp', csharp);

    applyTheme(Theme.info);
    Theme.changed.add(applyTheme);
}

init();
