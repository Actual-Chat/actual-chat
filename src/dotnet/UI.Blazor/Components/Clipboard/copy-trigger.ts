import { fromEvent, Subject, takeUntil, switchMap, tap, delay } from 'rxjs';
import { Log } from 'logging';

const { errorLog } = Log.get('CopyTrigger');

export class CopyTrigger {
    private readonly triggerElementRef: HTMLElement;
    private readonly copyText: string;
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

    private async copy() {
        let text = this.copyText;
        if (this.copyTextSourceRef != null) {
            const sourceText = this.copyTextSourceRef.value;
            if (this.copyTextFormatString.length > 0) {
                text = this.copyTextFormatString.replace('{0}', sourceText);
            }
        }
        return navigator.clipboard.writeText(text).catch(e => errorLog?.log(`copy: failed to write to clipboard`, e));
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
