import { Log } from 'logging';

const { warnLog } = Log.get('RecaptchaHandler');

export class RecaptchaHandler {
    public static observer: MutationObserver;

    public static init() {
        this.observer = new MutationObserver((mutations) => {
            for (const mutation of mutations) {
                for (const node of Array.from(mutation.addedNodes)) {
                    if (node.nodeType !== Node.ELEMENT_NODE)
                        continue;

                    const outerDiv = node as HTMLElement;

                    if (outerDiv.tagName !== 'DIV')
                        continue;

                    this.checkOuterDiv(outerDiv);
                }
            }
        });

        this.observer.observe(document.body, {
            childList: true
        });
    }

    private static checkOuterDiv(outerDiv: HTMLElement) {
        if (outerDiv.style.display == 'none')
            return;

        if (outerDiv.children.length !== 1)
            return;

        const innerDiv = outerDiv.children[0] as HTMLElement;
        if (innerDiv.tagName !== 'DIV')
            return;

        if (outerDiv.classList.length !== 0 || innerDiv.classList.length !== 0)
            return;

        const rawText = innerDiv.textContent;
        if (!rawText)
            return;

        const text = this.normalizeText(rawText);
        if (text.includes('recaptcha')) {
            outerDiv.style.display = 'none';
            warnLog?.log('reCAPTCHA error detected and hidden:', rawText);
        }
    }

    private static normalizeText(text: string): string {
        return text
            .toLowerCase()
            .replace(/\u00a0/g, ' ')
            .replace(/\s+/g, ' ')
            .trim();
    }
}

RecaptchaHandler.init();
