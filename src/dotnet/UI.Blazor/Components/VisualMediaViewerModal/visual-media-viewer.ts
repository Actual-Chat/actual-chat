import { fromEvent, Subject, takeUntil } from 'rxjs';
import { clearTimeout, setTimeout } from 'timerQueue';
import { Swiper } from 'swiper';

export class VisualMediaViewer {
    private readonly disposed$: Subject<void> = new Subject<void>();
    private readonly overlay: HTMLElement;
    private readonly header: HTMLElement;
    private readonly footer: HTMLElement | undefined;
    private isHeaderAndFooterVisible: boolean = true;
    private isHeaderAndFooterVisibilityForced: boolean = false;
    private readonly jumpTime: number = 5;
    private timerId: number;
    private videos: HTMLCollectionOf<HTMLVideoElement>;
    private imageContainers: NodeListOf<HTMLElement>;

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
        this.videos = this.imageViewer.getElementsByTagName('video');
        [...this.videos].forEach((video: HTMLMediaElement) => {
            this.addVideoListeners(video);
            this.videoPlugHandler(video);
        });

        this.imageContainers = this.imageViewer.querySelectorAll('.image-container');
        [...this.imageContainers].forEach((container: HTMLElement) => {
            this.addImageListeners(container);
        });

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
        }, 3000);

        this.updateVideoPlayback();
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private fixVideoPosition() {
        const isHeaderVisible = this.isHeaderAndFooterVisible;
        const videoWrapper = this.imageViewer.querySelector('.single-attachment') as HTMLElement;
        if (!videoWrapper)
            return;

        if (!isHeaderVisible) {
            videoWrapper.style.transform = "translateY(0)";
            return;
        }

        const videoRect = videoWrapper.getBoundingClientRect();
        const headerRect = this.header.getBoundingClientRect();
        const videoHeight = videoRect.height;
        const headerHeight = headerRect.height;
        const containerHeight = document.documentElement.getBoundingClientRect().height - headerHeight;
        if (videoHeight > containerHeight) {
            videoWrapper.style.transform = `translateY(${headerHeight / 2}px)`;
            return;
        }

        const videoRectTop = videoRect.top;
        const minTop = headerRect.height;
        if (videoRectTop >= minTop)
            return;

        let heightDelta = (minTop - videoRectTop);
        videoWrapper.style.transform = `translateY(${heightDelta}px)`;
    }

    private setShowHeaderTimout() {
        this.timerId = setTimeout(() => {
            this.hideHeaderAndFooter();
            clearTimeout(this.timerId);
        }, 3000);
    }

    private showHeaderAndFooter() {
        if (this.isHeaderAndFooterVisible)
            return;

        this.setShowHeaderTimout();
        this.isHeaderAndFooterVisible = true;
        this.header.classList.remove('show-to-hide');
        this.header.classList.add('hide-to-show');
        this.footer?.classList.remove('show-to-hide');
        this.footer?.classList.add('hide-to-show');
        this.imageViewer.classList.remove('navigation-hidden');
        this.imageViewer.classList.add('navigation-visible');
        this.fixVideoPosition();
    }

    private hideHeaderAndFooter() {
        if (!this.isHeaderAndFooterVisible)
            return;

        clearTimeout(this.timerId);
        this.isHeaderAndFooterVisible = false;
        this.header.classList.remove('hide-to-show');
        this.header.classList.add('show-to-hide');
        this.footer?.classList.remove('hide-to-show');
        this.footer?.classList.add('show-to-hide');
        this.imageViewer.classList.remove('navigation-visible');
        this.imageViewer.classList.add('navigation-hidden');
        this.fixVideoPosition();
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
            [...this.videos].forEach((video: HTMLMediaElement) => {
                video.pause();
            });

            const activeSlides = this.imageViewer.getElementsByClassName('swiper-slide-active');
            [...activeSlides].forEach((element: HTMLElement) => {
                const videos = element.getElementsByTagName('video');
                [...videos].forEach((video: HTMLMediaElement) => {
                    video.play()
                        .then(_ => {
                            this.hideSpinner(video);
                            this.fixVideoPosition();
                            let control = video.parentElement.querySelector('.video-control');
                            if (control && control.classList.contains('hide-control')) {
                                control.classList.remove('hide-control');
                            }
                        });
                });
            });
        }, 0);
    }

    private hideSpinner(video: HTMLMediaElement) {
        const wrapper = video.closest('.video-wrapper');
        if (!wrapper)
            return;

        const spinner = wrapper.querySelector('.spinner-icon-wrapper');
        if (!spinner)
            return;

        spinner.remove();
    }

    private videoPlugHandler(video: HTMLMediaElement) {
        const wrapper = video.closest('.video-wrapper') as HTMLElement;
        if (!wrapper)
            return;

        const thumbnailWrapper = wrapper.querySelector('.video-thumbnail-wrapper') as HTMLElement;
        if (!thumbnailWrapper)
            return;

        const thumbnail = wrapper.querySelector('.video-thumbnail') as HTMLElement;
        if (!thumbnail)
            return;

        const spinner = wrapper.querySelector('.spinner-icon-wrapper') as HTMLElement;

        if (video.readyState == 4) {
            thumbnailWrapper.remove();
            if (spinner)
                spinner.remove();
            return;
        }

        const imagePlug = thumbnail.querySelector('image-skeleton') as HTMLImageElement;
        if (imagePlug) {
            let plugWidth = 0;
            let plugHeight = 0;
            const originalWidth = Number(thumbnail.dataset.width);
            const originalHeight = Number(thumbnail.dataset.height);
            const screenWidth = window.innerWidth;
            const screenHeight = window.innerHeight;
            const originalRatio = originalWidth / originalHeight;

            plugWidth = screenWidth < originalWidth ? screenWidth : originalWidth;
            plugHeight = plugWidth / originalRatio;
            if (screenHeight < plugHeight) {
                plugHeight = screenHeight;
                plugWidth = plugHeight * originalRatio;
            }

            thumbnail.style.width = plugWidth + 'px';
            thumbnail.style.height = plugWidth / originalRatio + 'px';
        }
    }

    private addVideoListeners(video: HTMLMediaElement) {
        const control = video.parentElement.querySelector('.video-control') as HTMLElement;
        const playBtn = control.querySelector('.play-btn') as HTMLElement;
        const rewindBtn = control.querySelector('.rewind-btn') as HTMLElement;
        const forwardBtn = control.querySelector('.forward-btn') as HTMLElement;
        const progressBar = control.querySelector('progress') as HTMLProgressElement;
        if (!control || !playBtn || !rewindBtn || !forwardBtn || !progressBar) {
            if (control)
                control.remove();
            video.controls = true;
        }

        fromEvent(video, 'loadeddata')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: Event) => this.onVideoLoaded(event, video));

        fromEvent(video, 'play')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: Event) => this.playAndPauseHandler(event, playBtn));

        fromEvent(video, 'pause')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: Event) => this.playAndPauseHandler(event, playBtn));

        fromEvent(video, 'timeupdate')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: Event) => this.updateTimeline(video, control, progressBar));

        fromEvent(playBtn, 'click')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: PointerEvent | MouseEvent) => this.onPlayBtnClick(event, video));

        fromEvent(rewindBtn, 'click')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: PointerEvent | MouseEvent) => this.onJumpBtnClick(event, video, false));

        fromEvent(forwardBtn, 'click')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: PointerEvent | MouseEvent) => this.onJumpBtnClick(event, video, true));

        fromEvent(progressBar, 'click')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: PointerEvent | MouseEvent) => this.seekVideoPoint(event, video, progressBar));
    }

    private addImageListeners(container: HTMLElement) {
        const original = container.querySelector('.image-original') as HTMLImageElement;
        const plug = container.querySelector('.image-plug') as HTMLImageElement;
        const spinner = container.querySelector('.spinner-icon-wrapper');
        if (!original)
            return;

        if (!plug)
            return;

        if (original.complete) {
            plug.remove();
            if (spinner)
                spinner.remove();
            return;
        }

        let plugWidth = 0;
        const originalWidth = Number(plug.dataset.width);
        const originalHeight = Number(plug.dataset.height);
        const screenWidth = window.innerWidth;
        const screenHeight = window.innerHeight;
        const originalRatio = originalWidth / originalHeight;
        const screenRatio = screenWidth / screenHeight;
        if (screenRatio > originalRatio && originalHeight > screenHeight) {
            plugWidth = screenHeight * originalRatio;
        } else {
            plugWidth = originalWidth;
        }
        plug.width = plugWidth;

        fromEvent(original, 'load')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: Event) => this.onImageLoaded(event, container));
    }

    private onImageLoaded(event: Event, container: HTMLElement) {
        const plug = container.querySelector('.image-plug') as HTMLImageElement;
        const spinner = container.querySelector('.spinner-icon-wrapper');
        plug.remove();
        spinner.remove();
    }

    private onVideoLoaded(event: Event, video: HTMLMediaElement) {
        const wrapper = video.closest('.video-wrapper');
        const thumbnailWrapper = wrapper.querySelector('.video-thumbnail-wrapper');
        const spinner = wrapper.querySelector('.spinner-icon-wrapper');
        const control = wrapper.querySelector('.video-control') as HTMLElement;
        if (thumbnailWrapper)
            thumbnailWrapper.remove();
        if (spinner)
            spinner.remove();
        if (control)
            control.classList.remove('invisible');
        this.updateLoading(video);
    }

    private updateLoading(video: HTMLMediaElement) {
        const wrapper = video.closest('.video-wrapper');
        if (!wrapper)
            return;

        const control = wrapper.querySelector('.video-control') as HTMLElement;
        if (!control)
            return;

        const progressBar = control.querySelector('.c-progress-bar') as HTMLElement;
        const loadedBar = window.getComputedStyle(progressBar, ':before');
        if (!loadedBar)
            return;

        const duration = video.duration;
        let bufferId = setInterval(() => {
            let buffered = video.buffered.end(0);
            let maxWidth = progressBar.getBoundingClientRect().width;
            if (buffered == duration) {
                clearInterval(bufferId);
                progressBar.style.setProperty('--data-media-loaded', `${maxWidth}px`);
                return;
            }
            let loadedValue = buffered / duration;
            progressBar.style.setProperty('--data-media-loaded', `${loadedValue * maxWidth}px`);
        }, 200);
    }

    private updateTimeline(video: HTMLMediaElement, control: HTMLElement, progressBar: HTMLProgressElement) {
        let current = video.currentTime;
        let percentage = Math.round(current / video.duration * 100);
        progressBar.value = percentage;
        progressBar.innerHTML = percentage + '% played';
        let currentTimeDiv = control.querySelector('.c-current');
        currentTimeDiv.innerHTML = this.formatTime(current);
        let durationDiv = control.querySelector('.c-duration');
        durationDiv.innerHTML = this.formatTime(video.duration);
    }

    private formatTime(time: number) : string {
        let minutes = '';
        let seconds = '';
        let minNum = Math.floor((time / 60));
        let secNum = Math.round(time - (minNum * 60));
        if (minNum.toString().length < 2)
            minutes = `0${minNum}`;
        else
            minutes = minNum.toString();
        if (secNum.toString().length < 2)
            seconds = `0${secNum}`;
        else
            seconds = secNum.toString();
        return `${minutes}:${seconds}`;
    }

    private onPlayBtnClick(event: PointerEvent | MouseEvent, video: HTMLMediaElement) {
        event.stopPropagation();
        video.paused ? video.play() : video.pause();
    }

    private onJumpBtnClick(
        event: PointerEvent | MouseEvent,
        video: HTMLMediaElement,
        forward: boolean) {
        event.stopPropagation();
        const timeDelta = forward ? this.jumpTime : -this.jumpTime;
        video.currentTime += timeDelta
    }

    private seekVideoPoint(event: PointerEvent | MouseEvent, video: HTMLMediaElement, progressBar: HTMLProgressElement) {
        event.stopPropagation();
        let percent = event.offsetX / progressBar.offsetWidth;
        video.currentTime = percent * video.duration;
        let value = progressBar.value = Math.floor(percent * 100);
        progressBar.innerHTML = value + '% played';
    }

    private playAndPauseHandler(e: Event, btn: HTMLElement) {
        switch (e.type) {
            case 'play':
                btn.classList.remove('is-paused');
                if (!btn.classList.contains('is-playing'))
                    btn.classList.add('is-playing');
                break;
            case 'pause':
                btn.classList.remove('is-playing');
                if (!btn.classList.contains('is-paused'))
                    btn.classList.add('is-paused');
                break;
        }
    }
}
