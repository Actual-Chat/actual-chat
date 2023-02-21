import { fromEvent, Subject, takeUntil, switchMap, tap, delay } from 'rxjs';
import { Log, LogLevel, LogScope } from 'logging';
import { stopEvent } from 'event-handling';

const LogScope: LogScope = 'CopyToClipboard';
const errorLog = Log.get(LogScope, LogLevel.Error);

export class CopyToClipboard {
    private readonly inputRef: HTMLInputElement;
    private readonly buttonRef: HTMLButtonElement;
    private disposed$: Subject<void> = new Subject<void>();

    public constructor(inputRef: HTMLInputElement, buttonRef: HTMLButtonElement) {
        this.inputRef = inputRef;
        this.buttonRef = buttonRef;
        fromEvent(this.buttonRef, 'click').pipe(
            takeUntil(this.disposed$),
            tap(stopEvent),
            switchMap(() => this.copy()),
            tap(() => this.showCopiedHint()),
            delay(5000),
            tap(() => this.hideCopiedHint())
        ).subscribe();
    }

    public static create(inputRef: HTMLInputElement, buttonRef: HTMLButtonElement) {
        return new CopyToClipboard(inputRef, buttonRef);
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private async copy() {
        this.inputRef.select();
        const text = this.inputRef.dataset.copyText ?? this.inputRef.value;
        return navigator.clipboard.writeText(text).catch(e => errorLog?.log(`copy: failed to write to clipboard`, e));
    }

    private showCopiedHint() {
        this.buttonRef.classList.add('copied');
        this.redrawTooltip('Copied');
    }

    private hideCopiedHint() {
        this.buttonRef.classList.remove('copied');
        this.redrawTooltip('Copy');
    }

    private redrawTooltip(text: string) {
        this.buttonRef.setAttribute('data-tooltip', text);
        if (!this.buttonRef.dispatchEvent(new Event('mouseover'))) {
            errorLog?.log('showAsCopied: failed to dispatch mouseover');
        }
    }
}
