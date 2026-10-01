import { getLogs } from 'logging';

const { warnLog } = getLogs('RecaptchaHandler');

/**
 * Hides the "Could not connect to the reCAPTCHA service" text reCAPTCHA appends to <body>.
 * It may be inserted before this module loads, or get its text after insertion,
 * so every unmarked <body> div stays watched until it's identified.
 */
export class RecaptchaHandler {
    private static readonly hiddenAttribute = 'data-recaptcha-error';
    private static readonly watched = new WeakSet<HTMLDivElement>();

    public static init() {
        new MutationObserver(mutations => {
            for (const mutation of mutations)
                for (const node of Array.from(mutation.addedNodes))
                    this.watch(node);
        }).observe(document.body, { childList: true });
        for (const node of Array.from(document.body.children))
            this.watch(node);
    }

    // Private methods

    private static watch(node: Node) {
        if (!(node instanceof HTMLDivElement) || node.id || node.classList.length !== 0)
            return;
        if (this.watched.has(node) || this.isResolved(node))
            return;

        this.watched.add(node);
        const observer = new MutationObserver(() => {
            if (this.isResolved(node))
                observer.disconnect();
        });
        observer.observe(node, { childList: true, subtree: true, characterData: true });
    }

    private static isResolved(div: HTMLDivElement): boolean {
        if (!div.isConnected)
            return true;

        const inner = div.firstElementChild;
        if (div.childElementCount !== 1 || !(inner instanceof HTMLDivElement) || inner.classList.length !== 0)
            return false;

        const text = div.textContent;
        if (!text.toLowerCase().includes('recaptcha'))
            return false;

        div.setAttribute(this.hiddenAttribute, '');
        warnLog?.log('reCAPTCHA error detected and hidden:', text);
        return true;
    }
}

RecaptchaHandler.init();
