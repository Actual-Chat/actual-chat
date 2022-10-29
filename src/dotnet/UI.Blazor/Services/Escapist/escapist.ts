import { Observable, Subject, finalize, filter } from 'rxjs';
import keyboardDispatcher from './keyboard-dispatcher';

export class Escapist {
    public escapeEvents(): Observable<KeyboardEvent> {
        const subject = new Subject<KeyboardEvent>();
        keyboardDispatcher.add(subject);
        return subject.pipe(
            filter((event) => this.isEscapeKey(event) && !this.hasModifierKey(event)),
            finalize(() => keyboardDispatcher.remove(subject)),
        );
    }

    private hasModifierKey(event: KeyboardEvent): boolean {
        return event.altKey || event.shiftKey || event.ctrlKey || event.metaKey;
    }

    private isEscapeKey(event: KeyboardEvent): boolean {
        return event.keyCode === 27 || event.key === 'Escape' || event.key === 'Esc';
    }
}

const escapist = new Escapist();

export default escapist;
