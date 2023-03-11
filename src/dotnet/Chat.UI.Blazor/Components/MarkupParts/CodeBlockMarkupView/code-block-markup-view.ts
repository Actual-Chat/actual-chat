import { default as hljs } from 'highlight.js';
import { Log } from 'logging';
import 'highlight.js/styles/intellij-light.css'

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
