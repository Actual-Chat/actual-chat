import { Disposable } from 'disposable';
import { onDeviceAwake } from 'on-device-awake';

const LogScope = 'FirstInteraction';

const _interactionHandlers = new Array<Handler>();
let _isInteractionHappened = false;

class Handler implements Disposable {
    public readonly name: string;
    public readonly handler: () => Promise<boolean>;
    public isCompleted = false;

    private resultTask?: Promise<boolean> = null;
    private isDisposed = false;

    constructor(name: string, handler: () => Promise<boolean>) {
        this.name = name;
        this.handler = handler;
    }

    public async run(): Promise<void> {
        let result = false;
        try {
            if (this.resultTask == null)
                this.resultTask = this.handler();
            result = await this.resultTask;
            this.isCompleted = true;
            if (!result) {
                // we don't need the handler to be triggered after wake up anymore
                this.dispose();
            }
        } catch (e) {
            console.error(`${LogScope}.Handler.run: handler '${this.name}' failed with an error:`, e);
            this.resultTask = null;
        }
        if (result === true && !_isInteractionHappened) {
            console.log(`${LogScope}.Handler.run: handler '${this.name}' reported that user interaction happened.`);
        }
    }

    public dispose(): void {
        if (this.isDisposed)
            return;
        this.isDisposed = true;
        const index = _interactionHandlers.indexOf(this);
        if (index >= 0)
            _interactionHandlers.splice(index, 1);
    }
}

function runInteractionHandlers() {
    for (const handler of _interactionHandlers)
        void handler.run();
}

function onEvent(eventName: string, event: object) {
    console.debug(`${LogScope}.onEvent: triggered with event '${eventName}', data =`, event);
    runInteractionHandlers();
    onInteractionHappened();
}

function onInteractionHappened() {
    if (_isInteractionHappened)
        return;

    _isInteractionHappened = true;
    self.removeEventListener('click', onClick);
    self.removeEventListener('doubleclick', onDoubleClick);
    self.removeEventListener('onkeydown', onKeyDown);
    self.removeEventListener('touchend', onTouchEnd);
}

const onClick = (event: object) => onEvent('click', event);
const onDoubleClick = (event: object) => onEvent('doubleclick', event);
const onKeyDown = (event: object) => onEvent('keydown', event);
const onTouchEnd = (event: object) => onEvent('touchend', event);

function setup() {
    const options = { passive: true };
    self.addEventListener('click', onClick, options);
    self.addEventListener('doubleclick', onDoubleClick, options);
    self.addEventListener('onkeydown', onKeyDown, options);
    self.addEventListener('touchend', onTouchEnd, options);
}

function reset(): void {
    _isInteractionHappened = false;
    setup();
}

setup();
onDeviceAwake(() => reset());

export function isInteractionHappened() : boolean {
    return _isInteractionHappened;
}

/**
 * handler returns Promise<boolean> where true indicates that it should be invoked again
 * on the first user interaction after device wake up
 */
export function addInteractionHandler(name: string, handler: () => Promise<boolean>): Disposable {
    const h = new Handler(name, handler);
    _interactionHandlers.push(h);
    if (_isInteractionHappened)
        reset();
    return h;
}
