import { Disposable } from 'disposable';
import { PresenceTracker } from 'presence-tracker';
import { fromEvent, Subject, takeUntil, debounceTime, switchMap } from 'rxjs';

interface TextInputOptions {
    text: string;
    debounce: number;
    closeOnBlurSelector?: string;
}

/** Focus is browser state, so nothing renders this presence name - see presence-tracker.ts. An
 *  enclosing `data-children="focused-input"` turns `X:has(input:focus)` into `X[data-has-focused-input]`. */
const FocusedChild = 'focused-input';

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
                switchMap((e: InputEvent) =>
                    this.blazorRef.invokeMethodAsync('OnTextChanged', (e.target as HTMLInputElement).value))
            ).subscribe();

        fromEvent(this.element, 'paste')
            .pipe(
                takeUntil(this.disposed$),
                debounceTime(this.options.debounce),
                switchMap((e: ClipboardEvent) =>
                    this.blazorRef.invokeMethodAsync('OnPaste', e.clipboardData?.getData('Text'))),
            ).subscribe();

        this.setFocusedChild(document.activeElement === this.element);
        fromEvent(this.element, 'focus')
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => this.setFocusedChild(true));
        fromEvent(this.element, 'blur')
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => this.setFocusedChild(false));

        const closeOnBlurSelector = this.options.closeOnBlurSelector;
        if (closeOnBlurSelector) {
            const boundary = this.element.closest(closeOnBlurSelector);
            fromEvent(this.element, 'focusout')
                .pipe(takeUntil(this.disposed$))
                .subscribe((e: FocusEvent) => {
                    const related = e.relatedTarget as Node | null;
                    if (boundary && related && boundary.contains(related))
                        return;

                    void this.blazorRef.invokeMethodAsync('NotifyBlur');
                });
        }
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    public async clear() {
        this.element.value = '';
        await this.blazorRef.invokeMethodAsync('OnTextChanged', '');
    }

    public blur(): void {
        this.element.blur();
    }

    private setFocusedChild(isFocused: boolean): void {
        PresenceTracker.setChild(this.element, FocusedChild, isFocused);
    }

    // Called by Blazor
    public set(value: string | undefined): void {
        this.element.value = value ?? '';
    }
}
