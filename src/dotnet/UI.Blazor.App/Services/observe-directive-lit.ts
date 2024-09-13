import { AsyncDirective } from 'lit/async-directive.js';
import { directive } from 'lit/directive.js';
import { Observable, Subscription } from 'rxjs';

export class ObserveDirective extends AsyncDirective {
    #subscription: Subscription;

    render(observable: Observable<unknown>, defaultValue: unknown = undefined) {
        this.#subscription = observable.subscribe(value => this.setValue(value));
        return defaultValue !== undefined
            ? defaultValue.toString()
            : ``;
    }

    disconnected() {
        this.#subscription?.unsubscribe();
    }
}

export const observe = directive(ObserveDirective);
