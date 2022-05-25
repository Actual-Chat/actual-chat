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
            .pipe(debounceTime(800))
            .subscribe(event => {
                console.log("text-box, emit on change. value: " + input.value);
                input.dispatchEvent(new Event('change'));
            });
    }

    public focus() {
        this.input.focus();
    }

    public dispose() {
        this.disposed$.next();
        this.disposed$.complete();
    }
}
