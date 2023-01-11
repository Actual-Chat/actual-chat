import { Observable, Subject, finalize, filter } from 'rxjs';
import keyboardDispatcher from './keyboard-dispatcher';

export class Escapist {
    public static escape$: Observable<KeyboardEvent>;

    public static init(): void {
        const subject = new Subject<KeyboardEvent>();
        keyboardDispatcher.add(subject);
        this.escape$ = subject.pipe(
            filter((event) => this.isEscapeKey(event) && !this.hasModifierKey(event)),
            finalize(() => keyboardDispatcher.remove(subject)),
        );
    }

    private static hasModifierKey(event: KeyboardEvent): boolean {
        return event.altKey || event.shiftKey || event.ctrlKey || event.metaKey;
    }

    private static isEscapeKey(event: KeyboardEvent): boolean {
        return event.keyCode === 27 || event.key === 'Escape' || event.key === 'Esc';
    }
}

Escapist.init();
export default Escapist;
