import { fromEvent, Subject, takeUntil } from 'rxjs';
import { ScreenSize } from '../../../UI.Blazor/Services/ScreenSize/screen-size';

import { Log } from 'logging';
import { hasModifierKey } from 'keyboard';
import { preventDefaultForEvent } from 'event-handling';

const { debugLog } = Log.get('Landing');

export class Landing {
    private readonly disposed$ = new Subject<void>();
    private readonly header: HTMLElement;
    private readonly downloadLinksPage: HTMLElement;
    private readonly scrollContainer: HTMLElement;
    private lastPosition: number = 0;
    private isVideoPlayStarted = false;
    private observer: IntersectionObserver;
    private canScroll: boolean = true;

    static create(landing: HTMLElement): Landing {
        return new Landing(landing);
    }

    constructor(
        private readonly landing: HTMLElement,
    ) {
        this.disableAutoDarkMode();
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

        fromEvent(document, 'touchend', { passive: false, once: true })
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: TouchEvent) => this.onTouchEnd(event));

        fromEvent(document, 'wheel', { passive: false }) // WheelEvent is passive by default
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: WheelEvent) => this.onWheel(event));

        fromEvent(document, 'scroll', { capture: true, passive: true })
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => this.onScroll());

        const hoverPlug = this.landing.querySelector('.hover-plug');
        if (hoverPlug) {
            fromEvent(hoverPlug, 'click')
                .pipe(takeUntil(this.disposed$))
                .subscribe(() => this.onHoverPlugClick());
        }

        const cardVideos = [...this.landing.querySelectorAll<HTMLVideoElement>('.landing-card-video')];
        const options = {
            root: null,
            rootMargin: "0px",
            threshold: 1.0,
        };
        this.observer = new IntersectionObserver(this.cardVideoHandler, options);
        cardVideos.forEach(item => {
            this.observer.observe(item);
        });

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
        document.querySelector("[name='color-scheme']").remove();
        this.observer.disconnect();
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
        if (!this.canScroll) {
            preventDefaultForEvent(event);
            return;
        }
    }

    private onWheel(event: WheelEvent): void {
        if (hasModifierKey(event))
            return;
        if (!this.canScroll) {
            preventDefaultForEvent(event);
            return;
        }
    }

    private onTouch(event: TouchEvent): void {
        if (!this.canScroll) {
            preventDefaultForEvent(event);
            return;
        }
    }

    private onTouchEnd(event: TouchEvent): void {
        if (this.isVideoPlayStarted)
            return;

        const cardVideos = [...this.landing.querySelectorAll<HTMLVideoElement>('.landing-card-video')];
        for (const cardVideo of cardVideos) {
            cardVideo.muted = true;
            const isVideoPlaying = !!(cardVideo.currentTime > 0 && !cardVideo.paused && !cardVideo.ended && cardVideo.readyState > 2);
            if (!isVideoPlaying) {
                this.isVideoPlayStarted = true;
                void cardVideo.play().then(_ => {
                    debugLog?.log('onTouchEnd: card video playback started.');
                });
                debugLog?.log('onTouchEnd: card video play...');
            }
        }
    }

    private onScroll(): void {
        this.updateHeader();
    }

    private updateHeader(): void {
        const page1 = this.landing.querySelector('.page-1');
        let isNotFirstPage = page1.getBoundingClientRect().bottom <= 0;

        if ((isNotFirstPage && this.canScroll)) {
            this.header.classList.add('filled');
        } else {
            this.header.classList.remove('filled');
        }

        let downloadBtn = this.header.querySelector('.download-app');
        let mainPageBtn = this.header.querySelector('.btn-to-main-page');
        if (this == null || mainPageBtn == null)
            return;
        if (!this.canScroll) {
            downloadBtn.classList.add('!hidden');
            mainPageBtn.classList.remove('!hidden');
        } else {
            downloadBtn.classList.remove('!hidden');
            mainPageBtn.classList.add('!hidden');
        }
    }

    private scrollToPageLinks(): void {
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

    private scrollFromPageLinks(): void {
        this.downloadLinksToggle();
        let top = -(this.lastPosition);
        const options = {
            behavior: 'auto',
            top: top,
        } as ScrollToOptions;
        this.scrollContainer.scrollTo(options);
    }

    private scrollToWhyUs(): void {
        let rect = this.landing.querySelector('.page-4').getBoundingClientRect();
        let top = rect.top;
        let landingTop = this.landing.getBoundingClientRect().top;
        const options = {
            behavior: 'smooth',
            top: top - landingTop,
        } as ScrollToOptions;
        this.scrollContainer.scrollTo(options);
    }

    private downloadLinksToggle(): void {
        let page = this.downloadLinksPage;
        if (page.classList.contains('hidden')) {
            page.classList.remove('hidden');
            this.canScroll = false;
        } else {
            page.classList.add('hidden');
            this.canScroll = true;
        }
    }

    private showLinksPage(): void {
        if (this.canScroll)
            this.scrollToPageLinks();
        else
            return;
    }

    private disableAutoDarkMode(): void {
        const meta = document.createElement('meta');
        meta.name = "color-scheme";
        meta.content = "only light";
        document.getElementsByTagName('head')[0].appendChild(meta);
    }

    private onHoverPlugClick = () => {
        const video = this.landing.querySelector<HTMLVideoElement>('.landing-video');
        const plug = this.landing.querySelector<HTMLImageElement>('.landing-video-plug');
        const hoverPlug = this.landing.querySelector('.hover-plug');

        video.load();
        video.oncanplay = _ => {
            video.play().then(() => {
                plug.classList.remove('flex');
                plug.hidden = true;
                hoverPlug.remove();
                video.hidden = false;
            });
        };
    }

    private cardVideoHandler = (entries, observer) => {
        entries.forEach(entry => {
            const cardVideo = entry.target as HTMLVideoElement;
            if (!cardVideo || cardVideo.classList.contains('loaded'))
                return;
            if (entry.isIntersecting) {
                if (cardVideo) {
                    cardVideo.load();
                    cardVideo.muted = true;
                    cardVideo.oncanplay = _ => {
                        void cardVideo.play();
                        cardVideo.classList.add('loaded');
                    }
                }
            }
        })
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
