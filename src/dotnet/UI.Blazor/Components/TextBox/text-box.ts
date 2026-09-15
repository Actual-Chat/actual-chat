import { Disposable } from 'disposable';
import { Subject, takeUntil, debounceTime, fromEvent } from 'rxjs';
import { getLogs } from 'logging';
import { DeviceInfo } from 'device-info';
import { setupMobileKeyboardHandler } from 'dom-helpers';

const { debugLog } = getLogs('TextBox');

type Keyboard = 'email' | 'phone';

export class TextBox implements Disposable {
    private disposed$: Subject<void> = new Subject<void>();
    private input: HTMLInputElement;
    private blazorRef: DotNet.DotNetObject | null;
    private keyboard: Keyboard | null = null;
    private manualKeyboard: Keyboard | null = null;

    public static create(input: HTMLInputElement, blazorRef?: DotNet.DotNetObject, isPhoneOrEmail = false): TextBox {
        return new TextBox(input, blazorRef ?? null, isPhoneOrEmail);
    }

    constructor(input: HTMLInputElement, blazorRef: DotNet.DotNetObject | null, isPhoneOrEmail: boolean) {
        this.input = input;
        this.blazorRef = blazorRef;
        fromEvent(input, 'input')
            .pipe(
                takeUntil(this.disposed$),
                debounceTime(800),
            )
            .subscribe(() => {
                debugLog?.log(`input handler, value:`, input.value);
                if (this.blazorRef) {
                    void this.blazorRef.invokeMethodAsync('OnDebouncedChange', input.value);
                } else {
                    input.dispatchEvent(new Event('change', { bubbles: true }));
                }
            });

        setupMobileKeyboardHandler(input, this.disposed$);
        if (isPhoneOrEmail && DeviceInfo.isMobile)
            this.setupPhoneOrEmailKeyboard();
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
        this.blazorRef = null;
    }

    public focus() {
        this.input.focus({ preventScroll: true });
    }

    public togglePhoneKeyboard(): void {
        this.manualKeyboard = this.keyboard === 'phone' ? 'email' : 'phone';
        this.setKeyboard(this.manualKeyboard);
    }

    // Private methods

    /** The keyboard follows the value: email until it looks like a phone number, then the phone keypad; a manual toggle sticks until the field is cleared. */
    private setupPhoneOrEmailKeyboard(): void {
        fromEvent(this.input, 'input')
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => {
                const value = this.input.value.trim();
                if (value === '')
                    this.manualKeyboard = null;

                // A digit-only prefix flips to the keypad only after 3 digits: the keypad has no letter keys, and an email may start with a few digits.
                const isPhoneLike = /^\+?[\d\s().-]*$/.test(value)
                    && (value.startsWith('+') || value.replace(/\D/g, '').length >= 3);
                this.setKeyboard(this.manualKeyboard ?? (isPhoneLike ? 'phone' : 'email'));
            });
        this.setKeyboard('email');
    }

    private setKeyboard(keyboard: Keyboard): void {
        if (this.keyboard === keyboard)
            return;

        this.keyboard = keyboard;
        this.input.inputMode = keyboard === 'phone' ? 'tel' : 'email';
        this.input.autocomplete = keyboard === 'phone' ? 'tel' : 'email';
        void this.blazorRef?.invokeMethodAsync('OnPhoneKeyboardChanged', keyboard === 'phone');
    }
}
