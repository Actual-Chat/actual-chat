import { Subject } from 'rxjs';

class KeyboardDispatcher {
    private readonly subjects: Subject<KeyboardEvent>[] = [];
    private _isAttached: boolean;

    public add(subject: Subject<KeyboardEvent>): void {
        this.remove(subject);
        this.subjects.push(subject);

        if (!this._isAttached) {
            document.body.addEventListener('keydown', this.onKeyDown);
            this._isAttached = true;
        }
    }

    public remove(listener: Subject<KeyboardEvent>): void {
        const index = this.subjects.indexOf(listener);
        if (index > -1) {
            this.subjects.splice(index, 1);
        }

        if (this.subjects.length === 0) {
            this.detach();
        }
    }

    private onKeyDown = (event: KeyboardEvent) => {
        for (let i = this.subjects.length - 1; i > -1; i--) {
            let subject = this.subjects[i];
            if (subject.observed) {
                subject.next(event);
                break;
            }
        }
    };

    private detach() {
        if (this._isAttached) {
            document.body.removeEventListener('keydown', this.onKeyDown);
            this._isAttached = false;
        }
    }
}

const keyboardDispatcher = new KeyboardDispatcher();

export default keyboardDispatcher;

