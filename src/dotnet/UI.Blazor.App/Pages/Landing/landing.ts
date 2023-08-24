import { clamp } from 'math';
import { debounceTime, fromEvent, Subject, takeUntil } from 'rxjs';
import { DeviceInfo } from 'device-info';
import { hasModifierKey } from 'keyboard';
import { preventDefaultForEvent, stopEvent } from 'event-handling';
import { Timeout } from 'timeout';
import { ScreenSize } from '../../../UI.Blazor/Services/ScreenSize/screen-size';

import { Log } from 'logging';

const { debugLog } = Log.get('Landing');

enum ScrollBlock {
    start = 'start',
    end = 'end',
}

class Carousel {
    carousel: Element;
    order: number;
    currentSlideOrder: number;
    prevSlideOrder: number;
    nextSlideOrder: number;
    sideLeft: Element;
    sideRight: Element;
    arrowLeft: Element;
    arrowRight: Element;
    dots: NodeListOf<Element>;

    constructor(
        carousel: Element,
        order: number,
        currentSlideOrder: number,
        prevSlideOrder: number,
        nextSlideOrder: number,
        sideLeft: Element,
        sideRight: Element,
        arrowLeft: Element,
        arrowRight: Element,
        dots: NodeListOf<Element>) {
        this.carousel = carousel;
        this.order = order;
        this.currentSlideOrder = currentSlideOrder;
        this.prevSlideOrder = prevSlideOrder;
        this.nextSlideOrder = nextSlideOrder;
        this.sideLeft = sideLeft;
        this.sideRight = sideRight;
        this.arrowLeft = arrowLeft;
        this.arrowRight = arrowRight;
        this.dots = dots;
    }
}

export class Landing {
    private readonly disposed$ = new Subject<void>();
    private readonly header: HTMLElement;
    private readonly scrollContainer: HTMLElement;
    private readonly links = new Array<HTMLElement>();
    private readonly pages = new Array<HTMLElement>();
    private readonly downloadLinksPage: HTMLElement;
    private currentPage: HTMLElement;
    private lastPage0Top = 0;
    private isAutoScrolling = false;
    private finalScrollCheckTimeout?: Timeout;

    static create(landing: HTMLElement): Landing {
        return new Landing(landing);
    }

    constructor(
        private readonly landing: HTMLElement,
    ) {
        this.header = landing.querySelector('.landing-header');
        landing.querySelectorAll('.landing-links').forEach(e => this.links.push(e as HTMLElement));
        landing.querySelectorAll('.scrollable').forEach(e => this.pages.push(e as HTMLElement));

        this.scrollContainer = getScrollContainer(this.pages[0]);
        this.currentPage = this.pages[0];

        this.onScreenSizeChange();
        ScreenSize.event$
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => this.onScreenSizeChange());

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

        this.downloadLinksPage = this.landing.querySelector('.page-links');
        let downloadAppButtons = this.landing.querySelectorAll('.download-app');
        let toMainPageButtons = this.landing.querySelectorAll('.btn-to-main-page');

        fromEvent(toMainPageButtons, 'pointerdown')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: PointerEvent) => this.onToMainPageButtonClick(event));

        fromEvent(downloadAppButtons, 'pointerdown')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: PointerEvent) => this.onDownloadButtonClick(event));

        const plug = this.landing.querySelector('.landing-video-plug') as HTMLImageElement;
        const video = this.landing.querySelector('.landing-video') as HTMLVideoElement;
        if (video != null) {
            video.play().then(() => {
                plug.classList.remove('flex');
                plug.hidden = true;
                video.hidden = false;
            });
        }

        this.initCarousels();
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private initCarousels() {
        const carousels = this.landing.querySelectorAll('.carousel');
        carousels.forEach(c => {
            let id = c.getAttribute('id');
            let carouselOrder = Number(id.split('-')[1]);
            let currentSlideOrder = 1;
            let sideLeft = c.querySelector('.c-side-left');
            let sideRight = c.querySelector('.c-side-right');
            let arrowLeft = sideLeft.querySelector('.arrow');
            let arrowRight = sideRight.querySelector('.arrow');
            let dots = c.querySelectorAll('.carousel-dot');
            dots[0].classList.add('active');
            let carousel = new Carousel(
                c,
                carouselOrder,
                currentSlideOrder,
                currentSlideOrder - 1,
                currentSlideOrder + 1,
                sideLeft,
                sideRight,
                arrowLeft,
                arrowRight,
                dots);

            this.onDotClick(carousel);
            this.updateControls(carousel);
            let carouselContent = c.querySelector('.carousel-content');

            fromEvent(carousel.sideRight, 'click')
                .pipe(
                    takeUntil(this.disposed$),
                    debounceTime(500),
                ).subscribe(() => this.onArrowClick(carousel, false));

            fromEvent(carousel.sideLeft, 'click')
                .pipe(
                    takeUntil(this.disposed$),
                    debounceTime(500),
                ).subscribe(() => this.onArrowClick(carousel, true));

            fromEvent(carouselContent, 'scroll')
                .pipe(takeUntil(this.disposed$))
                .subscribe(() => this.updateControls(carousel));
        })
    }

    private onDotClick(carousel: Carousel) {
        const content = carousel.carousel.querySelector('.carousel-content');
        carousel.dots.forEach(dot => {
            let slideId = dot.getAttribute('id').replace('dot-', '');
            let slide = carousel.carousel.querySelector(`#${slideId}`)
            if (slide != null) {
                let slideLeft = slide.getBoundingClientRect().left;
                const options = {
                    behavior: 'smooth',
                    left: slideLeft,
                } as ScrollToOptions;
                fromEvent(dot, 'click')
                    .pipe(takeUntil(this.disposed$))
                    .subscribe(() => content.scrollTo(options));
            }
        })
    }

    private updateControls(carousel: Carousel) {
        this.getCurrentSlide(carousel);
        let currentDotId = `dot-slide-${carousel.order}-${carousel.currentSlideOrder}`;
        carousel.dots.forEach(d => {
            let dotId = d.getAttribute('id');
            if (dotId == currentDotId) {
                if (!d.classList.contains('active')) {
                    d.classList.add('active');
                }
            } else {
                d.classList.remove('active');
            }
        });

        if (carousel.currentSlideOrder == 1 && !carousel.arrowLeft.classList.contains('!hidden')) {
            carousel.arrowLeft.classList.add('!hidden');
            carousel.sideLeft.classList.add('cursor-default');
        } else {
            carousel.arrowLeft.classList.remove('!hidden');
            carousel.sideLeft.classList.remove('cursor-default');
        }
        if (carousel.currentSlideOrder == carousel.dots.length) {
            carousel.arrowRight.classList.add('!hidden');
            carousel.sideRight.classList.add('cursor-default');
        } else {
            carousel.arrowRight.classList.remove('!hidden');
            carousel.sideRight.classList.remove('cursor-default');
        }
    }

    private getCurrentSlide(carousel: Carousel) {
        let content = carousel.carousel.querySelector('.carousel-content');
        let slides = content.querySelectorAll('.carousel-page');
        slides.forEach(s => {
            let rect = s.getBoundingClientRect();
            if (rect.left >= 0 && rect.left < content.getBoundingClientRect().right) {
                let currentSlideOrder = Number(s.getAttribute('id').split('-')[2]);
                carousel.currentSlideOrder = currentSlideOrder;
                carousel.prevSlideOrder = currentSlideOrder - 1;
                carousel.nextSlideOrder = currentSlideOrder + 1;
            }
        });
    }

    private onArrowClick(carousel: Carousel, previous: boolean) {
        let slideId = previous
                          ? `slide-${carousel.order}-${carousel.prevSlideOrder}`
                          : `slide-${carousel.order}-${carousel.nextSlideOrder}`;
        let multiplier = previous ? carousel.prevSlideOrder: carousel.nextSlideOrder;

        let next = carousel.carousel.querySelector(`#${slideId}`);
        let content = carousel.carousel.querySelector('.carousel-content');
        if (next != null) {
            let nextLeft = next.getBoundingClientRect().left;
            const options = {
                behavior: 'smooth',
                left: Math.abs(nextLeft) * (multiplier - 1),
            } as ScrollToOptions;
            content.scrollTo(options);
        }
    }

    private updateHeader(): void {
        const page0 = this.pages[0] as HTMLElement;
        const downloadPage = this.downloadLinksPage;
        let condition1 = page0.getBoundingClientRect().bottom <= 0;
        let condition2 = downloadPage.classList.contains('hidden');
        let condition3 = Math.round(downloadPage.getBoundingClientRect().top) > 0;
        if (condition1 && ((!condition2 && condition3) || condition2)) {
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

        let downloadBtn = this.header.querySelector('.download-app');
        let mainPageBtn = this.header.querySelector('.btn-to-main-page');
        if (downloadBtn == null || mainPageBtn == null)
            return;
        if (!condition2 && !condition3) {
            downloadBtn.classList.add('!hidden');
            mainPageBtn.classList.remove('!hidden');
        } else {
            downloadBtn.classList.remove('!hidden');
            mainPageBtn.classList.add('!hidden');
        }
    }

    private autoScroll(isScrollDown: boolean, event?: Event, isScrolling = false) {
        let condition1 = this.downloadLinksPage.classList.contains('hidden');
        let condition2 = Math.round(this.downloadLinksPage.getBoundingClientRect().bottom) == window.innerHeight;

        if (DeviceInfo.isIos) {
            if (!condition1 && condition2) {
                preventDefaultForEvent(event);
            }
            return; // The auto-scroll doesn't work on iOS devices (yet)
        }

        if (!condition1 && condition2) {
            preventDefaultForEvent(event);
            return;
        }

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

        let pageHeight = Math.round(page.getBoundingClientRect().height);
        let nextPageHeight = Math.round(nextPage.getBoundingClientRect().height);
        if (pageHeight != window.innerHeight || nextPageHeight != window.innerHeight)
            return;

        debugLog?.log(`autoScroll: starting`);
        stopEvent(event);
        this.isAutoScrolling = true;
        scrollWithOffset(nextPage, this.scrollContainer, 0);
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
        const pageIndex = this.pages.indexOf(page);
        const nextPageOffset = isScrollDown ? 1 : isScrolling ? 0 : -1;
        const nextPageIndex = clamp(pageIndex + nextPageOffset, 0, this.pages.length - 1);
        debugLog?.log(`getNextPage: -> ${pageIndex} + ${nextPageOffset}`);
        return this.pages[nextPageIndex];
    }

    // Event handlers

    private onScreenSizeChange() {
        const h = window.innerHeight;
        const w = window.innerWidth;
        const hwRatio = h / w;
        let useFullScreenPages = ScreenSize.isNarrow() ? (hwRatio >= 1.8 && hwRatio <= 2.5) : (h >= 700);
        if (useFullScreenPages)
            this.landing.classList.remove('no-full-screen-pages');
        else
            this.landing.classList.add('no-full-screen-pages');
    }

    private onKeyDown(event: KeyboardEvent): void {
        if (hasModifierKey(event))
            return;
        if (event.key == "ArrowDown" || event.key == "PageDown")
            return this.autoScroll(true, event);
        if (event.key == "ArrowUp" || event.key == "PageUp")
            return this.autoScroll(false, event);
        if (event.key == "Escape" && Math.round(this.downloadLinksPage.getBoundingClientRect().top) <= 0) {
            let top = Math.round(this.currentPage.getBoundingClientRect().top);
            let landingTop = this.landing.getBoundingClientRect().top;
            const options = {
                behavior: 'auto',
                top: (top - landingTop),
            } as ScrollToOptions;
            this.scrollContainer.scrollTo(options);
        }
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
            debugLog?.log(`onScroll: scroll stopped`);
            this.isAutoScrolling = false;
            this.finalScrollCheckTimeout?.clear();
            this.finalScrollCheckTimeout = null;
            return;
        }
        if (this.isAutoScrolling) {
            if (!isFinalCheck) {
                // The very last scroll event may still report some dScrollTop, so...
                debugLog?.log(`onScroll: scheduling final check`);
                this.finalScrollCheckTimeout = new Timeout(100, () => this.onScroll(true));
            }
            // Still auto-scrolling
            return;
        }

        this.autoScroll(dPage0Top < 0, null, true);
    }

    private onDownloadButtonClick(event: PointerEvent) : void {
        this.downloadLinksToggle();
        this.currentPage = this.getCurrentPage();
        let top = this.downloadLinksPage.getBoundingClientRect().top;
        let landingTop = this.landing.getBoundingClientRect().top;
        const options = {
            behavior: 'auto',
            top: (top - landingTop),
        } as ScrollToOptions;
        this.scrollContainer.scrollTo(options);
    }

    private onToMainPageButtonClick(event: PointerEvent) : void {
        this.downloadLinksToggle();
        let top = Math.round(this.currentPage.getBoundingClientRect().top);
        let landingTop = this.landing.getBoundingClientRect().top;
        const options = {
            behavior: 'auto',
            top: (top - landingTop),
        } as ScrollToOptions;
        this.scrollContainer.scrollTo(options);
    }

    private downloadLinksToggle() {
        let page = this.downloadLinksPage;
        if (page.classList.contains('hidden')) {
            page.classList.remove('hidden');
        } else {
            page.classList.add('hidden');
        }
    }
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
