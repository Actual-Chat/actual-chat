import { Disposable } from 'disposable';

const LogScope = 'FirstInteraction';

const _interactionHandlers = new Array<Handler>();
const _postInteractionHandlers = new Array<() => void>();
let _isInteractionHappened = false;

export class Handler implements Disposable {
    public readonly name: string
    public readonly handler: () => Promise<boolean>;
    public isSucceeded = false;

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
            this.isSucceeded = true;
            this.dispose();
        }
        catch (e) {
            console.error(`${LogScope}.Handler.run: handler '${this.name}' failed with an error:`, e);
            this.resultTask = null;
        }
        if (result === true && !_isInteractionHappened) {
            console.log(`${LogScope}.Handler.run: handler '${this.name}' reported that user interaction happened.`);
            onInteractionHappened();
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

export function isInteractionHappened() : boolean {
    return _isInteractionHappened;
}

export function addInteractionHandler(name: string, handler: () => Promise<boolean>): Handler {
    const h = new Handler(name, handler);
    _interactionHandlers.push(h);
    if (_isInteractionHappened)
        runInteractionHandlers();
    return h;
}

export function addPostInteractionHandler(handler: () => void): void {
    _postInteractionHandlers.push(handler);
    if (_isInteractionHappened)
        runPostInteractionHandlers();
}

export function removePostInteractionHandler(handler: () => void): boolean {
    const index = _postInteractionHandlers.indexOf(handler);
    if (index < 0)
        return false;
    _postInteractionHandlers.splice(index, 1);
    return true;
}

function runInteractionHandlers() {
    for (const handler of _interactionHandlers)
        void handler.run();
}

function runPostInteractionHandlers() {
    while (_postInteractionHandlers.length > 0) {
        const handler = _postInteractionHandlers.pop();
        handler();
    }
}

function onEvent(eventName: string, event: object) {
    console.debug(`${LogScope}.onEvent: triggered with event '${eventName}', data =`, event);
    runInteractionHandlers();
}

function onInteractionHappened() {
    if (_isInteractionHappened)
        return;
    _isInteractionHappened = true;
    self.removeEventListener('click', onClick);
    self.removeEventListener('doubleclick', onDoubleClick);
    self.removeEventListener('onkeydown', onKeyDown);
    self.removeEventListener('touchend',  onTouchEnd);
    runPostInteractionHandlers();
}

const onClick = (event: object) => onEvent('click', event)
const onDoubleClick = (event: object) => onEvent('doubleclick', event)
const onKeyDown = (event: object) => onEvent('keydown', event)
const onTouchEnd = (event: object) => onEvent('touchend', event)

function setup() {
    const options = { passive: true };
    self.addEventListener('click', onClick, options);
    self.addEventListener('doubleclick', onDoubleClick, options);
    self.addEventListener('onkeydown', onKeyDown, options);
    self.addEventListener('touchend',  onTouchEnd, options);
}

setup();
