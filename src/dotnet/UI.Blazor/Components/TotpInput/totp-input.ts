import { Disposable } from 'disposable';
import { fromEvent, Subject, takeUntil, switchMap, tap } from 'rxjs';
import { preventDefaultForEvent, stopEvent } from 'event-handling';
import { hasModifierKey } from 'keyboard';

export class TotpInput implements Disposable {
    private readonly disposed$: Subject<void> = new Subject<void>();
    private readonly digits: (number | null)[];

    public static create(
        element: HTMLDivElement,
        inputs: HTMLInputElement[],
        blazorRef: DotNet.DotNetObject): TotpInput {
        return new TotpInput(element, inputs, blazorRef);
    }

    constructor(
        private readonly element: HTMLDivElement,
        private readonly inputs: HTMLInputElement[],
        private readonly blazorRef: DotNet.DotNetObject,
    ) {
        this.digits = new Array(this.length).fill(null);
        fromEvent(inputs, 'input')
            .pipe(
                takeUntil(this.disposed$),
                tap(stopEvent),
                tap(preventDefaultForEvent),
                switchMap((e: InputEvent) => this.onInput(e))
            ).subscribe();

        fromEvent(inputs, 'paste')
            .pipe(
                takeUntil(this.disposed$),
                tap(stopEvent),
                tap(preventDefaultForEvent),
                switchMap((e: ClipboardEvent) => this.onPaste(e)),
            ).subscribe();

        fromEvent(inputs, 'keyup')
            .pipe(
                takeUntil(this.disposed$),
            ).subscribe((e: KeyboardEvent) => this.onKeyUp(e));

        fromEvent(inputs, 'click')
            .pipe(
                tap(stopEvent),
                tap(preventDefaultForEvent),
                takeUntil(this.disposed$),
            ).subscribe((e: MouseEvent) => this.onClick(e));

        this.inputs[this.length - 1].focus();
    }

    private get length() {
        return this.inputs.length;
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    /** Called by blazor */
    public focus(i?: number) {
        i ??= this.getLeftEmptyDigitIdx() ?? 0;
        this.inputs[i]?.focus();
    }

    /** Called by blazor */
    public clear() {
        for (let i = this.length - 1; i >= 0; i--) {
            this.setDigit(i, null);
        }
        this.focus();
    }

    /** Called by blazor */
    public showError() {
        this.element.classList.add('invalid');
    }

    private hideError() {
        this.element.classList.remove('invalid');
    }

    private onKeyUp(e: KeyboardEvent) {
        if (hasModifierKey(e)){
            return;
        }

        const [i] = this.getEventCtx(e);
        switch (e.key) {
            case 'ArrowLeft':
                this.focus(i + 1);
                break;
            case 'ArrowRight':
                this.focus(i - 1);
                break;
            case 'Delete':
                this.setDigit(i, null);
                break;
            case 'Backspace':
                // if current is empty remove and focus left-hand digit input
                if (this.digits[i] !== null) {
                    this.setDigit(i, null);
                } else {
                    this.setDigit(i + 1, null);
                    this.focus(i + 1);
                }
                break;
            default:
                return;
        }

        e.preventDefault();
        e.stopPropagation();
    }

    private async onInput(e: InputEvent) {
        const [i, input] = this.getEventCtx(e);
        const text = e.data ?? input.value;
        await this.setFromText(text, i, input);
    }

    private async onPaste(e: ClipboardEvent) {
        const [i, input] = this.getEventCtx(e);
        let text = e.clipboardData.getData('Text');
        await this.setFromText(text, i, input);
    }

    private  async setFromText(text: string, i: number, input: HTMLInputElement) {
        text = text.replace(/\D/g, '').substring(0, this.length);
        if (!text.length)
        {
            // keep current digit or clear
            input.value = this.digits[i]?.toString() ?? '';
            return;
        }

        this.hideError();

        const startIdx = text.length === this.length ? this.length - 1 : i;
        text = text.substring(0, startIdx + 1);
        for (let j = 0; j < text.length; j++) {
            this.setDigit(startIdx - j, +text[j]);
        }
        const focusedDigitIndex = Math.max(0, startIdx - text.length);
        this.focus(focusedDigitIndex);
        if (focusedDigitIndex === 0)
            await this.reportIfCompleted();
    }

    private setDigit(i: number, value: number | null) {
        if (i < 0 || i >= this.length)
            return;

        this.digits[i] = value;
        this.inputs[i].value = value?.toString() ?? "";
    }

    private async reportIfCompleted() {
        if (this.digits.some(x => x === null))
            return;

        let code = 0;
        for (let i = 0; i < this.length; i++) {
            code += this.digits[i] * Math.pow(10, i);
        }
        return this.blazorRef.invokeMethodAsync('OnCompleted', code);
    }

    private onClick(e: MouseEvent) {
        // select most left empty or clicked digit input
        let i = this.getLeftEmptyDigitIdx() ?? this.getEventCtx(e)[0];
        this.focus(i);
    }

    private getEventCtx(e: Event): [number, HTMLInputElement]{
        const input = e.target as HTMLInputElement;
        const i = +input.dataset.index;
        return [i, input];
    }

    private getLeftEmptyDigitIdx(): number | null {
        for (let i = this.length - 1; i >= 0; i--) {
            if (this.digits[i] === null) {
                return i;
            }
        }

        return null;
    }
}
