import { Log, LogLevel } from 'logging';
import { Timeout } from 'timeout';
import { Vector2D } from 'math';

const LogScope = 'LongPress';
const debugLog = Log.get(LogScope, LogLevel.Debug);

export class LongPress {
    private static startPoint = Vector2D.zero;
    private static longPressTimeout: Timeout | null;
    private static cancelNextClickEvent = false;
    private static cancelNextClickAfterMouseUpEvent = false;

    public static defaultDelayMs = 500;
    public static cancelLongPressDistance: number;

    public static init(): void {
        debugLog?.log('init')
        const navigator = window.navigator;
        if (!navigator)
            return;

        const userAgent = (navigator.userAgent ?? '').toLowerCase();
        const isAndroid = userAgent.indexOf('android') > -1;
        this.cancelLongPressDistance = isAndroid ? 5 : 10;

        const hasPointerEvents = (('PointerEvent' in window) || ('msPointerEnabled' in navigator));
        const isTouchCapable = (('ontouchstart' in window)
            || (navigator['MaxTouchPoints'] as number > 0)
            || (navigator['msMaxTouchPoints'] as number > 0));
        const pointerDown = hasPointerEvents ? 'pointerdown' : isTouchCapable ? 'touchstart' : 'mousedown';
        const pointerMove = hasPointerEvents ? 'pointermove' : isTouchCapable ? 'touchmove' : 'mousemove';
        const pointerUp = hasPointerEvents ? 'pointerup' : isTouchCapable ? 'touchend' : 'mouseup';
        const pointerCancel = hasPointerEvents ? 'pointercancel' : isTouchCapable ? 'touchcancel' : null;

        const mustCapture = { capture: true };
        document.addEventListener('click', this.onClick, mustCapture);
        document.addEventListener('contextmenu', this.onClick, mustCapture);
        document.addEventListener('wheel', this.onAnyCancelling, mustCapture);
        document.addEventListener('scroll', this.onAnyCancelling, mustCapture);
        document.addEventListener(pointerDown, this.onPointerDown, mustCapture);
        document.addEventListener(pointerMove, this.onPointerMove, mustCapture);
        document.addEventListener(pointerUp, this.onPointerUp, mustCapture);
        if (pointerCancel)
            document.addEventListener(pointerCancel, this.onAnyCancelling, mustCapture);
    }

    private static fireLongPressEvent(element: HTMLElement, event: Event) {
        this.stopLongPressTimeout();

        const touch = getTouch(event);
        const longPressEvent = new CustomEvent('long-press', {
            bubbles: true,
            cancelable: true,
            detail: {
                clientX: touch.clientX,
                clientY: touch.clientY
            },
        });
        Object.assign(longPressEvent, {
            clientX: touch.clientX,
            clientY: touch.clientY,
            // eslint-disable-next-line @typescript-eslint/no-unsafe-assignment
            offsetX: touch['offsetX'],
            // eslint-disable-next-line @typescript-eslint/no-unsafe-assignment
            offsetY: touch['offsetY'],
            pageX: touch.pageX,
            pageY: touch.pageY,
            screenX: touch.screenX,
            screenY: touch.screenY,
        })

        const mustHandleDefault = element.dispatchEvent(longPressEvent);
        const mustCancelNextClick = !mustHandleDefault || !longPressEvent.cancelBubble;

        this.cancelNextClickEvent = mustCancelNextClick;
        this.cancelNextClickAfterMouseUpEvent = mustCancelNextClick;
    }

    private static startLongPressTimeout(event: Event) {
        this.stopLongPressTimeout();

        const target = event.target as HTMLElement;
        if (!(target instanceof HTMLElement))
            return;

        const delayValue = getNearestDataValue(target, 'longPressDelay');
        const delayMs = delayValue ? parseInt(delayValue) : this.defaultDelayMs;
        this.longPressTimeout = Timeout.startPrecise(delayMs, () => this.fireLongPressEvent(target, event))
    }

    private static stopLongPressTimeout() {
        this.longPressTimeout?.clear();
        this.longPressTimeout = null;
    }

    // Event handlers

    private static onPointerDown = (e: Event) => {
        // NOTE(AY): Only primary button should trigger long presses!
        if (e['button'] !== 0)
            return;

        this.startPoint = getTouchPoint(e);
        this.startLongPressTimeout(e);
    }

    private static onPointerMove = (e: Event) => {
        const distance = getTouchPoint(e).sub(this.startPoint).length;
        if (distance > this.cancelLongPressDistance)
            this.stopLongPressTimeout();
    }

    private static onPointerUp = (e: Event) => {
        this.stopLongPressTimeout();

        if (!this.cancelNextClickAfterMouseUpEvent)
            return;

        this.cancelNextClickAfterMouseUpEvent = false;
        this.cancelNextClickEvent = true;
        // Let's stop click cancellation in 100ms no matter what
        Timeout.startRegular(100, () => {
            this.cancelNextClickEvent = false;
        });
    }

    private static onAnyCancelling = (e: Event) => {
        this.stopLongPressTimeout();
        this.cancelNextClickAfterMouseUpEvent = false;
        this.cancelNextClickEvent = false;
    }

    private static onClick = (e: Event) => {
        this.stopLongPressTimeout();

        if (this.cancelNextClickEvent) {
            debugLog?.log('onClick: cancelling, event:', e);
            this.cancelNextClickEvent = false;
            cancelEvent(e);
        }
    }
}

function getTouchPoint(event: Event): Vector2D | null {
    const x = event['clientX'] as number;
    if (typeof x !== 'number')
        return null;
    const y = event['clientY'] as number;
    if (typeof y !== 'number')
        return null;

    return new Vector2D(x, y);
}

function getTouch(event: Event): Touch {
    const touchEvent = event as TouchEvent;
    return touchEvent.changedTouches !== undefined ? touchEvent.changedTouches[0] : event as undefined as Touch;
}

function getNearestDataValue(e: HTMLElement, itemName: string): string | null {
    while (e && e !== document.documentElement) {
        const value = e.dataset[itemName];
        if (value)
            return value;
        e = e.parentNode as HTMLElement;
    }
    return null;
}

function cancelEvent(event: Event) {
    event.stopPropagation();
    event.stopImmediatePropagation();
    event.preventDefault();
}

LongPress.init();
export default LongPress;
