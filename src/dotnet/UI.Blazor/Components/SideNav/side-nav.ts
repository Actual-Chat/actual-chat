import { clamp, Vector2D } from 'math';
import { Disposable } from 'disposable';
import { fromEvent, Subject, takeUntil } from 'rxjs';
import { ScreenSize } from '../../Services/ScreenSize/screen-size';
import { delayAsync, serialize } from 'promises';
import { DeviceInfo } from 'device-info';
import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'SideNav';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

const Deceleration = 4; // 1 = full width, per second
const MinDragOffset = 5; // DeviceInfo.isAndroid ? 5 : 10; // In CSS pixels
const OpenStartDurationMs = 100;
const ClosedStartDurationMs = 50;
const MinMoveDurationMs = 100;
const MaxSetVisibilityWaitDurationMs = 1000;

enum SideNavSide {
    Left,
    Right,
}

interface SideNavOptions {
    side: SideNavSide;
}

class MoveState {
    public static readonly ended = new MoveState(0, 0, null);

    public readonly startedAt: number;
    public readonly capturedAt: number
    public readonly velocity: number;
    public readonly terminalOpenRatio: number;

    constructor(
        public readonly pullRatio: number,
        public readonly openRatio: number,
        public prevMoveState: MoveState | null = null,
    ) {
        const now = Date.now();
        this.capturedAt = now;
        this.startedAt = prevMoveState?.startedAt ?? now;
        this.velocity = 0;
        this.terminalOpenRatio = openRatio;
        const s = prevMoveState?.prevMoveState;
        if (!s)
            return;

        const dt = this.capturedAt - s.capturedAt;
        this.velocity = (openRatio - s.openRatio) / dt * 1000;
        const decelerationTime = Math.abs(this.velocity / Deceleration);
        const decelerationDistance = this.velocity * decelerationTime / 2; // a*t^2/2
        this.terminalOpenRatio = openRatio + decelerationDistance;
        s.prevMoveState = null;
        // debugLog?.log(`MoveState: ${openRatio} + ${decelerationDistance} (v = ${this.velocity}) = ${this.terminalOpenRatio}`);
    }

    public get isInitial(): boolean {
        return !!this.prevMoveState;
    }
}

export class SideNav implements Disposable {
    private readonly disposed$: Subject<void> = new Subject<void>();

    private origin: Vector2D | null = null;
    private moveState: MoveState | null;

    public static create(
        element: HTMLDivElement,
        blazorRef: DotNet.DotNetObject,
        options: SideNavOptions
    ): SideNav {
        return new SideNav(element, blazorRef, options);
    }

    constructor(
        private readonly element: HTMLDivElement,
        private readonly blazorRef: DotNet.DotNetObject,
        private readonly options: SideNavOptions,
    ) {
        if (DeviceInfo.isIos)
            return; // No way to turn off overscroll in Safari, so...

        const captureOptions = { passive: true };
        fromEvent(element, 'touchstart', captureOptions)
            .pipe(takeUntil(this.disposed$))
            .subscribe(this.onTouchStart);
        fromEvent(element, 'touchmove', captureOptions)
            .pipe(takeUntil(this.disposed$))
            .subscribe(this.onTouchMove);
        fromEvent(element, 'touchend', captureOptions)
            .pipe(takeUntil(this.disposed$))
            .subscribe(this.onTouchEnd);
        fromEvent(element, 'touchcancel', captureOptions)
            .pipe(takeUntil(this.disposed$))
            .subscribe(this.onTouchCancel);
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    public get isOpen(): boolean {
        return !this.element.classList.contains('closed');
    }

    // Private methods

    private beginMove(e: TouchEvent) {
        if (this.moveState) // Already started or ended
            return;

        // debugLog?.log('beginMove:', e);
        this.origin = getCoords(e);
        this.moveState = new MoveState(0, this.isOpen ? 1 : 0);
        this.element.classList.add('side-nav-dragging');
        this.element.style.transform = null;
    }

    private endMove(e: TouchEvent, isCancelled = false) {
        const moveState = this.moveState;
        if (moveState == null || moveState === MoveState.ended)
            return;

        this.origin = null;
        this.moveState = MoveState.ended;
        const moveDuration = Date.now() - moveState.startedAt;
        if (moveDuration < MinMoveDurationMs)
            isCancelled = true;

        debugLog?.log('endMove:', e, ', isCancelled:', isCancelled, ', moveDuration:', moveDuration);
        let mustBeOpen = this.isOpen;
        if (moveState && !isCancelled && !ScreenSize.isWide())
            mustBeOpen = moveState.terminalOpenRatio > 0.5;

        this.setTransform(mustBeOpen ? 1 : 0);
        this.element.classList.remove('side-nav-dragging');
        (async () => {
            try {
                if (this.isOpen == mustBeOpen)
                    return;

                await this.setVisibility(mustBeOpen);
                // Make sure changes are applied to DOM
                const endTime = Date.now() + MaxSetVisibilityWaitDurationMs;
                while (this.isOpen != mustBeOpen && Date.now() < endTime)
                    await delayAsync(50);
            }
            finally {
                this.moveState = null; // Re-enables beginMove again
                this.element.style.transform = null;
            }
        })();
    }

    private continueMove(e: TouchEvent) {
        const moveState = this.moveState;
        if (moveState === MoveState.ended)
            return;

        // debugLog?.log('continueMove:', e);
        if (!moveState) {
            this.beginMove(e);
            return;
        }

        const isOpen = this.isOpen;
        const coords = getCoords(e);
        const offset = coords.sub(this.origin);
        if (Math.abs(offset.y) > 0.5 * Math.abs(offset.x)) {
            // Moved too far vertically
            this.endMove(e, true);
            return;
        }
        if (moveState.isInitial) {
            if (offset.length < MinDragOffset)
                return; // Too small offset to start drag animation
            const startDurationMs = isOpen ? OpenStartDurationMs : ClosedStartDurationMs;
            if (Date.now() - moveState.startedAt < startDurationMs)
                return; // Too early to start drag animation
        }

        const isLeft = this.options.side == SideNavSide.Left;
        const isOpenSign = isOpen ? 1 : -1;
        const openDirectionSign = isLeft ? 1 : -1;
        const allowedDirectionSign = openDirectionSign * -isOpenSign;
        const dx = isOpen ? offset.x : coords.x - (isLeft ? 0 : ScreenSize.width);
        const pdx = dx * allowedDirectionSign; // Must be positive
        if (pdx < -5) {
            this.origin = new Vector2D(coords.x, this.origin.y);
            this.moveState = new MoveState(0, this.isOpen ? 1 : 0);
        }
        else {
            const pullRatio = clamp(pdx / (this.element.clientWidth + 0.01), 0, 1);
            const openRatio = isOpen ? 1 - pullRatio : pullRatio;
            this.moveState = new MoveState(pullRatio, openRatio, moveState);
        }
        this.setTransform(moveState.openRatio);
    }

    setTransform(openRatio: number): void {
        if (ScreenSize.isWide()) {
            this.element.style.transform = null;
            return;
        }

        const isLeft = this.options.side == SideNavSide.Left;
        const closeDirectionSign = isLeft ? -1 : 1;
        const closeRatio = 1 - openRatio;
        const translateRatio = closeDirectionSign * closeRatio;
        const transform = `translate3d(${100 * translateRatio}%, 0, 0)`;
        this.element.style.transform = transform
        // debugLog?.log('transform:', transform);
    }

    setVisibility = serialize(async (isOpen: boolean): Promise<void> => {
        if (this.isOpen === isOpen)
            return;

        debugLog?.log(`setVisibility:`, isOpen);
        await this.blazorRef.invokeMethodAsync('OnVisibilityChanged', isOpen);
    });

    // Event handlers

    onTouchStart = (e: TouchEvent): void => {
        if (ScreenSize.isWide())
            return;

        this.beginMove(e);
    }

    onTouchMove = (e: TouchEvent): void => {
        if (ScreenSize.isWide())
            return;

        this.continueMove(e);
    }

    onTouchEnd = (e: TouchEvent): void => {
        this.endMove(e);
    }

    onTouchCancel = (e: TouchEvent): void => {
        this.endMove(e, true);
    }
}

function getCoords(e: TouchEvent): Vector2D {
    return new Vector2D(e.touches[0].pageX, e.touches[0].pageY);
}
