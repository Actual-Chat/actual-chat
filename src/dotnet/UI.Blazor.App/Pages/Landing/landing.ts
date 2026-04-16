// TODO: remove eslint-disables and fix errors
/* eslint-disable @typescript-eslint/no-unsafe-call,@typescript-eslint/no-unsafe-member-access,@typescript-eslint/no-unnecessary-condition */
import { fromEvent, Subject, takeUntil } from 'rxjs';
import { ScreenSize } from '../../../UI.Blazor/Services/ScreenSize/screen-size';

import { getLogs } from 'logging';
import { hasModifierKey } from 'keyboard';
import { preventDefaultForEvent } from 'event-handling';

const { debugLog } = getLogs('Landing');

export class Landing {
    private readonly disposed$ = new Subject<void>();
    private readonly header: HTMLElement | null;
    private readonly firstPage: HTMLElement | null;
    private readonly lastPage: HTMLElement | null;
    private readonly downloadLinksPage: HTMLElement | null;
    private readonly scrollContainer: HTMLElement | null;
    private lastPosition = 0;
    private vh = 0;
    private isVideoPlayStarted = false;
    private observer: IntersectionObserver;
    private canScroll = true;

    static create(landing: HTMLElement): Landing {
        return new Landing(landing);
    }

    constructor(
        private readonly landing: HTMLElement,
    ) {
        this.disableAutoDarkMode();
        this.header = landing.querySelector('.landing-header');
        if (!this.header)
            return;

        this.firstPage = landing.querySelector('.page-1');
        this.lastPage = landing.querySelector('.page-last');
        this.vh = window.innerHeight * 0.01;
        document.documentElement.style.setProperty('--vh', `${this.vh}px`);

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
            .subscribe(() => this.onTouchEnd());

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

        const cardVideoPlugs = [...this.landing.querySelectorAll<HTMLImageElement>('.landing-card-video-plug')];
        const options = {
            root: null,
            rootMargin: '0px',
            threshold: 1.0,
        };
        this.observer = new IntersectionObserver(this.cardVideoHandler, options);
        cardVideoPlugs.forEach(item => {
            this.observer.observe(item);
        });

        this.downloadLinksPage = this.landing.querySelector('.page-links');
        if (this.downloadLinksPage)
            this.scrollContainer = getScrollContainer(this.downloadLinksPage);

        // blur is always applied via CSS — no need for IntersectionObserver toggle
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
        const colorScheme = document.querySelector("[name='color-scheme']");
        if (colorScheme)
            colorScheme.remove();
        this.observer.disconnect();
    }

    // Event handlers

    private onScreenSizeChange() {
        const h = window.innerHeight;
        const w = window.innerWidth;
        const hwRatio = h / w;
        const useFullScreenPages = ScreenSize.isNarrow() ? (hwRatio >= 1.8 && hwRatio <= 2.5) : (h >= 700);
        if (useFullScreenPages)
            this.landing.classList.remove('no-full-screen-pages');
        else
            this.landing.classList.add('no-full-screen-pages');
        this.vh = window.innerHeight * 0.01;
        document.documentElement.style.setProperty('--vh', `${this.vh}px`);
    }

    private onKeyDown(event: KeyboardEvent): void {
        if (hasModifierKey(event))
            return;
        let canScroll = false;
        if (event.key == 'ArrowDown' || event.key == 'PageDown' || event.key == 'ArrowUp' || event.key == 'PageUp')
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

    private onTouchEnd(): void {
        if (this.isVideoPlayStarted)
            return;

        const cardVideos = [...this.landing.querySelectorAll<HTMLVideoElement>('.landing-card-video')];
        for (const cardVideo of cardVideos) {
            cardVideo.muted = true;
            const isVideoPlaying = (cardVideo.currentTime > 0 && !cardVideo.paused && !cardVideo.ended && cardVideo.readyState > 2);
            if (!isVideoPlaying) {
                this.isVideoPlayStarted = true;
                addVideoSources(cardVideo);
                cardVideo.load();
                cardVideo.oncanplay = () => {
                    void cardVideo.play().then(() => {
                        debugLog?.log('onTouchEnd: card video playback started.');
                        cardVideo.hidden = false;
                    });
                };
                debugLog?.log('onTouchEnd: card video play...');
            }
        }
    }

    private onScroll(): void {
        this.updateHeader();
    }

    private updateHeader(): void {
        if (this.header == null)
            return;

        const isNotFirstPage = this.firstPage && this.firstPage.getBoundingClientRect().bottom <= 0;
        const isNotLastPage = this.lastPage && Math.trunc(this.lastPage.getBoundingClientRect().top) > 0;

        if ((isNotFirstPage && isNotLastPage && this.canScroll)) {
            this.header.classList.add('filled');
        } else {
            this.header.classList.remove('filled');
        }

        const downloadBtn = this.header.querySelector('.download-app');
        const mainPageBtn = this.header.querySelector('.btn-to-main-page');
        if (!mainPageBtn || !downloadBtn)
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
        const landingTop = this.landing.getBoundingClientRect().top;
        this.lastPosition = landingTop;
        if (!this.downloadLinksPage || !this.scrollContainer)
            return;

        const top = this.downloadLinksPage.getBoundingClientRect().top;
        const options = {
            behavior: 'auto',
            top: (top - landingTop),
        } as ScrollToOptions;
        this.scrollContainer.scrollTo(options);
    }

    private scrollFromPageLinks(): void {
        if (!this.scrollContainer)
            return;

        this.downloadLinksToggle();
        const top = -(this.lastPosition);
        const options = {
            behavior: 'auto',
            top: top,
        } as ScrollToOptions;
        this.scrollContainer.scrollTo(options);
    }

    private scrollToWhyUs(): void {
        const page4 = this.landing.querySelector('.page-4');
        if (!page4 || !this.scrollContainer)
            return;

        const rect = page4.getBoundingClientRect();
        const top = rect.top;
        const landingTop = this.landing.getBoundingClientRect().top;
        const options = {
            behavior: 'smooth',
            top: top - landingTop,
        } as ScrollToOptions;
        this.scrollContainer.scrollTo(options);
    }

    private downloadLinksToggle(): void {
        const page = this.downloadLinksPage;
        if (!page)
            return;

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
        meta.name = 'color-scheme';
        meta.content = 'only light';
        document.getElementsByTagName('head')[0].appendChild(meta);
    }

    private onHoverPlugClick = () => {
        const video = this.landing.querySelector<HTMLVideoElement>('.landing-video');
        const plug = this.landing.querySelector<HTMLImageElement>('.landing-video-plug');
        const hoverPlug = this.landing.querySelector('.hover-plug');
        if (!video || !plug || !hoverPlug)
            return;

        addVideoSources(video);
        video.load();
        video.oncanplay = () => {
            void video.play().then(() => {
                plug.classList.remove('flex');
                plug.hidden = true;
                hoverPlug.remove();
                video.hidden = false;
            });
        };
    }

    private cardVideoHandler = (entries) => {
        entries.forEach(entry => {
            const plug = entry.target as HTMLImageElement;
            if (!plug || plug.hidden)
                return;
            const cardVideo = plug.parentElement?.querySelector<HTMLVideoElement>('.landing-card-video');
            if (!cardVideo || cardVideo.classList.contains('loaded'))
                return;
            if (entry.isIntersecting) {
                addVideoSources(cardVideo);
                cardVideo.load();
                cardVideo.muted = true;
                cardVideo.oncanplay = () => {
                    void cardVideo.play();
                    cardVideo.classList.add('loaded');
                    cardVideo.hidden = false;
                }
            }
        })
    }
}

function addVideoSources(video: HTMLVideoElement): void {
    // Skip if sources already added
    if (video.querySelector('source'))
        return;

    const webmSrc = video.dataset.srcWebm;
    const mp4Src = video.dataset.srcMp4;

    if (webmSrc) {
        const source = document.createElement('source');
        source.src = webmSrc;
        source.type = 'video/webm';
        video.appendChild(source);
    }
    if (mp4Src) {
        const source = document.createElement('source');
        source.src = mp4Src;
        source.type = 'video/mp4';
        video.appendChild(source);
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
