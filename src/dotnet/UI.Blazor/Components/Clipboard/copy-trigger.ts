import { fromEvent, Subject, takeUntil, switchMap, tap, delay } from 'rxjs';
import { getLogs } from 'logging';
import { getOrInheritData } from 'dom-helpers';
import { DocumentEvents } from 'event-handling';

const { errorLog } = getLogs('CopyTrigger');

const CopiedClass = 'copied';
const CopiedHintDuration = 3000;

export class CopyTrigger {
    private readonly triggerElementRef: HTMLElement;
    private copyText: string;
    private readonly copyTextFormatString: string;
    private readonly tooltip: string;
    private readonly copyTextSourceRef: HTMLInputElement | null;
    private disposed$: Subject<void> = new Subject<void>();

    public constructor(
        triggerElementRef: HTMLElement,
        copyText: string,
        tooltip: string,
        copyTextSourceRef: HTMLInputElement | null,
        copyTextFormatString : string
    ) {
        this.triggerElementRef = triggerElementRef;
        this.copyText = copyText;
        this.tooltip = tooltip;
        this.copyTextSourceRef = copyTextSourceRef;
        this.copyTextFormatString = copyTextFormatString;
        fromEvent(this.triggerElementRef, 'click').pipe(
            takeUntil(this.disposed$),
            switchMap(() => this.copy()),
            tap(() => this.showCopiedHint()),
            delay(3000),
            tap(() => this.hideCopiedHint())
        ).subscribe();
    }

    public static create(triggerElementRef: HTMLElement, copyText: string, tooltip: string, copyTextSourceInputRef: HTMLInputElement | null, copyTextFormatString : string) {
        return new CopyTrigger(triggerElementRef, copyText, tooltip, copyTextSourceInputRef, copyTextFormatString);
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    public setCopyText(text: string) {
        this.copyText = text;
    }

    private async copy() {
        let text = this.copyText;
        if (this.copyTextSourceRef != null) {
            let sourceText = this.copyTextSourceRef.value;
            if (!sourceText || sourceText.length === 0) {
                if (this.copyTextSourceRef.dataset.copySource === 'innerText') {
                    sourceText = this.copyTextSourceRef.innerText;
                }
            }
            text = this.copyTextFormatString.length > 0 ? this.copyTextFormatString.replace('{0}', sourceText) : sourceText;
        }
        return navigator.clipboard.writeText(text).catch((e: unknown) => errorLog?.log(`copy: failed to write to clipboard`, e));
    }

    private showCopiedHint() {
        this.triggerElementRef.classList.add('copied');
        this.redrawTooltip('Copied');
    }

    private hideCopiedHint() {
        this.triggerElementRef.classList.remove('copied');
        this.redrawTooltip(this.tooltip);
    }

    private redrawTooltip(text: string) {
        if (!this.tooltip)
            return;
        this.triggerElementRef.setAttribute('data-tooltip', text);
        const mouseover = new Event('mouseover', { bubbles: true });
        if (!this.triggerElementRef.dispatchEvent(mouseover))
            errorLog?.log('showAsCopied: failed to dispatch mouseover');
    }
}

// Delegated copy: any element carrying data-copy-text copies it on click, with no component,
// wrapper element or JS object of its own. Inline code spans use this rather than a CopyTrigger
// each - a markup-heavy chat renders hundreds of them, and one CopyTrigger per span costs a
// wrapper element, a component, an ElementReference and an interop round trip on every render.
// The tooltip needs nothing here: tooltip-host reads data-tooltip off the nearest ancestor too.
function onDelegatedCopyClick(event: MouseEvent): void {
    const [element, copyText] = getOrInheritData(event.target, 'copyText');
    if (!element || !copyText || element.classList.contains(CopiedClass))
        return;

    void navigator.clipboard.writeText(copyText)
        .then(() => showCopied(element))
        .catch((e: unknown) => errorLog?.log('onDelegatedCopyClick: failed to write to clipboard', e));
}

function showCopied(element: HTMLElement | SVGElement): void {
    const tooltip = element.getAttribute('data-tooltip');
    element.classList.add(CopiedClass);
    redrawTooltip(element, 'Copied');
    setTimeout(() => {
        element.classList.remove(CopiedClass);
        if (tooltip !== null)
            redrawTooltip(element, tooltip);
    }, CopiedHintDuration);
}

function redrawTooltip(element: HTMLElement | SVGElement, text: string): void {
    if (!element.hasAttribute('data-tooltip'))
        return;

    element.setAttribute('data-tooltip', text);
    element.dispatchEvent(new Event('mouseover', { bubbles: true }));
}

DocumentEvents.active.click$.subscribe(onDelegatedCopyClick);
