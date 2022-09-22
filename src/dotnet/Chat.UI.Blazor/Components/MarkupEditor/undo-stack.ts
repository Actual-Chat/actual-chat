import { debounce, Debounced } from '../../../../nodejs/src/debounce';

const LogScope = 'UndoStack';

export class UndoStack<T> {
    private items: Array<T> = new Array<T>();
    private position: number = 0;
    private isPushEnabled: boolean = true;
    public maxSize: number = 200;
    public pushDebounced: Debounced<() => void>

    public constructor(
        public reader: () => T,
        public writer: (T) => void,
        public equalityComparer: (first: T, second: T) => boolean,
        public pushDebounceDelay: number = 500,
        private debug: boolean = false,
    ) {
        this.pushDebounced = debounce(this.push, pushDebounceDelay)
        this.clear();

        // Replacing writer so it temporary disables push.
        const oldWriter = this.writer;
        this.writer = (value) => {
            const oldIsPushEnabled = this.isPushEnabled;
            this.isPushEnabled = false;
            try {
                oldWriter(value);
            }
            finally {
                this.isPushEnabled = oldIsPushEnabled;
            }
        }
    }

    public push() {
        if (!this.isPushEnabled)
            return;

        const value = this.reader();

        // Checking if next redo is the same
        if (this.position < this.items.length && this.equalityComparer(value, this.items[this.position])) {
            if (this.debug)
                console.debug(`${LogScope}.push: skipping (matching redo)`);
            return;
        }
        // Checking if prev. undo is the same
        if (this.equalityComparer(value, this.items[this.position - 1])) {
            if (this.debug)
                console.debug(`${LogScope}.push: skipping (matching undo)`);
            return;
        }

        this.clearRedo();
        this.items.push(value)
        while (this.items.length > this.maxSize)
            this.items.splice(0, 1);
        this.position = this.items.length;

        if (this.debug)
            console.debug(`${LogScope}.push: items:`, this.items, `, position: `, this.position);
    }

    public undo() {
        this.pushDebounced.cancel();
        this.push();

        try {
            const value = this.reader();
            while (true) {
                const position = this.position - 1;
                const storedValue = this.items[position];
                if (position == 0)
                    return this.writer(storedValue);
                this.position = position;
                if (!this.equalityComparer(value, storedValue))
                    return this.writer(storedValue);
            }
        }
        finally {
            if (this.debug)
                console.debug(`${LogScope}.undo: items:`, this.items, `, position: `, this.position);
        }
    }

    public redo() {
        this.pushDebounced.cancel();
        this.push();

        try {
            if (this.position >= this.items.length)
                return;

            const value = this.reader();
            while (true) {
                this.position++;
                const storedValue = this.items[this.position - 1];
                if (this.position >= this.items.length)
                    return this.writer(storedValue);
                if (!this.equalityComparer(value, storedValue))
                    return this.writer(storedValue);
            }
        }
        finally {
            if (this.debug)
                console.debug(`${LogScope}.redo: items:`, this.items, `, position: `, this.position);
        }
    }

    public clearRedo() {
        this.items.splice(this.position);

        if (this.debug)
            console.debug(`${LogScope}.clearRedo:`, this.items, `, position: `, this.position);
    }

    public clear() {
        this.pushDebounced.cancel();
        this.items.splice(0);
        this.items.push(this.reader())
        this.position = 1;

        if (this.debug)
            console.debug(`${LogScope}.clear:`, this.items, `, position: `, this.position);
    }
}
