import { throttle, ResettableFunc } from 'promises';
import { Log } from 'logging';

const { debugLog } = Log.get('UndoStack');

export class UndoStack<T> {
    private items: Array<T> = new Array<T>();
    private position: number = 0;
    private isPushEnabled: boolean = true;
    public maxSize: number = 200;
    public pushThrottled: ResettableFunc<() => void>

    public constructor(
        public reader: () => T,
        public writer: (T) => void,
        public equalityComparer: (first: T, second: T) => boolean,
        public pushThrottleInterval: number,
    ) {
        this.pushThrottled = throttle(this.push, pushThrottleInterval)
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
            debugLog?.log(`push: skipping (matching redo)`);
            return;
        }
        // Checking if prev. undo is the same
        if (this.equalityComparer(value, this.items[this.position - 1])) {
            debugLog?.log(`push: skipping (matching undo)`);
            return;
        }

        this.clearRedo();
        this.items.push(value)
        while (this.items.length > this.maxSize)
            this.items.splice(0, 1);
        this.position = this.items.length;
        debugLog?.log(`push: items:`, this.items, `, position: `, this.position);
    }

    public undo() {
        this.pushThrottled.reset();
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
            debugLog?.log(`undo: items:`, this.items, `, position: `, this.position);
        }
    }

    public redo() {
        this.pushThrottled.reset();
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
            debugLog?.log(`redo: items:`, this.items, `, position: `, this.position);
        }
    }

    public clearRedo() {
        this.items.splice(this.position);
        debugLog?.log(`clearRedo:`, this.items, `, position: `, this.position);
    }

    public clear() {
        this.pushThrottled.reset();
        this.items.splice(0);
        this.items.push(this.reader())
        this.position = 1;
        debugLog?.log(`clear:`, this.items, `, position: `, this.position);
    }
}
