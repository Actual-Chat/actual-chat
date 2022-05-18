import { Disposable } from 'disposable';
import { Subject, takeUntil, debounceTime, fromEvent } from 'rxjs';

export class TextBox implements Disposable {
    private disposed$: Subject<void> = new Subject<void>();
    private input: HTMLInputElement;

    public static create(input: HTMLInputElement): TextBox {
        return new TextBox(input);
    }

    constructor(input: HTMLInputElement) {
        this.input = input;
        fromEvent(input, 'input')
            .pipe(takeUntil(this.disposed$))
            .pipe(debounceTime(200))
            .subscribe(event => {
                input.dispatchEvent(new Event('change'));
            });
    }

    public dispose() {
        this.disposed$.next();
        this.disposed$.complete();
    }
}
