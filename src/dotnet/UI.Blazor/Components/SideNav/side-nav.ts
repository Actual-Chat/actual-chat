import { clamp, Vector2D } from 'math';
import { delayAsync, PromiseSourceWithTimeout, serialize } from 'promises';
import { DeviceInfo } from 'device-info';
import { Disposable, DisposableBag, fromSubscription } from 'disposable';
import { DocumentEvents, tryPreventDefaultForEvent } from 'event-handling';
import { fromEvent } from 'rxjs';
import { Gesture, Gestures } from 'gestures';
import { ScreenSize } from '../../Services/ScreenSize/screen-size';
import { Log } from 'logging';
import { BrowserInfo, HostKind } from '../../Services/BrowserInfo/browser-info';
import { fastRaf, fastReadRaf, fastWriteRaf } from 'fast-raf';

const { debugLog } = Log.get('SideNav');

const Deceleration = 0.1; // 1 = full width/second^2
const PullBoundary = 0.333; // 33% of the screen width
const PrePullDistance1 = 10; // Normal pre-pull distance in CSS pixels
const PrePullDistance2 = 20; // Pre-pull distance over control
const PrePullDurationMs = 20;
const MinPullDurationMs = 20;
const MaxChatViewScroll = 40;
const MaxSetVisibilityWaitDurationMs = 500;

enum SideNavSide {
    Left,
    Right,
}

interface SideNavOptions {
    side: SideNavSide;
}

type TouchResponsiveControlKind = 'none' | 'control' | 'scrollable' | 'unknown';

export class SideNav extends DisposableBag {
    public static left: SideNav | null = null;
    public static right: SideNav | null = null;

    private readonly contentDiv: HTMLElement;
    private readonly bodyClassWhenOpen: string;

    public readonly hasHistoryNavigationGesture: boolean;
    public get side(): SideNavSide { return this.options.side; }
    public get opposite(): SideNav { return this.side == SideNavSide.Left ? SideNav.right : SideNav.left; }
    public get isOpen() { return this.element.dataset['sideNav'] === 'open'; }
    public get isPulling() { return this.element.classList.contains('pulling')}
    public set isPulling(value: boolean) {
        if (value)
            this.element.classList.add('pulling');
        else
            this.element.classList.remove('pulling');
    }

    public static create(
        element: HTMLDivElement,
        blazorRef: DotNet.DotNetObject,
        options: SideNavOptions,
    ): SideNav {
        return new SideNav(element, blazorRef, options);
    }

    constructor(
        public readonly element: HTMLDivElement,
        private readonly blazorRef: DotNet.DotNetObject,
        public readonly options: SideNavOptions,
    ) {
        super();
        this.contentDiv = element.firstElementChild as HTMLElement;
        this.bodyClassWhenOpen = `side-nav-${this.side == SideNavSide.Left ? 'left' : 'right'}-open`;
        this.hasHistoryNavigationGesture = DeviceInfo.isWebKit && BrowserInfo.hostKind !== 'MauiApp';
        const stateObserver = new MutationObserver(() => this.updateBodyClassList());
        stateObserver.observe(this.element, { attributeFilter: ['data-side-nav'] });
        if (this.side == SideNavSide.Left)
            SideNav.left = this;
        else
            SideNav.right = this;
        const sideNav = this;

        const pullGestureDisposer = SideNavPullDetectGesture.use(this);
        this.addDisposables(pullGestureDisposer, {
            dispose() {
                if (SideNav.left === sideNav)
                    SideNav.left = null;
                else if (SideNav.right === sideNav)
                    SideNav.right = null;
                document.body.classList.remove(sideNav.bodyClassWhenOpen);
                stateObserver.disconnect();
            },
        });

        this.updateBodyClassList();

        delayAsync(250).then(async () => {
            // No transitions immediately after the first render
            await fastWriteRaf();
            this.element.classList.add('animated');
        });
    }

    /** Call during RAF */
    public resetTransform(): void {
        debugLog?.log('resetTransform()');
        this.setTransform(this.isOpen ? 1 : 0);
    }

    /** Call during RAF */
    public setTransform(openRatio: number): void {
        const mustTransform = !ScreenSize.isWide() && (this.isOpen ? openRatio < 1 : openRatio > 0);
        if (!mustTransform) {
            this.element.style.transform = null;
            this.element.style.backgroundColor = null;
            this.element.style.backdropFilter = null;
            this.contentDiv.style.opacity = null;
            this.contentDiv.style.backdropFilter = null;
            this.element.style.removeProperty('-webkit-backdrop-filter');
            this.contentDiv.style.removeProperty('-webkit-backdrop-filter');
            return;
        }

        const isLeft = this.side == SideNavSide.Left;
        const closeDirectionSign = isLeft ? -1 : 1;
        const closeRatio = 1 - openRatio;
        const translateRatio = closeDirectionSign * closeRatio;
        const opacity = Math.min(1, 0.05 + Math.pow(openRatio, 0.35));
        this.element.style.transform = `translate3d(${100 * translateRatio}%, 0, 0)`;
        this.element.style.backdropFilter = `blur(3px)`;
        this.contentDiv.style.backdropFilter = 'blur(3px)';
        this.element.style.setProperty('-webkit-backdrop-filter', 'blur(3px)');
        this.contentDiv.style.setProperty('-webkit-backdrop-filter', 'blur(3px)');
        this.element.style.backgroundColor = `rgba(1, 1, 1, 0)`;
        this.contentDiv.style.opacity = opacity.toString();
    }

    public setVisibility = serialize(async (isOpen: boolean): Promise<void> => {
        if (this.isOpen === isOpen)
            return;

        debugLog?.log(`setVisibility:`, isOpen);
        await this.blazorRef.invokeMethodAsync('OnVisibilityChanged', isOpen);
    });

    /** Call during RAF */
    private updateBodyClassList(): void {
        if (this.isOpen)
            document.body.classList.add(this.bodyClassWhenOpen);
        else
            document.body.classList.remove(this.bodyClassWhenOpen);
    }
}

// Gestures

class SideNavPullDetectGesture extends Gesture {
    public static use(sideNav: SideNav): Disposable {
        debugLog?.log(`SideNavPullDetectGesture.use[${sideNav.side}]`);

        const touchStartEvent = sideNav.hasHistoryNavigationGesture
            ? DocumentEvents.capturedActive.touchStart$
            : DocumentEvents.capturedPassive.touchStart$;

        return fromSubscription(touchStartEvent.subscribe(async (event: TouchEvent) => {
            if (ScreenSize.isWide())
                return;

            await fastReadRaf();

            if (document.querySelector('.modal')) // Modal is shown
                return;
            if (document.querySelector('.ac-menu-host.has-overlay')) // Context menu is shown
                return;
            if (document.querySelector('.ac-bubble-host > .ac-bubble')) // Walk-through bubble is shown
                return;

            const prePullDistance = getPrePullDistance(event.target);
            if (!prePullDistance)
                return;

            if (sideNav.opposite?.isOpen === true) {
                // The other SideNav is open
                if (!sideNav.isOpen)
                    return; // And this SideNav is closed, so only other SideNav can be pulled
                if (sideNav.side === SideNavSide.Right)
                    return; // And this is the right SideNav - while the left one is always on top
            }

            for (const activeGesture of Gestures.activeGestures) {
                if (activeGesture instanceof SideNavPullGesture)
                    return;
            }

            const coords = getCoords(event);
            if (!coords)
                return; // Not sure if this is possible, but just in case

            if (sideNav.hasHistoryNavigationGesture) {
                let pullEdge = sideNav.side === SideNavSide.Left ? 0 : 1;
                if (sideNav.isOpen)
                    pullEdge = 1 - pullEdge;
                const pullEdgeX = ScreenSize.width * pullEdge;

                const headerFooterHeight = 56;
                const isHeaderSwipe = coords.y <= headerFooterHeight;
                const isFooterSwipe = coords.y >= (ScreenSize.height - headerFooterHeight);
                if (isHeaderSwipe || isFooterSwipe)
                    return;

                const isEdgeSwipe = Math.abs(coords.x - pullEdgeX) <= 25;
                if (isEdgeSwipe) {
                    const isControlSwipe = prePullDistance === PrePullDistance2;
                    if (isControlSwipe)
                        return;

                    tryPreventDefaultForEvent(event);
                }
            }

            Gestures.addActive(new SideNavPullDetectGesture(sideNav, coords, event, prePullDistance));
        }));
    }

    constructor(
        public readonly sideNav: SideNav,
        public readonly origin: Vector2D,
        public readonly touchStartEvent: TouchEvent,
        public readonly prePullDistance: number,
    ) {
        super();
        const initialState = new MoveState(0, sideNav.isOpen ? 1 : 0);

        const move = (event: TouchEvent) => {
            if (this.isDisposed)
                return;

            if (ScreenSize.isWide()) {
                this.dispose();
                return;
            }

            const coords = getCoords(event);
            if (!coords) {
                // This is touchEnd on WebKit/Safari
                this.dispose();
                return;
            }

            const offset = coords.sub(this.origin);
            if (offset.length < prePullDistance || Date.now() - initialState.startedAt < PrePullDurationMs)
                return; // Too small pull distance or too early to start the pull

            const isLeft = sideNav.side == SideNavSide.Left;
            const isOpenSign = sideNav.isOpen ? 1 : -1;
            const openDirectionSign = isLeft ? 1 : -1;
            const allowedDirectionSign = openDirectionSign * -isOpenSign;
            const isHorizontal = offset.isHorizontal(1.732); // 1/tan(30deg) = 1.732
            if (!isHorizontal || Math.abs(Math.sign(offset.x) - allowedDirectionSign) > 0.1) {
                // Wrong direction
                debugLog?.log(`SideNavPullDetectGesture[${sideNav.side}].touchMove: wrong direction`);
                this.dispose();
                return;
            }

            if (!sideNav.isOpen) {
                let boundary = origin.x / ScreenSize.width;
                if (!isLeft)
                    boundary = 1 - boundary;
                if (boundary > PullBoundary) {
                    this.dispose();
                    return;
                }
            }
            if (sideNav.isPulling)
                return;

            Gestures.addActive(new SideNavPullGesture(sideNav, origin, initialState, touchStartEvent, event));
            this.dispose();
        };

        const chatViewDiv = document.querySelector('.chat-view.virtual-list');
        this.addDisposables(
            DocumentEvents.capturedPassive.touchCancel$.subscribe(_ => this.dispose()),
            DocumentEvents.capturedPassive.touchEnd$.subscribe(e => {
                move(e);
                this.dispose();
            }),
            DocumentEvents.capturedPassive.touchMove$.subscribe(e => move(e)),
            chatViewDiv
                ? fromSubscription(fromEvent(chatViewDiv, 'scroll').subscribe(_ => this.dispose()))
                : null,
        );
    }
}

class SideNavPullGesture extends Gesture {
    private state: MoveState;

    constructor(
        public readonly sideNav: SideNav,
        public readonly origin: Vector2D,
        public readonly initialState: MoveState,
        public readonly touchStartEvent: TouchEvent,
        public readonly firstMoveEvent: TouchEvent,
    ) {
        super();
        const isOpen = sideNav.isOpen;
        const isLeft = sideNav.side == SideNavSide.Left;
        const isOpenSign = sideNav.isOpen ? 1 : -1;
        const openDirectionSign = isLeft ? 1 : -1;
        const allowedDirectionSign = openDirectionSign * -isOpenSign;
        this.state = initialState;

        const endMove = async (event: TouchEvent | null, isCancelled: boolean): Promise<void> => {
            if (this.state === null)
                return;

            debugLog?.log(
                `SideNavPullGesture[${sideNav.side}].endMove:`,
                event,
                ', isCancelled:',
                isCancelled,
                ', state:',
                this.state);

            tryPreventDefaultForEvent(event);

            const moveDuration = Date.now() - this.state.startedAt;
            if (event === null || event.type === 'touchstart' || moveDuration < MinPullDurationMs)
                isCancelled = true;

            const coords = getCoords(event);
            if (coords && !isCancelled) {
                await move(event);
                if (this.state === null!) // move(event) may call endMove(..., true)
                    return;
            }

            let mustBeOpen = isOpen;
            if (this.state && !isCancelled && !ScreenSize.isWide())
                mustBeOpen = this.state.terminalOpenRatio > 0.5;

            debugLog?.log(`SideNavPullGesture[${sideNav.side}].endMove: ending w/ mustBeOpen:`, mustBeOpen);
            this.state = null; // Ended
            try {
                await fastWriteRaf();
                sideNav.isPulling = false;
                if (sideNav.isOpen == mustBeOpen)
                    return; // Note that we call sideNav.resetTransform() in finally { ... }

                // "Pre-apply" visibility change
                sideNav.setTransform(mustBeOpen ? 1 : 0);

                const transitionEnded = new PromiseSourceWithTimeout<void>();
                transitionEnded.setTimeout(MaxSetVisibilityWaitDurationMs);
                sideNav.element.addEventListener('transitionend', async () => {
                    transitionEnded.resolve(undefined);
                }, { once: true });

                // Wait when the changes are applied to DOM
                await transitionEnded;
                await sideNav.setVisibility(mustBeOpen);

                const endTime = Date.now() + MaxSetVisibilityWaitDurationMs;
                while (sideNav.isOpen != mustBeOpen && Date.now() < endTime) {
                    await delayAsync(50);
                    await fastReadRaf();
                }
            } finally {
                await fastWriteRaf();
                sideNav.setTransform(mustBeOpen ? 1 : 0);
                this.dispose();
            }
        };

        const move = async (event: TouchEvent): Promise<void> => {
            if (this.state === null)
                return;

            if (ScreenSize.isWide()) {
                await endMove(event, true);
                return;
            }

            tryPreventDefaultForEvent(event);

            const coords = getCoords(event);
            const offset = coords.sub(origin);
            if (!offset.isHorizontal()) { // >45 deg. vertical
                await endMove(event, true);
                return;
            }

            fastRaf({
                read: () => {
                    if (this.state === null)
                        return;

                    const dx = isOpen ? offset.x : coords.x - (isLeft ? 0 : ScreenSize.width);
                    const pdx = dx * allowedDirectionSign; // Must be positive
                    const pullRatio = clamp(pdx / (sideNav.element.clientWidth + 0.01), 0, 1);
                    const openRatio = isOpen ? 1 - pullRatio : pullRatio;
                    this.state = new MoveState(pullRatio, openRatio, this.state);
                },
                write: () => {
                    if (this.state === null)
                        return;

                    sideNav.setTransform(this.state.openRatio);
                    // console.warn(this.state);
                },
            });
        };

        fastRaf({
            read: async () => {
                const chatViewDiv = document.querySelector('.chat-view.virtual-list');
                const initialChatViewScrollTop = chatViewDiv?.scrollTop;
                if (firstMoveEvent.type === 'touchend') {
                    await endMove(firstMoveEvent, false);
                } else {
                    try {
                        await move(firstMoveEvent);
                    } catch (e) {
                        await endMove(firstMoveEvent, true);
                        throw e;
                    }
                    this.addDisposables(
                        DocumentEvents.active.touchEnd$.subscribe(e => endMove(e, false)),
                        DocumentEvents.active.touchCancel$.subscribe(e => endMove(e, true)),
                        DocumentEvents.active.touchStart$.subscribe(e => endMove(e, true)), // Just in case
                        DocumentEvents.active.touchMove$.subscribe(move),
                        chatViewDiv
                        ? fromSubscription(fromEvent(chatViewDiv, 'scroll').subscribe(_ => {
                            // This doesn't work on Safari - i.e. it still drags the chat view while you move:
                            // chatViewDiv.scrollTop = initialChatViewScrollTop;
                            if (Math.abs(chatViewDiv.scrollTop - initialChatViewScrollTop) > MaxChatViewScroll)
                                void endMove(null, true);
                        }))
                        : null,
                    );
                }
            },
            write: () => {
                sideNav.isPulling = true;
                sideNav.setTransform(isOpen ? 1 : 0);
            },
        });
    }


    public dispose() {
        if (this.isDisposed)
            return;

        debugLog?.log('dispose()');
        this.state = null;
        super.dispose();
    }
}

// Helpers

class MoveState {
    public readonly startedAt: number;
    public readonly capturedAt: number;
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
        const s = prevMoveState?.prevMoveState ?? prevMoveState;
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

// Helpers

function getCoords(event?: TouchEvent): Vector2D | null {
    let touches = event?.changedTouches ?? event?.touches;
    if (!touches?.length)
        return null;

    const touch = touches[0];
    return new Vector2D(touch.pageX, touch.pageY);
}

function getPrePullDistance(node: EventTarget): number | null {
    const controlKind = getTouchResponsiveControlKind(node);
    debugLog?.log(`getPrePullDistance: node:`, node, `, controlKind:`, controlKind);
    switch (controlKind) {
    case 'none':
        return PrePullDistance1;
    case 'control':
        return PrePullDistance2;
    default: // 'scrollable' | 'unknown'
        return null;
    }
}

function getTouchResponsiveControlKind(node: EventTarget): TouchResponsiveControlKind {
    if (!(node instanceof HTMLElement || node instanceof SVGElement))
        return 'unknown';

    const tagName = node.tagName;
    if (tagName === 'BODY' || tagName === 'HTML')
        return 'none';
    if (tagName === 'INPUT' || tagName === 'BUTTON' || tagName === 'LABEL')
        return 'control';
    if (node.scrollWidth > (node.clientWidth + 0.5))
        return 'scrollable';

    return getTouchResponsiveControlKind(node.parentNode);
}
