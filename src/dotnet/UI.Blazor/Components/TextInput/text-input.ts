import { Disposable } from 'disposable';
import { fromEvent, Subject, takeUntil, debounceTime } from 'rxjs';

interface TextInputOptions {
    text: string;
    debounce: number;
}

export class TextInput implements Disposable {
    private readonly disposed$: Subject<void> = new Subject<void>();

    public static create(
        element: HTMLInputElement,
        blazorRef: DotNet.DotNetObject,
        options: TextInputOptions): TextInput {
        return new TextInput(element, blazorRef, options);
    }

    constructor(
        private readonly element: HTMLInputElement,
        private readonly blazorRef: DotNet.DotNetObject,
        private readonly options: TextInputOptions,
    ) {
        this.element.value = options.text;

        fromEvent(this.element, 'input')
            .pipe(
                takeUntil(this.disposed$),
                debounceTime(this.options.debounce)
            )
            .subscribe((e: InputEvent) => {
                this.blazorRef.invokeMethodAsync('OnTextChanged', (<HTMLInputElement>e.target).value);
            });
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    public clear(): void {
        this.element.value = "";
        this.blazorRef.invokeMethodAsync('OnTextChanged', "");
    }
}
