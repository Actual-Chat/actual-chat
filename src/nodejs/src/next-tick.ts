/**
 * Something like polyfill of [setImmediate](https://developer.mozilla.org/en-US/docs/Web/API/Window/setImmediate)
 * Do not use webpack's polyfill of setImmediate, because it's implemented using
 * setTimeout which will be throttled in background tabs.
 */
const channel = new MessageChannel();
const callbacks = [];

channel.port1.onmessage = (_event) => {
    const callback = callbacks.shift();
    callback();
};

/**
 * This method is used to break up long running operations
 * and run a callback function immediately after the browser has completed other operations
 * such as events and display updates.
 */
export function nextTick(callback: () => void) {
    callbacks.push(callback);
    channel.port2.postMessage(null);
}

/** This method is used to break up long running operations */
export function nextTickAsync(): Promise<void> {
    return new Promise<void>(resolve => nextTick(resolve));
}