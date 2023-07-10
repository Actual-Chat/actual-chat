import { Disposable } from 'disposable';
import { fromEvent, Subject, takeUntil, debounceTime, switchMap } from 'rxjs';

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
                debounceTime(this.options.debounce),
                switchMap((e: InputEvent) => this.blazorRef.invokeMethodAsync('OnTextChanged', (<HTMLInputElement>e.target).value))
            ).subscribe();

        fromEvent(this.element, 'paste')
            .pipe(
                takeUntil(this.disposed$),
                debounceTime(this.options.debounce),
                switchMap((e: ClipboardEvent) => this.blazorRef.invokeMethodAsync('OnPaste', e.clipboardData.getData('Text'))),
            ).subscribe();
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    public async clear() {
        this.element.value = "";
        await this.blazorRef.invokeMethodAsync('OnTextChanged', "");
    }

    /** Called by Blazor */
    public set(value: string): void {
        this.element.value = value ?? "";
    }
}
