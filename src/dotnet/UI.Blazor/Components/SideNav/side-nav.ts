import { Disposable } from 'disposable';
import { fromEvent, Subject, takeUntil } from 'rxjs';
import { ScreenSize } from '../../Services/ScreenSize/screen-size';
import { DocumentEvents } from 'event-handling';
import { clamp, Vector2D } from 'math';
import { Log, LogLevel, LogScope } from 'logging';
import { serialize } from '../../../../nodejs/src/promises';

const LogScope: LogScope = 'SideNav';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

const magnetRange = 0.25;
const magnetPower = 2;
const deceleration = 4; // 1 = full width, per second

enum SideNavSide {
    Left,
    Right,
}

interface SideNavOptions {
    side: SideNavSide;
}

class MoveState {
    public readonly capturedAt = Date.now();
    public readonly velocity: number;
    public readonly terminalOpenRatio: number;

    constructor(
        public readonly pullRatio: number,
        public readonly openRatio: number,
        public prevMoveState: MoveState | null = null,
    ) {
        this.velocity = 0;
        this.terminalOpenRatio = openRatio;
        const s = prevMoveState?.prevMoveState;
        if (!s)
            return;

        const dt = this.capturedAt - s.capturedAt;
        this.velocity = (openRatio - s.openRatio) / dt * 1000;
        const decelerationTime = Math.abs(this.velocity / deceleration);
        const decelerationDistance = this.velocity * decelerationTime / 2; // a*t^2/2
        this.terminalOpenRatio = openRatio + decelerationDistance;
        s.prevMoveState = null;
        // debugLog?.log(`MoveState: ${openRatio} + ${decelerationDistance} (v = ${this.velocity}) = ${this.terminalOpenRatio}`);
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
        if (!(e.target instanceof Element))
            return;

        this.origin = getCoords(e);
        this.moveState = new MoveState(0, this.isOpen ? 1 : 0);
        this.element.classList.add('side-nav-dragging');
        this.element.style.transform = null;
    }

    private endMove(e: TouchEvent) {
        this.origin = null;
        this.moveState = null;
        this.element.classList.remove('side-nav-dragging');
        this.element.style.transform = null;
    }

    private continueMove(e: TouchEvent) {
        if (!this.moveState) // Already ended
            return;

        const coords = getCoords(e);
        const offset = coords.sub(this.origin);
        if (Math.abs(offset.y) > 0.5 * Math.abs(offset.x)) {
            this.endMove(e);
            return;
        }

        const dx = offset.x;
        const isLeft = this.options.side == SideNavSide.Left;
        const isOpen = this.isOpen;
        const isOpenSign = isOpen ? 1 : -1;
        const openDirectionSign = isLeft ? 1 : -1;
        const allowedDirectionSign = openDirectionSign * -isOpenSign;
        const pdx = dx * allowedDirectionSign; // Must be positive
        if (pdx < -5) {
            this.origin = new Vector2D(coords.x, this.origin.y);
            this.moveState = new MoveState(0, this.isOpen ? 1 : 0);
        }
        else {
            const pullRatio = magnet(clamp(pdx / (this.element.clientWidth + 0.01), 0, 1));
            const openRatio = isOpen ? 1 - pullRatio : pullRatio;
            this.moveState = new MoveState(pullRatio, openRatio, this.moveState);
        }
        const closeRatio = 1 - this.moveState.openRatio;
        const translateRatio = -openDirectionSign * closeRatio;
        this.element.style.transform = `translate3d(${100 * translateRatio}%, 0, 0)`;
    }

    // Event handlers

    setVisibility = serialize(async (isOpen: boolean): Promise<void> => {
        if (this.isOpen === isOpen)
            return;

        debugLog?.log(`setVisibility:`, isOpen);
        await this.blazorRef.invokeMethodAsync('OnVisibilityChanged', isOpen);
    });

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
        if (ScreenSize.isWide())
            return;

        const moveState = this.moveState;
        if (!moveState) // Already ended
            return;

        const mustBeOpen = moveState.terminalOpenRatio > 0.5;
        if (this.isOpen !== mustBeOpen)
            void this.setVisibility(mustBeOpen);
        this.endMove(e);
    }

    onTouchCancel = (e: TouchEvent): void => {
        if (ScreenSize.isWide())
            return;

        this.endMove(e);
    }
}

function getCoords(e: TouchEvent): Vector2D {
    return new Vector2D(e.touches[0].clientX, e.touches[0].clientY);
}

function magnet(x: number): number {
    if (x > 0.5)
        return 1 - magnet(1 - x); // Mirror magnet on another boundary

    if (x > magnetRange)
        return x;

    return magnetRange * Math.pow(x / magnetRange, magnetPower);
}
