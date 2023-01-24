import { debounceTime, filter, fromEvent, map, merge, Subject, takeUntil } from 'rxjs';
import { clamp } from 'math';
import { hasModifierKey } from 'keyboard';
import { endEvent } from 'event-handling';
import { Timeout } from 'timeout';
import { ScreenSize } from '../../../UI.Blazor/Services/ScreenSize/screen-size';

import { Log, LogLevel, LogScope } from 'logging';
const LogScope: LogScope = 'Landing';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

enum ScrollBlock {
    start = 'start',
    end = 'end',
}

export class Landing {
    private readonly disposed$ = new Subject<void>();
    private readonly menu: HTMLElement;
    private readonly header: HTMLElement;
    private readonly scrollContainer: HTMLElement;
    private readonly links = new Array<HTMLElement>();
    private readonly pages = new Array<HTMLElement>();
    private lastPage0Top = 0;
    private isAutoScrolling = false;
    private finalScrollCheckTimeout?: Timeout;

    static create(landing: HTMLElement, blazorRef: DotNet.DotNetObject): Landing {
        return new Landing(landing, blazorRef);
    }

    constructor(
        private readonly landing: HTMLElement,
        private readonly blazorRef: DotNet.DotNetObject,
    ) {
        this.header = landing.querySelector('.landing-header');
        this.menu = landing.querySelector('.landing-menu');
        landing.querySelectorAll('.landing-links').forEach(e => this.links.push(e as HTMLElement));
        landing.querySelectorAll('.page').forEach(e => this.pages.push(e as HTMLElement));
        this.scrollContainer = getScrollContainer(this.pages[0]);

        fromEvent(document, 'click')
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => this.onClick())

        fromEvent(document, 'keydown')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: KeyboardEvent) => this.onKeyDown(event));

        fromEvent(document, 'wheel', { passive: false }) // WheelEvent is passive by default
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: WheelEvent) => this.onWheel(event));

        // Scroll event bubbles only to document.defaultView
        fromEvent(this.scrollContainer, 'scroll', { capture: true, passive: true })
            .pipe(
                takeUntil(this.disposed$),
                debounceTime(100),
            ).subscribe(() => this.onScroll(false));
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private updateHeader(): void {
        const page0 = this.pages[0] as HTMLElement;
        if (page0.getBoundingClientRect().bottom <= 0) {
            this.header.classList.add('filled');
        } else {
            this.header.classList.remove('filled');
        }

        if (this.links.length == 0)
            return;

        const headerRect = this.header.getBoundingClientRect();
        this.links.forEach(link => {
            let linkRect = link.getBoundingClientRect();
            if (linkRect.top <= headerRect.top && linkRect.bottom >= headerRect.bottom) {
                // Link covers the header
                this.header.classList.add('hide-header');
            }
        });
        // There are no links covering the header
        this.header.classList.remove('hide-header');
    }

    private autoScroll(isScrollDown: boolean, event?: Event, isScrolling = false) {
        const page = this.getCurrentPage();
        if (page == null)
            return;

        if (isScrolling) {
            const headerBottom = this.header.getBoundingClientRect().bottom;
            const pageRect = page.getBoundingClientRect();
            if (isScrolling && Math.abs(pageRect.top - headerBottom) < 0.1)
                return; // < 1 means we're already aligned
        }

        const nextPage = this.getNextPage(page, isScrollDown, isScrolling);
        if (nextPage == null)
            return;

        debugLog?.log(`autoScroll: starting`);
        endEvent(event);
        this.isAutoScrolling = true;
        scrollWithOffset(nextPage, this.scrollContainer, this.header.getBoundingClientRect().height);
    }

    private getCurrentPage(): HTMLElement | null {
        const headerBottom = this.header.getBoundingClientRect().bottom;
        for (let i = 0; i < this.pages.length; i++) {
            const page = this.pages[i];
            const pageRect = page.getBoundingClientRect();
            const pageEnd = pageRect.bottom - 1;
            if (pageEnd > headerBottom)
                return this.pages[i];
        }
        return null;
    }

    private getNextPage(page: HTMLElement, isScrollDown: boolean, isScrolling = false): HTMLElement | null {
        if (page.classList.contains('no-auto-scroll'))
            return null;

        const pageIndex = this.pages.indexOf(page);
        const nextPageOffset = isScrollDown ? 1 : isScrolling ? 0 : -1;
        const nextPageIndex = clamp(pageIndex + nextPageOffset, 0, this.pages.length - 1);
        debugLog?.log(`getNextPage: -> ${pageIndex} + ${nextPageOffset}`);
        return this.pages[nextPageIndex];
    }

    // Event handlers

    private onKeyDown(event: KeyboardEvent): void {
        if (hasModifierKey(event))
            return;
        if (event.key == "ArrowDown" || event.key == "PageDown")
            return this.autoScroll(true, event);
        if (event.key == "ArrowUp" || event.key == "PageUp")
            return this.autoScroll(false, event);
    }

    private onWheel(event: WheelEvent): void {
        if (hasModifierKey(event))
            return;
        if (event.deltaY > 0)
            return this.autoScroll(true, event);
        if (event.deltaY < 0)
            return this.autoScroll(false, event);
    }

    private onScroll(isFinalCheck: boolean): void {
        /*
        if (ScreenSize.isNarrow())
            return; // Don't align on mobile
         */

        this.finalScrollCheckTimeout?.clear();
        this.finalScrollCheckTimeout = null;

        this.updateHeader();
        const page0Top = this.pages[0].getBoundingClientRect().top;
        const dPage0Top = page0Top - this.lastPage0Top;
        this.lastPage0Top = page0Top;
        debugLog?.log(`onScroll(${isFinalCheck}): dPage0Top:`, dPage0Top);

        if (Math.abs(dPage0Top) < 0.1) {
            // The scroll is stopped
            debugLog?.log(`onScroll: scroll stopped`)
            this.isAutoScrolling = false;
            this.finalScrollCheckTimeout?.clear();
            this.finalScrollCheckTimeout = null;
            return;
        }
        if (this.isAutoScrolling) {
            if (!isFinalCheck) {
                // The very last scroll event may still report some dScrollTop, so...
                debugLog?.log(`onScroll: scheduling final check`)
                this.finalScrollCheckTimeout = new Timeout(100, () => this.onScroll(true));
            }
            // Still auto-scrolling
            return;
        }

        this.autoScroll(dPage0Top < 0, null, true);
    }

    private onClick() {
        if (!this.menu.classList.contains('open'))
            return;

        const container = this.menu.querySelector('.c-container');
        const withinMenu = event.composedPath().includes(container);
        if (withinMenu)
            return;

        this.blazorRef.invokeMethodAsync('CloseMenu');
        endEvent(event);
    };
}

// Helpers

function scrollWithOffset(
    element: HTMLElement,
    scrollingElement: HTMLElement,
    offset: number,
    behavior = 'smooth'
) {
    const dTop = element.getBoundingClientRect().top - offset - scrollingElement.getBoundingClientRect().top;
    const options = {
        behavior: behavior,
        top: scrollingElement.scrollTop + dTop,
    } as ScrollToOptions;
    scrollingElement.scrollTo(options)
}

function getScrollContainer(element: HTMLElement): HTMLElement | null {
    let parent = element.parentElement;
    while (parent) {
        if (parent.scrollHeight > parent.clientHeight) // = has vertical scroller
            return parent;
        parent = parent.parentElement;
    }
    return null;
}
