export type Callback = () => void;
export interface FastRafOptions {
    key?: string;
    read?: Callback;
    write?: Callback;
}

let readCallbacks: Callback [] | null = null;
let writeCallbacks: Callback [] | null = null;
const callbackKeys: Set<string> = new Set<string>();

export function fastRaf(options: FastRafOptions): boolean;
export function fastRaf(read: Callback, key?: string): boolean;
export function fastRaf(arg: Callback | FastRafOptions, key?: string): boolean {
    if (!arg)
        return false;

    let cbKey = key;
    let read: Callback | null = null;
    let write: Callback | null = null;
    if (typeof arg === 'function') {
        read = arg;
    }
    else {
        read = arg.read;
        write = arg.write;
        cbKey ??= arg.key;
    }
    if (cbKey) {
        if (callbackKeys.has(cbKey))
            return false;
        else
            callbackKeys.add(cbKey);
    }

    if(!readCallbacks || !writeCallbacks) {
        readCallbacks = new Array<Callback>();
        writeCallbacks = new Array<Callback>();
        if (read)
            readCallbacks.push(read);
        if (write)
            writeCallbacks.push(write);

        requestAnimationFrame(() => {
            const currentReadCallbacks = readCallbacks;
            const currentWriteCallbacks = writeCallbacks;
            readCallbacks = null;
            writeCallbacks = null;
            callbackKeys.clear();
            currentReadCallbacks.forEach((cb) => cb());
            currentWriteCallbacks.forEach((cb) => cb());
        });
    } else {
        if (read)
            readCallbacks.push(read);
        if (write)
            writeCallbacks.push(write);
    }
    return true;
}

let readRafPromise: Promise<void> | null = null;

export function fastReadRaf(): Promise<void> {
    // eslint-disable-next-line @typescript-eslint/no-misused-promises
    if (readRafPromise)
        return readRafPromise;

    readRafPromise = new Promise<void>((resolve) => fastRaf(() => resolve()));
    void readRafPromise.then(() => {
        readRafPromise = null;
    });

    return readRafPromise;
}


let writeRafPromise: Promise<void> | null = null;

export function fastWriteRaf(): Promise<void> {
    // eslint-disable-next-line @typescript-eslint/no-misused-promises
    if (writeRafPromise)
        return writeRafPromise;

    writeRafPromise = new Promise<void>((resolve) => fastRaf({ write: () => resolve()}));
    void writeRafPromise.then(() => {
        writeRafPromise = null;
    });

    return writeRafPromise;
}
