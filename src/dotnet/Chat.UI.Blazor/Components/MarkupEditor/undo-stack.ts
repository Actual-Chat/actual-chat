import { debounce, Debounced } from '../../../../nodejs/src/debounce';

const LogScope = 'UndoStack';

export class UndoStack<T> {
    private items: Array<T> = new Array<T>();
    private position: number = 0;
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
    }

    public push() {
        const value = this.reader();
        const position = this.position - 1;
        const storedValue = this.items[position];
        if (this.equalityComparer(value, storedValue)) {
            if (this.debug)
                console.debug(`${LogScope}.push: skipping`);
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

    public undo(nested: boolean = false) {
        if (!nested)
            this.pushDebounced.cancel();

        if (this.debug)
            console.debug(`${LogScope}.undo: items:`, this.items, `, position: `, this.position);

        const value = this.reader();
        const position = this.position - 1;
        const storedValue = this.items[position];
        if (position <= 0) {
            if (this.debug)
                console.debug(`${LogScope}.undo: last item`);
            return this.writer(storedValue);
        }

        this.position = position;
        if (this.equalityComparer(value, storedValue))
            return this.undo(true);

        this.writer(storedValue);
        if (!nested)
            this.items[position] = value;

        if (this.debug)
            console.debug(`${LogScope}.undo: completed, items:`, this.items, `, position: `, this.position);
    }

    public redo(nested: boolean = false) {
        if (!nested)
            this.pushDebounced.cancel();

        if (this.debug)
            console.debug(`${LogScope}.redo: items:`, this.items, `, position: `, this.position);

        const value = this.reader();
        const position = this.position + 1;
        if (position > this.items.length) {
            if (this.debug)
                console.debug(`${LogScope}.redo: last item`);
            return;
        }

        const storedValue = this.items[position - 1];
        this.position = position;
        if (this.equalityComparer(value, storedValue))
            return this.redo(true);

        this.writer(storedValue);
        if (!nested)
            this.items[position - 1] = value;

        if (this.debug)
            console.debug(`${LogScope}.redo: completed, items:`, this.items, `, position: `, this.position);
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
