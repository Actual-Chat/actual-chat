import { Observable, Subscriber } from 'rxjs';
import { hasModifierKey } from 'keyboard';

export class Escapist {
    public static event$: Observable<KeyboardEvent>;
    public static capturedEvent$: Observable<KeyboardEvent>;

    public static init(): void {
        this.event$ = this.createObservable(false);
        this.capturedEvent$ = this.createObservable(true);
    }

    private static createObservable(capture: boolean): Observable<KeyboardEvent> {
        return Observable.create((subscriber: Subscriber<KeyboardEvent>) => {
            const onKeyDown = (event: KeyboardEvent) => {
                if (isEscapeKey(event) && !hasModifierKey(event))
                    subscriber.next(event);
            }
            document.body.addEventListener('keydown', onKeyDown, { capture: capture });
            return () => {
                document.body.removeEventListener('keydown', onKeyDown, { capture: capture });
            }
        })
    }
}

// Helpers

function isEscapeKey(event: KeyboardEvent): boolean {
    return event.keyCode === 27 || event.key === 'Escape' || event.key === 'Esc';
}

Escapist.init();
export default Escapist;
