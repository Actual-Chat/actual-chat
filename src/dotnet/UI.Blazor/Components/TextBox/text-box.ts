import { Disposable } from 'disposable';
import { Subject, takeUntil, debounceTime, fromEvent } from 'rxjs';
import { Log } from 'logging';
import { DeviceInfo } from 'device-info';

const { debugLog } = Log.get('TextBox');

export class TextBox implements Disposable {
    private disposed$: Subject<void> = new Subject<void>();
    private input: HTMLInputElement;

    public static create(input: HTMLInputElement): TextBox {
        return new TextBox(input);
    }

    constructor(input: HTMLInputElement) {
        this.input = input;
        fromEvent(input, 'input')
            .pipe(
                takeUntil(this.disposed$),
                debounceTime(800),
            )
            .subscribe(() => {
                debugLog?.log(`input handler, value:`, input.value);
                input.dispatchEvent(new Event('change'));
            });

        // Mobile browsers don't always scroll to show input above keyboard
        // Keep input visible on any viewport change (keyboard, screen recording, orientation)
        if (DeviceInfo.isMobile && window.visualViewport) {
            let settled: ReturnType<typeof setTimeout>;
            const keepVisible = () => {
                if (document.activeElement === input) {
                    input.scrollIntoView({ behavior: 'smooth', block: 'center' });
                }
            };
            fromEvent(window.visualViewport, 'resize')
                .pipe(takeUntil(this.disposed$))
                .subscribe(() => {
                    clearTimeout(settled);
                    settled = setTimeout(keepVisible, 150);
                });
        }
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    public focus() {
        this.input.focus();
    }
}
