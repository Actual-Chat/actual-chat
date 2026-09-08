import { clamp, Vector2D } from 'math';
import { delayAsync, PromiseSourceWithTimeout, serialize } from 'actuallab-core';
import { DeviceInfo } from 'device-info';
import { Disposable, DisposableBag, Disposables } from 'disposable';
import { DocumentEvents, tryPreventDefaultForEvent } from 'event-handling';
import { fromEvent } from 'rxjs';
import { Gesture, Gestures } from 'gestures';
import { PullAnimation } from 'pull-animation';
import { ScrollController } from 'scroll-controller';
import { Timeout } from 'timeout';
import { ScreenSize } from '../../Services/ScreenSize/screen-size';
import { getLogs } from 'logging';
import { unselect } from 'keyboard';
import { BrowserInfo } from '../../Services/BrowserInfo/browser-info';
import { fastRaf, fastReadRafAsync, fastWriteRafAsync } from 'fast-raf';

const { debugLog } = getLogs('SideNav');

const PullBoundary = 0.333; // 33% of the screen width
// Vector2D.isHorizontal(r) is |x| > r*|y|, so r = 1/tan(angle-from-horizontal). Starting a pull
// asks for a more committed swipe than keeping one alive, so a wandering finger doesn't drop it.
const PullStartAngleRatio = 1.428; // 1/tan(35deg)
const PullDropAngleRatio = 0.839; // 1/tan(50deg)
const PrePullDistance1 = 10; // Normal pre-pull distance in CSS pixels
const PrePullDistance2 = 20; // Pre-pull distance over control
const PrePullDurationMs = 20;
const MinPullDurationMs = 20;
const MaxChatViewScroll = 40;
// Outlasts the longest settle PullAnimation can produce, so it only fires if frames stop coming
const MaxSettleWaitDurationMs = 500;
// SSB round-trips the visibility change through the server, so this has to outlast a slow
// network - the finally below disposes before it waits, so a long bound blocks nothing
const MaxSetVisibilityWaitDurationMs = 3000;
// Native history navigation can take over without dispatching the final touch event.
const PullGestureStaleMs = 1000;
const WidthCacheDurationMs = 1000;

enum SideNavSide {
    Left,
    Right,
}

interface SideNavOptions {
    side: SideNavSide;
}

type TouchResponsiveControlKind = 'none' | 'control' | 'scrollable' | 'unknown';

export class SideNav extends DisposableBag {
    public static left: SideNav;
    public static right: SideNav;

    private readonly contentDiv: HTMLElement;
    private readonly bodyClassWhenOpen: string;
    private _isPulling = false;
    private _isTransformed = false;
    private _width = 0;
    private _widthCapturedAt = 0;
    private pageWithHeaderAndFooter: HTMLElement;

    public readonly hasHistoryNavigationGesture: boolean;
    public get side(): SideNavSide { return this.options.side; }
    // Cached: clientWidth forces a layout, and a pull reads it on every frame
    public get width(): number {
        const now = performance.now();
        if (now - this._widthCapturedAt >= WidthCacheDurationMs) {
            this._width = this.element.clientWidth;
            this._widthCapturedAt = now;
        }
        return this._width;
    }
    // Nullable: SideNav.left/right are cleared to null! on dispose, so only one side may be registered
    public get opposite(): SideNav | null { return this.side == SideNavSide.Left ? SideNav.right : SideNav.left; }
    public get isOpen() { return this.element.dataset.sideNav === 'open'; }
    public get isPulling() { return this._isPulling; }
    public set isPulling(value: boolean) {
        this._isPulling = value;
        this.element.toggleAttribute('data-pulling', value);
        if (value)
            ScrollController.cancelMomentumAll();
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
        public readonly blazorRef: DotNet.DotNetObject,
        public readonly options: SideNavOptions,
    ) {
        super();
        this.contentDiv = element.firstElementChild as HTMLElement;
        this.bodyClassWhenOpen = `side-nav-${this.side == SideNavSide.Left ? 'left' : 'right'}-open`;
        this.hasHistoryNavigationGesture = DeviceInfo.isWebKit && BrowserInfo.hostKind !== 'MauiApp';
        this.pageWithHeaderAndFooter = element.closest('.page-with-header-and-footer')!;
        const stateObserver = new MutationObserver(() => this.updateBodyClassList());
        stateObserver.observe(this.element, { attributeFilter: ['data-side-nav'] });
        if (this.side == SideNavSide.Left)
            SideNav.left = this;
        else
            SideNav.right = this;
        const pullGestureDisposer = SideNavPullDetectGesture.use(this);
        this.addDisposables(pullGestureDisposer, {
            dispose: () => {
                if (SideNav.left === this)
                    SideNav.left = null!;
                else if (SideNav.right === this)
                    SideNav.right = null!;
                document.body.classList.remove(this.bodyClassWhenOpen);
                stateObserver.disconnect();
            },
        });

        this.updateBodyClassList();

        void delayAsync(250).then(async () => {
            // No transitions immediately after the first render
            await fastWriteRafAsync();
            this.element.toggleAttribute('data-animated', true);
        });
    }

    // Call during RAF
    public resetTransform(): void {
        debugLog?.log('resetTransform()');
        this.setTransform(this.isOpen ? 1 : 0);
    }

    // Call during RAF
    public setTransform(openRatio: number): void {
        // The backdrop and its blur are constant for the whole gesture, so they live on
        // [data-transformed] in CSS - what's left here is the only per-frame write there is.
        const mustTransform = !ScreenSize.isWide() && (this.isOpen ? openRatio < 1 : openRatio > 0);
        if (mustTransform !== this._isTransformed) {
            this._isTransformed = mustTransform;
            this.element.toggleAttribute('data-transformed', mustTransform);
        }
        if (!mustTransform) {
            this.element.style.transform = null!;
            this.contentDiv.style.opacity = null!;
            return;
        }

        const isLeft = this.side == SideNavSide.Left;
        const closeDirectionSign = isLeft ? -1 : 1;
        const closeRatio = 1 - openRatio;
        const translateRatio = closeDirectionSign * closeRatio;
        const opacity = Math.min(1, 0.05 + Math.pow(openRatio, 0.35));
        this.element.style.transform = `translate3d(${100 * translateRatio}%, 0, 0)`;
        this.contentDiv.style.opacity = opacity.toString();
    }

    public setVisibility = serialize(async (isOpen: boolean): Promise<void> => {
        if (this.isOpen === isOpen)
            return;

        debugLog?.log(`setVisibility:`, isOpen);
        await this.blazorRef.invokeMethodAsync('OnVisibilityChanged', isOpen);
    });

    // Private methods

    // Call during RAF
    private updateBodyClassList(): void {
        if (this.isOpen) {
            document.body.classList.add(this.bodyClassWhenOpen);
            if (ScreenSize.isNarrow())
                unselect(true);
        } else {
            document.body.classList.remove(this.bodyClassWhenOpen);
        }
    }
}

// Gestures

class SideNavPullDetectGesture extends Gesture {
    public static use(sideNav: SideNav): Disposable {
        debugLog?.log(`SideNavPullDetectGesture.use[${sideNav.side}]`);

        const touchStartEvent = sideNav.hasHistoryNavigationGesture
            ? DocumentEvents.capturedActive.touchStart$
            : DocumentEvents.capturedPassive.touchStart$;

        return Disposables.fromSubscription(touchStartEvent.subscribe((event: TouchEvent) => { void (async () => {
            if (ScreenSize.isWide())
                return;

            await fastReadRafAsync();

            if (document.querySelector('.modal')) // Modal is shown
                return;
            if (document.querySelector('.ac-menu-host.has-overlay')) // Context menu is shown
                return;
            if (document.querySelector('.ac-bubble-host > .ac-bubble')) // Walk-through bubble is shown
                return;

            if (!event.target)
                return; // Not sure if this is possible, but just in case

            const editor = document.querySelector('.chat-message-editor');
            if (editor?.contains(event.target as Node))
                return;

            const tabs = document.querySelectorAll('.tab-panel-tabs');
            for (const tab of tabs) {
                if (tab.contains(event.target as Node))
                    return;
            }

            const prePullDistance = getPrePullDistance(event.target);
            if (!prePullDistance)
                return;

            if (sideNav.opposite?.isOpen) {
                // The other SideNav is open
                if (!sideNav.isOpen)
                    return; // And this SideNav is closed, so only other SideNav can be pulled
                if (sideNav.side === SideNavSide.Right)
                    return; // And this is the right SideNav - while the left one is always on top
            }

            for (const activeGesture of Gestures.activeGestures) {
                if (activeGesture instanceof SideNavPullGesture && activeGesture.isActive)
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

            // Placed after the block above, so an edge swipe over such an element still
            // suppresses the browser's history-navigation gesture
            if (event.target instanceof Element && event.target.closest('[data-no-side-nav-pull]'))
                return; // The element pans on its own (e.g. an interactive map)

            Gestures.addActive(new SideNavPullDetectGesture(sideNav, coords, event, prePullDistance));
        })(); }));
    }

    constructor(
        public readonly sideNav: SideNav,
        public readonly origin: Vector2D,
        public readonly touchStartEvent: TouchEvent,
        public readonly prePullDistance: number,
    ) {
        super();
        const startedAt = performance.now();

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
            if (offset.length < prePullDistance || performance.now() - startedAt < PrePullDurationMs)
                return; // Too small pull distance or too early to start the pull

            const isLeft = sideNav.side == SideNavSide.Left;
            const isOpenSign = sideNav.isOpen ? 1 : -1;
            const openDirectionSign = isLeft ? 1 : -1;
            const allowedDirectionSign = openDirectionSign * -isOpenSign;
            const isHorizontal = offset.isHorizontal(PullStartAngleRatio);
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

            Gestures.addActive(new SideNavPullGesture(sideNav, origin, startedAt, touchStartEvent, event));
            this.dispose();
        };

        const chatViewDiv = document.querySelector('.chat-view.virtual-list');
        this.addDisposables(
            DocumentEvents.capturedPassive.touchCancel$.subscribe(() => this.dispose()),
            DocumentEvents.capturedPassive.touchEnd$.subscribe(e => {
                move(e);
                this.dispose();
            }),
            DocumentEvents.capturedPassive.touchMove$.subscribe(e => move(e)),
            chatViewDiv
                ? Disposables.fromSubscription(fromEvent(chatViewDiv, 'scroll').subscribe(() => this.dispose()))
                : Disposables.empty(),
        );
    }
}

// Owns one pull from touchstart to the end of the settle. PullAnimation carries the position;
// this class only feeds it the finger and writes the result out once per frame, so touchmove does
// no DOM work at all and a frame that carries no touch sample still moves the panel.
class SideNavPullGesture extends Gesture {
    private readonly isLeft: boolean;
    private readonly wasOpen: boolean;
    private readonly allowedDirectionSign: number;
    private readonly animation: PullAnimation;
    private readonly chatViewDiv: Element | null;
    private readonly settled = new PromiseSourceWithTimeout<void>();
    private staleTimeout: Timeout | null = null;
    private lastCoords: Vector2D | null = null;
    private chatViewScrollTop: number | null = null;
    private isEnded = false;

    // False once endMove starts - i.e. during the settle, well before dispose()
    public get isActive() { return !this.isEnded && !this.isDisposed; }

    constructor(
        public readonly sideNav: SideNav,
        public readonly origin: Vector2D,
        public readonly startedAt: number,
        public readonly touchStartEvent: TouchEvent,
        public readonly firstMoveEvent: TouchEvent,
    ) {
        super();
        this.isLeft = sideNav.side == SideNavSide.Left;
        this.wasOpen = sideNav.isOpen;
        const isOpenSign = this.wasOpen ? 1 : -1;
        const openDirectionSign = this.isLeft ? 1 : -1;
        this.allowedDirectionSign = openDirectionSign * -isOpenSign;
        this.animation = new PullAnimation(this.wasOpen ? 1 : 0, performance.now());
        this.chatViewDiv = document.querySelector('.chat-view.virtual-list');

        if (firstMoveEvent.type === 'touchend') {
            void this.endMove(firstMoveEvent, false);
        } else {
            this.move(firstMoveEvent);
            const chatViewDiv = this.chatViewDiv;
            this.addDisposables(
                DocumentEvents.capturedActive.touchEnd$.subscribe(e => { void this.endMove(e, false); }),
                DocumentEvents.capturedActive.touchCancel$.subscribe(e => { void this.endMove(e, true); }),
                // Just in case
                DocumentEvents.capturedActive.touchStart$.subscribe(e => { void this.endMove(e, true); }),
                DocumentEvents.capturedActive.touchMove$.subscribe(e => this.move(e)),
                chatViewDiv
                    ? Disposables.fromSubscription(fromEvent(chatViewDiv, 'scroll').subscribe(() => {
                    // This doesn't work on Safari - i.e. it still drags the chat view while you move:
                    // chatViewDiv.scrollTop = this.chatViewScrollTop;
                        if (this.chatViewScrollTop !== null
                            && Math.abs(chatViewDiv.scrollTop - this.chatViewScrollTop) > MaxChatViewScroll)
                            void this.endMove(null, true);
                    }))
                    : Disposables.empty(),
            );
        }
        this.scheduleFrame();
    }

    public dispose() {
        if (this.isDisposed)
            return;

        debugLog?.log('dispose()');
        this.staleTimeout?.dispose();
        this.staleTimeout = null;
        this.isEnded = true;
        this.settled.resolve(undefined);
        super.dispose();
    }

    // Private methods

    private move(event: TouchEvent): void {
        if (!this.isActive)
            return;

        this.staleTimeout?.dispose();
        this.staleTimeout = new Timeout(PullGestureStaleMs, () => { void this.endMove(null, true); });
        if (ScreenSize.isWide()) {
            void this.endMove(event, true);
            return;
        }

        tryPreventDefaultForEvent(event);
        const coords = getCoords(event);
        if (!coords)
            return;

        if (!coords.sub(this.origin).isHorizontal(PullDropAngleRatio)) {
            void this.endMove(event, true);
            return;
        }

        this.lastCoords = coords;
    }

    private async endMove(event: TouchEvent | null, isCancelled: boolean): Promise<void> {
        if (this.isEnded)
            return;

        this.isEnded = true;
        this.staleTimeout?.dispose();
        this.staleTimeout = null;
        const sideNav = this.sideNav;
        debugLog?.log(`SideNavPullGesture[${sideNav.side}].endMove:`, event, ', isCancelled:', isCancelled);

        tryPreventDefaultForEvent(event);
        const moveDuration = performance.now() - this.startedAt;
        if (event === null || event.type === 'touchstart' || moveDuration < MinPullDurationMs)
            isCancelled = true;

        const coords = event === null ? null : getCoords(event);
        if (coords && !isCancelled) {
            this.lastCoords = coords;
            this.animation.setTarget(this.openRatioAt(coords));
        }

        // A cancelled pull goes back where it started; otherwise the projected rest point decides
        const mustRevert = isCancelled || ScreenSize.isWide();
        this.animation.release(performance.now(), mustRevert ? (this.wasOpen ? 1 : 0) : undefined);
        const mustBeOpen = this.animation.terminalRatio > 0.5;
        debugLog?.log(`SideNavPullGesture[${sideNav.side}].endMove: ending w/ mustBeOpen:`, mustBeOpen);
        try {
            // The magnet starts here, ahead of Blazor hearing about it below - the one call that
            // can't be derived on the .NET side.
            if (sideNav.isOpen != mustBeOpen)
                void sideNav.blazorRef.invokeMethodAsync('OnPullSettling');

            this.settled.setTimeout(MaxSettleWaitDurationMs, () => this.settled.resolve(undefined));
            await this.settled;
            if (sideNav.isOpen == mustBeOpen)
                return; // Note that we call sideNav.setTransform() in finally { ... }

            // A stopped WebView leaves this interop call unresolved, and setVisibility is
            // serialized - unbounded, it would strand the finally below and with it dispose()
            const visibilityChanged = new PromiseSourceWithTimeout<void>();
            visibilityChanged.setTimeout(
                MaxSetVisibilityWaitDurationMs, () => visibilityChanged.resolve(undefined));
            void sideNav.setVisibility(mustBeOpen)
                .catch(() => undefined)
                .then(() => visibilityChanged.resolve(undefined));
            await visibilityChanged;

            const endTime = performance.now() + MaxSetVisibilityWaitDurationMs;
            while (sideNav.isOpen != mustBeOpen && performance.now() < endTime) {
                await delayAsync(50);
                await fastReadRafAsync();
            }
        } finally {
            // Unregisters before the await, not after: rAF never fires in a backgrounded
            // WebView, and a gesture left active blocks every later pull
            this.dispose();
            await fastWriteRafAsync();
            sideNav.isPulling = false;
            sideNav.setTransform(mustBeOpen ? 1 : 0);
        }
    }

    private scheduleFrame(): void {
        if (this.isDisposed)
            return;

        fastRaf({ read: time => this.onFrameRead(time), write: () => this.onFrameWrite() });
    }

    private onFrameRead(time: number): void {
        if (this.isDisposed)
            return;

        this.chatViewScrollTop ??= this.chatViewDiv?.scrollTop ?? 0;
        if (this.animation.phase === 'follow' && this.lastCoords)
            this.animation.setTarget(this.openRatioAt(this.lastCoords));
        this.animation.advance(time);
    }

    private onFrameWrite(): void {
        if (this.isDisposed)
            return;

        if (!this.sideNav.isPulling)
            this.sideNav.isPulling = true;
        this.sideNav.setTransform(this.animation.ratio);
        if (this.animation.isDone)
            this.settled.resolve(undefined);
        else
            this.scheduleFrame();
    }

    // Call during the RAF read phase - sideNav.width can force a layout
    private openRatioAt(coords: Vector2D): number {
        const dx = this.wasOpen
            ? coords.x - this.origin.x
            : coords.x - (this.isLeft ? 0 : ScreenSize.width);
        const pdx = dx * this.allowedDirectionSign; // Must be positive
        const pullRatio = clamp(pdx / (this.sideNav.width + 0.01), 0, 1);
        return this.wasOpen ? 1 - pullRatio : pullRatio;
    }
}

// Helpers

function getCoords(event?: TouchEvent): Vector2D | null {
    const touches = event?.changedTouches ?? event?.touches;
    if (!touches?.length)
        return null;

    const touch = touches[0];
    // clientX/Y, not pageX/Y: the panel is position: fixed, and every edge these coordinates are
    // compared against (0, ScreenSize.width, ScreenSize.height) is in viewport space too.
    return new Vector2D(touch.clientX, touch.clientY);
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
    if (tagName === 'INPUT') {
        if ((node as HTMLInputElement).type === 'range')
            return 'scrollable';
        return 'control';
    }
    if (tagName === 'BUTTON' || tagName === 'LABEL')
        return 'control';
    if (node.scrollWidth > (node.clientWidth + 0.5))
        return 'scrollable';

    return getTouchResponsiveControlKind(node.parentNode!);
}
