import { Disposable } from 'disposable';
import { fromEvent, Subject, takeUntil, concatMap, tap, merge } from 'rxjs';
import { preventDefaultForEvent, stopEvent } from 'event-handling';
import { hasModifierKey } from 'keyboard';

export class TotpInput implements Disposable {
    private readonly disposed$: Subject<void> = new Subject<void>();

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
        merge(fromEvent(inputs, 'input'), fromEvent(inputs, 'paste'), fromEvent(inputs, 'change'))
            .pipe(
                takeUntil(this.disposed$),
                concatMap(() => this.onChanged())
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

        this.focus();
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
    public focus() {
        const text = this.getText();
        const i = text.length >= this.length ? this.length - 1 : text.length;
        this.inputs[i].focus();
    }

    /** Called by blazor */
    public clear() {
        for (let i = 0; i < this.length; i++) {
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

    private async onKeyUp(e: KeyboardEvent) {
        if (hasModifierKey(e)){
            return;
        }

        switch (e.key) {
            case 'ArrowLeft':
            case 'Backspace':
            case 'Delete':
                const text = this.getText();
                await this.setFromText(text.substring(0, text.length - 1))
                break;
            default:
                return;
        }

        e.preventDefault();
        e.stopPropagation();
    }

    private async onChanged() {
        const text = this.getText();
        return this.setFromText(text);
    }

    private async setFromText(text: string) {
        this.clear();
        if(!text.length) {
            return;
        }
        for (let i = 0; i < text.length; i++) {
            this.setDigit(i, text[i]);
        }

        this.hideError();
        this.focus();
        if (text.length >= this.length)
            await this.blazorRef.invokeMethodAsync('OnCompleted', +text);
    }

    private setDigit(i: number, value: string | null) {
        if (i < 0 || i >= this.length)
            return;

        this.inputs[i].value = value?.toString() ?? "";
    }

    private onClick(e: MouseEvent) {
        this.focus();
    }

    private getText() {
        return this.inputs.map(x => x.value).join().replace(/\D/g, '').substring(0, this.length);
    }
}
