import { Observable, Subject, filter, finalize } from 'rxjs';
import { hasModifierKey, isEscapeKey } from 'keyboard';
import keyboardDispatcher from './keyboard-dispatcher';

class Escapist {
    public escapeEvents(): Observable<KeyboardEvent> {
        const subject = new Subject<KeyboardEvent>();
        keyboardDispatcher.add(subject);
        return subject.pipe(
            filter((event) => isEscapeKey(event) && !hasModifierKey(event)),
            finalize(() => keyboardDispatcher.remove(subject)),
        );
    }
}

const escapist = new Escapist();

export default escapist;
