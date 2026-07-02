// TODO: Fix ESLint errors
/* eslint-disable @typescript-eslint/no-floating-promises */
// TODO: remove eslint-disables and fix errors
/* eslint-disable @typescript-eslint/no-unnecessary-condition,@typescript-eslint/require-await,@typescript-eslint/no-unsafe-member-access */
import {
    Subject,
    takeUntil,
    fromEvent
} from 'rxjs';
import { fastRaf } from 'fast-raf';

export class RightPanelHeader {
    private blazorRef: DotNet.DotNetObject;
    private readonly header: HTMLElement;
    private readonly varTarget: HTMLElement;
    private avatarDiv: HTMLElement;
    private headerWidth: number;
    private centerDiv: HTMLElement;
    private smallAvatar: HTMLElement;
    private smallAvatarContent: HTMLElement;
    private readonly rightSideNav: HTMLElement;
    private mutationObserver: MutationObserver;
    private resizeObserver: ResizeObserver;
    private readonly disposed$: Subject<void> = new Subject<void>();

    private resizeRaf = 0;
    private closeFallbackTimer = 0;

    static create(header: HTMLDivElement, blazorRef: DotNet.DotNetObject): RightPanelHeader {
        return new RightPanelHeader(header, blazorRef);
    }

    constructor(header: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this.header = header;
        this.blazorRef = blazorRef;
        // Expose --expanded-header-height on the right panel (not just the header) so the sibling
        // scroll region (.c-panel-content) can read it and slide below the full-screen avatar square.
        this.varTarget = header.closest<HTMLElement>('.right-panel') ?? header;
        this.rightSideNav = document.querySelector('.side-nav-right')!;
        if (!this.rightSideNav)
            return;

        this.avatarDiv = this.header.querySelector('.chat-avatar')!;
        if (!this.avatarDiv)
            return;

        this.smallAvatar = this.avatarDiv.querySelector('.small-avatar')!;
        if (!this.smallAvatar)
            return;

        this.smallAvatarContent = this.smallAvatar.querySelector('.c-content')!;
        if (!this.smallAvatarContent)
            return;

        fromEvent(this.smallAvatarContent, 'transitionend')
            .pipe(takeUntil(this.disposed$))
            .subscribe((e: TransitionEvent) => {
                if (e.propertyName !== 'height')
                    return;

                this.fullSizeAvatarOpacityHandler();
            });

        this.centerDiv = this.header.querySelector('.c-center')!;

        this.headerWidth = this.header.offsetWidth;
        this.varTarget.style.setProperty('--expanded-header-height', `${this.headerWidth}px`);

        this.mutationObserver = new MutationObserver(this.stateHandler);
        this.mutationObserver.observe(this.header, {
            attributes: true,
            attributeFilter: ['class'],
            attributeOldValue: true,
        });

        this.resizeObserver = new ResizeObserver(this.resizeSideNavHandler);
        this.resizeObserver.observe(this.rightSideNav);

        fromEvent(this.avatarDiv, 'click')
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => this.onAvatarClickHandler());

        this.centerDiv.addEventListener('transitionend', this.onTransitionEndBound);

        // While the avatar is full-screen, swiping it up or down (as well as a plain tap) closes the
        // full-screen view; a following list scroll then resumes the normal collapse.
        let swipeStartY = 0;
        let swipeTracking = false;
        fromEvent<TouchEvent>(this.avatarDiv, 'touchstart')
            .pipe(takeUntil(this.disposed$))
            .subscribe(e => {
                swipeTracking = this.header.classList.contains('expanded-header');
                swipeStartY = e.touches[0]?.clientY ?? 0;
            });
        fromEvent<TouchEvent>(this.avatarDiv, 'touchmove')
            .pipe(takeUntil(this.disposed$))
            .subscribe(e => {
                if (!swipeTracking)
                    return;
                if (Math.abs((e.touches[0]?.clientY ?? 0) - swipeStartY) > 40) {
                    swipeTracking = false;
                    this.closeFullScreenAvatar();
                }
            });
        fromEvent<TouchEvent>(this.avatarDiv, 'touchend')
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => swipeTracking = false);
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
        this.header.removeEventListener('transitionend', this.onTransitionEndBound);
        clearTimeout(this.closeFallbackTimer);
        this.resizeObserver?.disconnect();
        this.mutationObserver?.disconnect();
    }

    // Public methods

    private fullSizeAvatarOpacityHandler() {
        const fullSizeAvatar = this.header.querySelector('.full-size-avatar')!;
        if (!fullSizeAvatar)
            return;

        const opacity = parseFloat(getComputedStyle(fullSizeAvatar).opacity || '0');
        // @ts-expect-error TODO: fix error
        fullSizeAvatar.style.opacity = opacity === 0 ? '1' : '0';
    }

    private resizeSideNavHandler: ResizeObserverCallback = (entries) => {
        const width = entries[0].contentRect.width;

        if (!this.header.classList.contains('expanded-header'))
            return;

        if (Math.abs(width - this.headerWidth) < 2)
            return;

        this.headerWidth = width;

        cancelAnimationFrame(this.resizeRaf);
        this.resizeRaf = requestAnimationFrame(() => {
            fastRaf(() => {
                this.varTarget.style.setProperty('--expanded-header-height', `${width}px`);
            });
        });
    };

    private stateHandler: MutationCallback = (mutations) => {
        for (const mutation of mutations) {
            if (mutation.type === 'attributes' && mutation.attributeName === 'class') {
                const target = mutation.target as HTMLElement;
                const oldClassList = mutation.oldValue?.split(/\s+/) ?? [];
                const newClassList = target.classList;

                const hadExpandedClass = oldClassList.includes('expanded-header');
                const hasExpandedClass = newClassList.contains('expanded-header');
                const hadCollapsedClass = oldClassList.includes('collapsed-header');
                const hasCollapsedClass = newClassList.contains('collapsed-header');

                if (!hadExpandedClass && hasExpandedClass) {
                    // expand
                    this.changeHeader(true);
                }

                if (!hadCollapsedClass && hasCollapsedClass) {
                    // collapse
                    this.changeHeader(false);
                }
            }
        }
    };

    private changeHeader(openFullScreenAvatar = false) {
        if (openFullScreenAvatar) {
            this.headerWidth = this.header.offsetWidth;
            this.varTarget.style.setProperty('--expanded-header-height', `${this.headerWidth}px`);
        }
    }

    private onAvatarClickHandler = () => {
        const clickableAvatar = this.header.querySelector('.clickable-avatar');
        if (!clickableAvatar)
            return;

        const isExpanded = this.header.classList.contains('expanded-header');

        // While the header is collapsed (scrolled compact), the avatar is inert.
        const rightPanel = this.header.closest('.right-panel');
        if (!isExpanded && rightPanel?.classList.contains('collapsed'))
            return;

        if (!isExpanded) {
            // show avatar
            this.header.classList.add('expanded-header');
            this.blazorRef.invokeMethodAsync('FullSizeAvatarHandler', true);
        } else {
            this.closeFullScreenAvatar();
        }
    };

    private closeFullScreenAvatar = () => {
        if (!this.header.classList.contains('expanded-header'))
            return;

        this.header.addEventListener('transitionend', this.onTransitionEndBound);
        this.header.classList.add('collapsed-header');
        this.header.classList.remove('expanded-header');
        // Fallback: if the .c-center height transition never completes — e.g. it's killed when the list
        // is scrolled right after closing (the scroll-collapse suppresses transitions) — collapsed-header
        // would stick and freeze the avatar at full size with the buttons shown. Finish on a timer too.
        clearTimeout(this.closeFallbackTimer);
        this.closeFallbackTimer = window.setTimeout(() => this.finishClose(), 400);
    };

    private onTransitionEndBound = (e: TransitionEvent) => this.onTransitionEnd(e);
    private onTransitionEnd(e: TransitionEvent) {
        if (e.target !== this.centerDiv)
            return;

        if (e.propertyName !== 'height')
            return;

        this.finishClose();
    }

    private finishClose() {
        clearTimeout(this.closeFallbackTimer);
        this.header.removeEventListener('transitionend', this.onTransitionEndBound);

        if (this.header.classList.contains('collapsed-header')) {
            this.header.classList.remove('collapsed-header');
            void this.blazorRef.invokeMethodAsync('FullSizeAvatarHandler', false);
        }
    }

    private async clearHeader() : Promise<void> {
        this.header.classList.remove('expanded-header', 'collapsed-header');
        // Chat switched: let the collapse component re-baseline and re-measure the header for the new chat.
        this.varTarget.dispatchEvent(new CustomEvent('right-panel:chat-changed'));
    }
}
