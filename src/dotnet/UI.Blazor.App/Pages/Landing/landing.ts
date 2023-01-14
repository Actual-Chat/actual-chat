import { debounceTime, fromEvent, Subject, takeUntil } from 'rxjs';
import { Log, LogLevel } from 'logging';
import { endEvent } from '../../../../nodejs/src/event-handling';
import { ScreenSize } from '../../../UI.Blazor/Services/ScreenSize/screen-size';
import { Timeout } from '../../../../nodejs/src/timeout';
import { clamp } from '../../../UI.Blazor/Components/VirtualList/ts/math';

const LogScope = 'Landing';
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
    private readonly links = new Array<HTMLElement>();
    private readonly pages = new Array<HTMLElement>();
    private lastPage0Top = 0;
    private lastScrollDirection = 0;
    private scrollCheckTimeout?: Timeout;
    private scrollAlignTimeout?: Timeout;

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

        fromEvent(document, 'click')
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => this.onClick())

        fromEvent(document.defaultView, 'scroll', { passive: true, capture: true })
            .pipe(
                takeUntil(this.disposed$),
                debounceTime(100),
            ).subscribe(() => this.onScroll());
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

    private getScrollAlignmentPage(): HTMLElement | null {
        for (let i = 0; i < this.pages.length; i++) {
            const page = this.pages[i];
            const pageRect = page.getBoundingClientRect();
            const pageBreak = pageRect.bottom;
            if (pageBreak > 0) {
                if (Math.abs(pageRect.top) < 1)
                    return null; // < 1 means we're already aligned

                const nextPageOffset = this.lastScrollDirection > 0 ? 1 : 0;
                const nextPageIndex = clamp(i + nextPageOffset, 0, this.pages.length - 1);
                debugLog?.log(`getScrollAlignmentPage: nextPageIndex = ${i} + ${nextPageOffset}`);
                return this.pages[nextPageIndex];
            }
        }
        // Kinda impossible, but since we don't know how to align this...
        return null;
    }

    // Event handlers

    private onScroll(): void {
        if (ScreenSize.isNarrow())
            return; // Don't align on mobile

        this.scrollCheckTimeout?.clear();
        this.scrollAlignTimeout?.clear();
        this.updateHeader();

        const page0Top = this.pages[0].getBoundingClientRect().top;
        const dPage0Top = page0Top - this.lastPage0Top;
        this.lastPage0Top = page0Top;
        debugLog?.log("scrollToPage: dPage0Top:", dPage0Top);
        if (Math.abs(dPage0Top) > 1) {
            // We're still scrolling, but since the dPage0Top may be < 1 even on scroll stop,
            // we need to schedule a check that scroll really ended.
            this.lastScrollDirection = -Math.sign(dPage0Top);
            this.scrollCheckTimeout = new Timeout(200, () => this.onScroll());
            return;
        }

        this.scrollAlignTimeout = new Timeout(50, () => {
            const page = this.getScrollAlignmentPage();
            if (page != null) {
                debugLog?.log(`scrollToPage: scrolling to page`, page)
                page.scrollIntoView({ behavior: 'smooth', block: ScrollBlock.start })
            }
        })
    }

    private onClick() {
        if (!this.menu.classList.contains('open'))
            return;

        const withinMenu = event.composedPath().includes(this.menu);
        if (!withinMenu)
            return;

        this.blazorRef.invokeMethodAsync('CloseMenu');
        endEvent(event);
    };
}

// Helpers

function round(value: number): number {
    return Math.round(value);
}
