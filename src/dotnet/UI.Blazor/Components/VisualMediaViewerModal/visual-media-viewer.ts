import { fromEvent, Subject, takeUntil } from 'rxjs';
import { setTimeout } from 'timerQueue';
import { Swiper } from 'swiper';

export class VisualMediaViewer {
    private readonly disposed$: Subject<void> = new Subject<void>();
    private readonly overlay: HTMLElement;
    private readonly header: HTMLElement;
    private readonly footer: HTMLElement | undefined;
    private isHeaderAndFooterVisible: boolean = true;
    private isHeaderAndFooterVisibilityForced: boolean = false;

    static create(imageViewer: HTMLElement, blazorRef: DotNet.DotNetObject): VisualMediaViewer {
        return new VisualMediaViewer(imageViewer, blazorRef);
    }

    constructor(
        private readonly imageViewer: HTMLElement,
        private readonly blazorRef: DotNet.DotNetObject
    ) {
        this.overlay = this.imageViewer.closest('.modal-overlay');
        this.header = this.overlay.querySelector('.image-viewer-header');
        this.footer = this.overlay.querySelector('.image-viewer-footer');

         fromEvent(this.overlay, 'click')
             .pipe(takeUntil(this.disposed$))
             .subscribe((event: PointerEvent) => this.onClick(event));

        fromEvent(this.overlay, 'swiperslidechange')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event) => this.onSlideChange(event));

        fromEvent(this.overlay, 'youtubeplayeronstatechange')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: CustomEvent<YT.OnStateChangeEvent>) => this.onYouTubePlayerStateChange(event));

        setTimeout(() => {
            if (!this.isHeaderAndFooterVisibilityForced) {
                this.hideHeaderAndFooter();
            }

            fromEvent(this.overlay, 'mousemove')
                .pipe(takeUntil(this.disposed$))
                .subscribe((event: MouseEvent) => this.onMouseMove(event));
        }, 3000);

        this.updateVideoPlayback();
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private showHeaderAndFooter() {
        if (this.isHeaderAndFooterVisible)
            return;

        this.isHeaderAndFooterVisible = true;
        this.header.classList.remove('show-to-hide');
        this.header.classList.add('hide-to-show');
        this.footer?.classList.remove('show-to-hide');
        this.footer?.classList.add('hide-to-show');
        this.imageViewer.classList.remove('navigation-hidden');
        this.imageViewer.classList.add('navigation-visible');
    }

    private hideHeaderAndFooter() {
        if (!this.isHeaderAndFooterVisible)
            return;

        this.isHeaderAndFooterVisible = false;
        this.header.classList.remove('hide-to-show');
        this.header.classList.add('show-to-hide');
        this.footer?.classList.remove('hide-to-show');
        this.footer?.classList.add('show-to-hide');
        this.imageViewer.classList.remove('navigation-visible');
        this.imageViewer.classList.add('navigation-hidden');
    }

    private toggleHeaderAndFooterVisibility() {
        this.isHeaderAndFooterVisibilityForced = true;
        if (this.isHeaderAndFooterVisible) {
            this.hideHeaderAndFooter();
        } else {
            this.showHeaderAndFooter();
        }
    }

    // Event handlers

    private onMouseMove(event: MouseEvent) {
         if (this.isHeaderAndFooterVisibilityForced)
             return;
        const { pageY, pageX } = event;
        const cursorInPrevButtonArea = pageX <= 40;
        const cursorInNextButtonArea = this.overlay.offsetWidth - pageX <= 40;
         const cursorInHeaderArea = pageY <= this.header.offsetHeight;
         const cursorInFooterArea = this.footer
             ? this.overlay.offsetHeight - pageY <= this.footer.offsetHeight
             : false;
         if (cursorInHeaderArea || cursorInFooterArea || cursorInPrevButtonArea || cursorInNextButtonArea) {
             this.showHeaderAndFooter();
         } else {
             this.hideHeaderAndFooter();
         }
    }

    private onClick(event: PointerEvent | MouseEvent) {
        const { pageY } = event;
        const cursorInHeaderArea = pageY <= this.header.offsetHeight;
        const cursorInFooterArea = this.footer
            ? this.overlay.offsetHeight - pageY <= this.footer.offsetHeight
            : false;
        if (this.isHeaderAndFooterVisible && (cursorInHeaderArea || cursorInFooterArea))
            return;

        const target = event.target as HTMLElement;
        if (target.classList.contains('media-swiper')) {
            // click on prev / next buttons
        } else if (target.classList.contains('swiper-zoom-container')) {
            // click outside image/video
            void this.blazorRef.invokeMethodAsync('Close');
        } else {
            // click on image/video
            this.toggleHeaderAndFooterVisibility();
        }
    }

    private onYouTubePlayerStateChange(event: CustomEvent<YT.OnStateChangeEvent>): void {
        switch (event.detail.data) {
            case YT.PlayerState.PAUSED:
            case YT.PlayerState.ENDED:
                this.isHeaderAndFooterVisibilityForced = true;
                this.showHeaderAndFooter();
                break;
            case YT.PlayerState.PLAYING:
                this.hideHeaderAndFooter();
                break;
        }
    }

    private async onSlideChange(event: any): Promise<void> {
        this.updateVideoPlayback();
        const swiper: Swiper = event.detail[0];
        void this.blazorRef.invokeMethodAsync('SlideChanged', swiper.activeIndex);
    }

    private updateVideoPlayback(): void {
        setTimeout(() => {
            const allVideos = this.imageViewer.getElementsByTagName('video');
            [...allVideos].forEach((video: HTMLMediaElement) => video.pause());

            const activeSlides = this.imageViewer.getElementsByClassName('swiper-slide-active');
            [...activeSlides].forEach((element: HTMLElement) => {
                const videos = element.getElementsByTagName('video');
                [...videos].forEach((video: HTMLMediaElement) => video.play());
            });
        }, 0);
    }
}
