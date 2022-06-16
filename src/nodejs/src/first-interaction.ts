import { Disposable } from 'disposable';

const LogScope = 'FirstInteraction';

const _handlers = new Array<Handler>();
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
        const index = _handlers.indexOf(this);
        if (index >= 0)
            _handlers.splice(index, 1);
    }
}

export function isInteractionHappened() : boolean {
    return _isInteractionHappened;
}

export function addInteractionHandler(name: string, handler: () => Promise<boolean>): Handler {
    const h = new Handler(name, handler);
    _handlers.push(h);
    if (_isInteractionHappened)
        runHandlers();
    return h;
}

function runHandlers() {
    for (const handler of _handlers)
        void handler.run();
}

function onEvent(eventName: string, event: object) {
    console.log(`${LogScope}.onEvent: triggered with event '${eventName}', data =`, event);
    runHandlers();
}

function onInteractionHappened() {
    if (_isInteractionHappened)
        return;
    _isInteractionHappened = true;
    self.removeEventListener('click', onClick);
    self.removeEventListener('doubleclick', onDoubleClick);
    self.removeEventListener('onkeydown', onKeyDown);
    self.removeEventListener('touchend',  onTouchEnd);
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
