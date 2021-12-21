/**
 * https://developer.mozilla.org/en-US/docs/Web/API/Window/setImmediate
 * polyfill
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