import { Disposable } from 'disposable';
import { Subject, takeUntil, fromEvent } from 'rxjs';
import { DeviceInfo } from 'device-info';

type Keyboard = 'email' | 'phone';

/** Picks the mobile keyboard for a phone-or-email input: email until the value looks like a phone number, then the phone keypad; a manual toggle sticks until the field is cleared. */
export class PhoneOrEmailKeyboard implements Disposable {
    private disposed$: Subject<void> = new Subject<void>();
    private input: HTMLInputElement;
    private blazorRef: DotNet.DotNetObject | null;
    private keyboard: Keyboard | null = null;
    private manualKeyboard: Keyboard | null = null;

    public static create(input: HTMLInputElement, blazorRef: DotNet.DotNetObject): PhoneOrEmailKeyboard {
        return new PhoneOrEmailKeyboard(input, blazorRef);
    }

    constructor(input: HTMLInputElement, blazorRef: DotNet.DotNetObject) {
        this.input = input;
        this.blazorRef = blazorRef;
        if (!DeviceInfo.isMobile)
            return;

        fromEvent(input, 'input')
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => {
                const value = input.value.trim();
                if (value === '')
                    this.manualKeyboard = null;

                // A digit-only prefix flips to the keypad only after 3 digits: the keypad has no letter keys, and an email may start with a few digits.
                const isPhoneLike = /^\+?[\d\s().-]*$/.test(value)
                    && (value.startsWith('+') || value.replace(/\D/g, '').length >= 3);
                this.setKeyboard(this.manualKeyboard ?? (isPhoneLike ? 'phone' : 'email'));
            });
        this.setKeyboard('email');
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
        this.blazorRef = null;
    }

    public toggle(): void {
        this.manualKeyboard = this.keyboard === 'phone' ? 'email' : 'phone';
        this.setKeyboard(this.manualKeyboard);
    }

    // Private methods

    private setKeyboard(keyboard: Keyboard): void {
        if (this.keyboard === keyboard)
            return;

        this.keyboard = keyboard;
        this.input.inputMode = keyboard === 'phone' ? 'tel' : 'email';
        this.input.autocomplete = keyboard === 'phone' ? 'tel' : 'email';
        void this.blazorRef?.invokeMethodAsync('OnPhoneKeyboardChanged', keyboard === 'phone');
    }
}
