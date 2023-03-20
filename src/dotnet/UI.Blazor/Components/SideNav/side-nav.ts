import { clamp, Vector2D } from 'math';
import { delayAsync, serialize } from 'promises';
import { DeviceInfo } from 'device-info';
import { Disposable, DisposableBag, fromSubscription } from 'disposable';
import { DocumentEvents, preventDefaultForEvent } from 'event-handling';
import { Gesture, Gestures } from 'gestures';
import { ScreenSize } from '../../Services/ScreenSize/screen-size';
import { Log } from 'logging';

const { debugLog } = Log.get('SideNav');

const Deceleration = 4; // 1 = full width, per second
const PullBoundary = 0.2; // 20% of the screen width
const PrePullDistance = 5; // DeviceInfo.isAndroid ? 5 : 10; // In CSS pixels
const PrePullDurationMs = 100;
const MinPullDurationMs = 150;
const MaxSetVisibilityWaitDurationMs = 500;

enum SideNavSide {
    Left,
    Right,
}

interface SideNavOptions {
    side: SideNavSide;
}

export class SideNav extends DisposableBag {
    public static left: SideNav | null = null;
    public static right: SideNav | null = null;

    public get side(): SideNavSide { return this.options.side; }
    public get opposite(): SideNav { return this.side == SideNavSide.Left ? SideNav.right : SideNav.left; }

    public static create(
        element: HTMLDivElement,
        blazorRef: DotNet.DotNetObject,
        options: SideNavOptions
    ): SideNav {
        return new SideNav(element, blazorRef, options);
    }

    constructor(
        public readonly element: HTMLDivElement,
        private readonly blazorRef: DotNet.DotNetObject,
        public readonly options: SideNavOptions,
    ) {
        super();
        const pullGestureDisposer = DeviceInfo.isIos ? null : SideNavPullDetectGesture.use(this);
        if (this.side == SideNavSide.Left) {
            SideNav.left = this;
            this.addDisposables(pullGestureDisposer, { dispose() { SideNav.left = null }});
        }
        else {
            SideNav.right = this;
            this.addDisposables(pullGestureDisposer, { dispose() { SideNav.right = null }});
        }
    }

    public get isOpen(): boolean {
        return !this.element.classList.contains('closed');
    }

    public beginPull() {
        this.element.classList.add('side-nav-dragging');
    }

    public endPull() {
        this.element.classList.remove('side-nav-dragging');
    }

    public setTransform(openRatio: number | null = null): void {
        if (ScreenSize.isWide()) {
            this.element.style.transform = null;
            return;
        }
        if (openRatio === null) {
            this.element.style.transform = null;
            return;
        }

        const isLeft = this.side == SideNavSide.Left;
        const closeDirectionSign = isLeft ? -1 : 1;
        const closeRatio = 1 - openRatio;
        const translateRatio = closeDirectionSign * closeRatio;
        this.element.style.transform = `translate3d(${100 * translateRatio}%, 0, 0)`;
    }

    public setVisibility = serialize(async (isOpen: boolean): Promise<void> => {
        if (this.isOpen === isOpen)
            return;

        debugLog?.log(`setVisibility:`, isOpen);
        await this.blazorRef.invokeMethodAsync('OnVisibilityChanged', isOpen);
    });
}

// Gestures

class SideNavPullDetectGesture extends Gesture {
    public static use(sideNav: SideNav): Disposable {
        debugLog?.log(`SideNavPullDetectGesture.use: sideNav.side:`, sideNav.side);

        return fromSubscription(DocumentEvents.capturedPassive.touchStart$.subscribe((event: TouchEvent) => {
            const target = event.target;
            if (!(target instanceof HTMLElement) && !(target instanceof SVGElement))
                return;
            if (sideNav.opposite?.isOpen === true)
                return; // The other SideNav is open, so only it can be pulled

            for (const activeGesture of Gestures.activeGestures)
                if (activeGesture instanceof SideNavPullGesture)
                    return; // Pull is already ongoing / completing

            Gestures.addActive(new SideNavPullDetectGesture(sideNav, getCoords(event), event));
        }));
    }

    constructor(
        public readonly sideNav: SideNav,
        public readonly origin: Vector2D,
        public readonly touchStartEvent: TouchEvent,
    ) {
        super();
        const initialState = new MoveState(0, sideNav.isOpen ? 1 : 0);

        this.addDisposables(
            DocumentEvents.capturedPassive.touchEnd$.subscribe(() => this.dispose()),
            DocumentEvents.capturedPassive.touchCancel$.subscribe(() => this.dispose()),
            DocumentEvents.capturedPassive.touchMove$.subscribe((event: TouchEvent) => {
                if (ScreenSize.isWide()) {
                    this.dispose();
                    return;
                }

                const coords = getCoords(event);
                const offset = coords.sub(this.origin);
                if (offset.length < PrePullDistance || Date.now() - initialState.startedAt < PrePullDurationMs)
                    return; // Too small pull distance or too early to start the pull

                const isLeft = sideNav.side == SideNavSide.Left;
                const isOpenSign = sideNav.isOpen ? 1 : -1;
                const openDirectionSign = isLeft ? 1 : -1;
                const allowedDirectionSign = openDirectionSign * -isOpenSign;
                if (!offset.isHorizontal() || Math.abs(Math.sign(offset.x) - allowedDirectionSign) > 0.1) {
                    // Wrong direction
                    debugLog?.log(`SideNavPullDetectGesture.touchMove: wrong direction`);
                    this.dispose();
                    return;
                }
                if (!sideNav.isOpen) {
                    let boundary = coords.x / ScreenSize.width;
                    if (!isLeft)
                        boundary = 1 - boundary;
                    if (boundary > PullBoundary) {
                        this.dispose();
                        return;
                    }
                }

                Gestures.addActive(new SideNavPullGesture(sideNav, origin, initialState, touchStartEvent, event));
                this.dispose();
            }),
        );
    }
}

class SideNavPullGesture extends Gesture {
    constructor(
        public readonly sideNav: SideNav,
        origin: Vector2D,
        initialState: MoveState,
        touchStartEvent: TouchEvent,
        firstMoveEvent: TouchEvent,
    ) {
        super();
        const isOpen = sideNav.isOpen;
        const isLeft = sideNav.side == SideNavSide.Left;
        const isOpenSign = sideNav.isOpen ? 1 : -1;
        const openDirectionSign = isLeft ? 1 : -1;
        const allowedDirectionSign = openDirectionSign * -isOpenSign;
        let state = initialState;

        const endMove = (event: TouchEvent, isCancelled: boolean) => {
            if (state === null)
                return;

            debugLog?.log('SideNavPullGesture.endMove:', event, ', isCancelled:', isCancelled);

            const moveDuration = Date.now() - state.startedAt;
            if (moveDuration < MinPullDurationMs)
                isCancelled = true;

            let mustBeOpen = isOpen;
            if (state && !isCancelled && !ScreenSize.isWide())
                mustBeOpen = state.terminalOpenRatio > 0.5;

            state = null; // Ended
            sideNav.setTransform(mustBeOpen ? 1 : 0);
            sideNav.endPull();
            (async () => {
                try {
                    if (sideNav.isOpen == mustBeOpen)
                        return;

                    await sideNav.setVisibility(mustBeOpen);
                    // Make sure changes are applied to DOM
                    const endTime = Date.now() + MaxSetVisibilityWaitDurationMs;
                    while (sideNav.isOpen != mustBeOpen && Date.now() < endTime)
                        await delayAsync(50);
                }
                finally {
                    sideNav.setTransform(null);
                    this.dispose();
                }
            })();
        }

        const move = (event: TouchEvent) => {
            if (state === null)
                return;

            if (event !== firstMoveEvent && event.cancelable && !event.defaultPrevented)
                preventDefaultForEvent(event);

            if (ScreenSize.isWide()) {
                endMove(event, true);
                return;
            }

            const coords = getCoords(event);
            const offset = coords.sub(origin);
            if (!offset.isHorizontal()) {
                // Wrong direction
                endMove(event, true);
                return;
            }

            const dx = isOpen ? offset.x : coords.x - (isLeft ? 0 : ScreenSize.width);
            const pdx = dx * allowedDirectionSign; // Must be positive
            const pullRatio = clamp(pdx / (sideNav.element.clientWidth + 0.01), 0, 1);
            const openRatio = isOpen ? 1 - pullRatio : pullRatio;
            state = new MoveState(pullRatio, openRatio, state);
            sideNav.setTransform(state.openRatio);
        }

        sideNav.beginPull();
        sideNav.setTransform(null);
        move(firstMoveEvent);

        this.addDisposables(
            DocumentEvents.active.touchEnd$.subscribe(e => endMove(e, false)),
            DocumentEvents.active.touchCancel$.subscribe(e => endMove(e, true)),
            DocumentEvents.active.touchMove$.subscribe(move),
        );
    }
}

// Helpers

class MoveState {
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
}

function getCoords(e: PointerEvent | TouchEvent): Vector2D {
    if (e['touches']) {
        const touch = (e as TouchEvent).touches[0];
        return new Vector2D(touch.pageX, touch.pageY);
    }

    const pe = e as PointerEvent;
    return new Vector2D(pe.pageX, pe.pageY);
}
