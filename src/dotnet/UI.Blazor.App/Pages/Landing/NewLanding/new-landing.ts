import { fromEvent, Subject, takeUntil } from 'rxjs';
import { ScreenSize } from '../../../../UI.Blazor/Services/ScreenSize/screen-size';

import { Log } from 'logging';
import { hasModifierKey } from 'keyboard';
import { preventDefaultForEvent } from 'event-handling';

const { debugLog } = Log.get('Landing');

export class NewLanding {
    private readonly disposed$ = new Subject<void>();
    private readonly header: HTMLElement;
    private readonly downloadLinksPage: HTMLElement;
    private readonly scrollContainer: HTMLElement;
    private lastPosition: number = 0;

    static create(landing: HTMLElement): NewLanding {
        return new NewLanding(landing);
    }

    constructor(
        private readonly landing: HTMLElement,
    ) {
        this.header = landing.querySelector('.landing-header');
        this.onScreenSizeChange();
        ScreenSize.event$
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => this.onScreenSizeChange());

        fromEvent(document, 'keydown')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: KeyboardEvent) => this.onKeyDown(event));

        fromEvent(document, 'touchmove', { passive: false })
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: TouchEvent) => this.onTouch(event));

        fromEvent(document, 'wheel', { passive: false }) // WheelEvent is passive by default
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: WheelEvent) => this.onWheel(event));

        fromEvent(document, 'scroll', { capture: true, passive: true })
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => this.onScroll());

        const plug = this.landing.querySelector('.landing-video-plug') as HTMLImageElement;
        const video = this.landing.querySelector('.landing-video') as HTMLVideoElement;
        if (video != null) {
            video.play().then(() => {
                plug.classList.remove('flex');
                plug.hidden = true;
                video.hidden = false;
            });
        }

        this.downloadLinksPage = this.landing.querySelector('.page-links');
        this.scrollContainer = getScrollContainer(this.downloadLinksPage);

        let vh = window.innerHeight * 0.01;
        document.documentElement.style.setProperty('--vh', `${vh}px`);
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
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
        let vh = window.innerHeight * 0.01;
        document.documentElement.style.setProperty('--vh', `${vh}px`);
    }

    private onKeyDown(event: KeyboardEvent): void {
        if (hasModifierKey(event))
            return;
        let canScroll = false;
        if (event.key == "ArrowDown" || event.key == "PageDown" || event.key == "ArrowUp" || event.key == "PageUp")
            canScroll = true;
        if (!canScroll)
            return;

        const linksPage = this.landing.querySelector('.page-links');
        let isNotLinksPage = linksPage.classList.contains('hidden')
            || linksPage.getBoundingClientRect().top <= 0;

        if (!isNotLinksPage) {
            preventDefaultForEvent(event);
            return;
        }
    }

    private onWheel(event: WheelEvent): void {
        if (hasModifierKey(event))
            return;
        const linksPage = this.landing.querySelector('.page-links');
        let isNotLinksPage = linksPage.classList.contains('hidden')
            || linksPage.getBoundingClientRect().top <= 0;

        if (!isNotLinksPage) {
            preventDefaultForEvent(event);
            return;
        }
    }

    private onTouch(event: TouchEvent): void {
        const linksPage = this.landing.querySelector('.page-links');
        let isNotLinksPage = linksPage.classList.contains('hidden')
            || linksPage.getBoundingClientRect().top <= 0;

        if (!isNotLinksPage) {
            preventDefaultForEvent(event);
            return;
        }
    }

    private onScroll(): void {
        this.updateHeader();
    }

    private updateHeader(): void {
        const page1 = this.landing.querySelector('.page-1');
        const linksPage = this.landing.querySelector('.page-links');
        let isNotFirstPage = page1.getBoundingClientRect().bottom <= 0;
        let isNotLinksPage = linksPage.classList.contains('hidden')
            || linksPage.getBoundingClientRect().top <= 0;

        if (isNotFirstPage && isNotLinksPage) {
            this.header.classList.add('filled');
        } else {
            this.header.classList.remove('filled');
        }

        let downloadBtn = this.header.querySelector('.download-app');
        let mainPageBtn = this.header.querySelector('.btn-to-main-page');
        if (this == null || mainPageBtn == null)
            return;
        if (!linksPage.classList.contains('hidden') && !isNotLinksPage) {
            downloadBtn.classList.add('!hidden');
            mainPageBtn.classList.remove('!hidden');
        } else {
            downloadBtn.classList.remove('!hidden');
            mainPageBtn.classList.add('!hidden');
        }
    }

    private scrollToPageLinks() {
        this.downloadLinksToggle();
        let landingTop = this.landing.getBoundingClientRect().top;
        this.lastPosition = landingTop;
        let top = this.downloadLinksPage.getBoundingClientRect().top;
        const options = {
            behavior: 'auto',
            top: (top - landingTop),
        } as ScrollToOptions;
        this.scrollContainer.scrollTo(options);
    }

    private scrollFromPageLinks() : void {
        this.downloadLinksToggle();
        let top = -(this.lastPosition);
        const options = {
            behavior: 'auto',
            top: top,
        } as ScrollToOptions;
        this.scrollContainer.scrollTo(options);
    }

    private scrollToWhyUs() : void {
        let rect = this.landing.querySelector('.page-4').getBoundingClientRect();
        let top = rect.top;
        let landingTop = this.landing.getBoundingClientRect().top;
        const options = {
            behavior: 'smooth',
            top: top - landingTop,
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

    private showLinksPage(): void {
        const linksPage = this.landing.querySelector('.page-links');
        let isNotLinksPage = linksPage.classList.contains('hidden')
            || linksPage.getBoundingClientRect().top <= 0;
        if (isNotLinksPage)
            this.scrollToPageLinks();
        else
            return;
    }
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
