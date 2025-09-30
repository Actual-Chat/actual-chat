import { fromEvent, Subject, takeUntil } from 'rxjs';
import { clearTimeout, setTimeout } from 'timerQueue';
import { Swiper } from 'swiper';
import { debounce, throttle } from 'promises';

interface SwiperElement extends HTMLElement {
    swiper: any;
    initialize: () => void;
}

export class VisualMediaViewer {
    private readonly disposed$: Subject<void> = new Subject<void>();
    private readonly overlay: HTMLElement;
    private readonly header: HTMLElement;
    private readonly footer: HTMLElement | undefined;
    private isHeaderAndFooterVisible: boolean = true;
    private readonly jumpTime: number = 5;
    private videos: HTMLCollectionOf<HTMLVideoElement>;
    private imageContainers: NodeListOf<HTMLElement>;
    private maxVideoWidth: number = 0;
    private maxVideoHeight: number = 0;
    private videoRatio: number = 0;
    private readonly headerHeight: number = 0;
    private windowWidth: number = 0;
    private windowHeight: number = 0;
    private hideTimeoutId: number | null = null;
    private swiperEl: any;
    private thumbsEl: any;
    private swiper: any;
    private thumbs: any;
    private clickTimeout: number | null = null;
    private CLICK_DELAY = 250;

    static create(imageViewer: HTMLElement, blazorRef: DotNet.DotNetObject): VisualMediaViewer {
        return new VisualMediaViewer(imageViewer, blazorRef);
    }

    constructor(
        private readonly imageViewer: HTMLElement,
        private readonly blazorRef: DotNet.DotNetObject
    ) {
        this.overlay = this.imageViewer.closest('.modal-overlay')!;
        this.header = this.overlay.querySelector('.image-viewer-header')!;
        this.headerHeight = this.header.offsetHeight;
        this.footer = this.overlay.querySelector('.image-viewer-footer') as HTMLElement;
        this.videos = this.imageViewer.getElementsByTagName('video');

        this.swiperEl = document.querySelector('.media-swiper') as SwiperElement;
        this.thumbsEl = document.querySelector('.media-preview-swiper') as SwiperElement;

        const mainSwiperParams = {
            touchRatio: 1,
            touchAngle: 45,
            resistanceRatio: 0.85,
            threshold: 10,
        };
        Object.assign(this.swiperEl, mainSwiperParams);
        this.swiperEl.initialize();
        this.swiper = this.swiperEl.swiper as SwiperElement;


        if (this.thumbsEl) {
            const footerSwiperParams = {
                slidesPerView: 'auto',
                watchSlidesProgress: true,
                freeMode: true,
            }
            Object.assign(this.thumbsEl, footerSwiperParams);
            this.thumbsEl.initialize();
            this.thumbs = this.thumbsEl.swiper as SwiperElement;
            void this.initThumbs();
        }



        this.swiper.on('init', () => this.safeCenterThumb());

        this.swiper.on('slideChange', async () => {
            void this.onSlideChange(this.swiper);
            this.onUserActivityThrottled();
            await this.safeCenterThumb();
        });

        this.swiper.on('pointerDown', (swiper: Swiper, event: PointerEvent) =>
            this.onZoomedSlideSwipe(swiper, event)
        );

        fromEvent(window.visualViewport ?? window, 'resize')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: Event) => {
                [...this.videos].forEach(video => {
                    const activeSlide = this.imageViewer.querySelector('.swiper-slide-active');
                    if (activeSlide) {
                        const activeVideo = activeSlide.querySelector('video');
                        if (activeVideo)
                            this.setMaxSize(activeVideo);
                    }
                    this.onUpdateProgressBarDebounced(event, video);
                });
            });

        [...this.videos].forEach((video: HTMLMediaElement) => {
            this.setMaxSize(video);
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

        fromEvent(this.overlay, 'dblclick')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: PointerEvent) => this.onClick(event, true));

        fromEvent(this.overlay, 'youtubeplayeronstatechange')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: CustomEvent<YT.OnStateChangeEvent>) => this.onYouTubePlayerStateChange(event));

        fromEvent<PointerEvent>(document, 'pointermove')
            .pipe(takeUntil(this.disposed$))
            .subscribe(ev => {
                if (ev.pointerType !== 'mouse') {
                    return;
                }
                this.onUserActivityThrottled();
            });

        this.setHideTimeout();
        this.updateVideoPlayback();
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private async initThumbs() {
        ["imagesReady", "resize", "update"].forEach(event =>
            this.thumbs.on(event, () => this.safeCenterThumb())
        );

        this.thumbs.update();
        await this.safeCenterThumb();
    }

    private safeCenterThumb(attempt: number = 0): Promise<void> {
        return new Promise(resolve => {
            if (!this.swiper || !this.thumbs) {
                resolve();
                return;
            }

            const index = this.swiper.realIndex ?? this.swiper.activeIndex;
            const slide = this.thumbs.slides[index];

            if (!slide) {
                resolve();
                return;
            }

            const swiperWidth = this.thumbs.width;
            const slideLeft = slide.offsetLeft;
            const slideWidth = slide.scrollWidth;

            if ((!swiperWidth || !slideWidth) && attempt < 10) {
                setTimeout(() => {
                    this.safeCenterThumb(attempt + 1).then(resolve);
                }, 50);
                return;
            }

            if (!swiperWidth || !slideWidth) {
                resolve();
                return;
            }

            const target = slideLeft - (swiperWidth / 2) + (slideWidth / 2);

            this.thumbs.setTranslate(-target);
            this.thumbs.updateProgress();
            this.thumbs.updateActiveIndex();
            this.thumbs.updateSlidesClasses();
            this.thumbs.wrapperEl.style.transform = `translate3d(${-target}px, 0, 0)`;
            resolve();
        });
    }

    private setMaxSize(videoFile: HTMLMediaElement) {
        const windowWidth = document.documentElement.offsetWidth;
        const windowHeight = document.documentElement.offsetHeight;
        if (windowWidth == this.windowWidth
            && windowHeight == this.windowHeight
            && this.videoRatio != 0
            && this.maxVideoWidth != 0
            && this.maxVideoHeight != 0)
            return;

        this.windowWidth = windowWidth;
        this.windowHeight = windowHeight;
        let maxWidth = Number(videoFile.dataset.width);
        let maxHeight = Number(videoFile.dataset.height);
        this.videoRatio = maxWidth / maxHeight;
        if (windowWidth < maxWidth) {
            maxWidth = windowWidth;
            maxHeight = maxWidth / this.videoRatio;
        }
        if (windowHeight < maxHeight) {
            maxHeight = windowHeight;
            maxWidth = maxHeight * this.videoRatio;
        }
        this.maxVideoWidth = maxWidth;
        this.maxVideoHeight = maxHeight;
        videoFile.style.maxWidth = `${maxWidth}px`;
        videoFile.style.maxHeight = `${maxHeight}px`;
    }

    private fixVideoPosition() {
        const activeSlide = this.imageViewer.querySelector('.swiper-slide-active');
        if (!activeSlide)
            return;

        const isHeaderVisible = this.isHeaderAndFooterVisible;
        const videoWrapper = activeSlide.querySelector('.video-wrapper') as HTMLElement;
        if (!videoWrapper)
            return;

        const videoFile = videoWrapper.querySelector('video');
        if (!videoFile)
            return;

        this.setMaxSize(videoFile);

        if (!isHeaderVisible) {
            videoWrapper.style.transform = "translateY(0)";
            return;
        }

        const videoHeight = videoWrapper.offsetHeight;
        const containerHeight = document.documentElement.offsetHeight - this.headerHeight;
        if (videoHeight >= containerHeight) {
            if (!this.footer) {
                videoWrapper.style.transform = `translateY(${this.headerHeight / 2}px)`;
            } else {
                videoWrapper.style.transform = "translateY(0)";
            }
            return;
        }
    }

    private setHideTimeout() {
        if (this.hideTimeoutId !== null)
            clearTimeout(this.hideTimeoutId);

        this.hideTimeoutId = window.setTimeout(() => {
            void this.hideHeaderAndFooter();
        }, 3000);
    }

    private onUserActivityThrottled = throttle(() =>
        this.onUserActivity(), 500, 'delayHead');

    private onUserActivity() {
        void this.showHeaderAndFooter();
    }

    private async showHeaderAndFooter() {
        if (this.isHeaderAndFooterVisible) {
            this.setHideTimeout();
            return;
        }

        const footer = this.footer;
        const header = this.header;
        const viewer = this.imageViewer;
        const canContinue = (!this.thumbsEl && header && viewer) || (this.thumbsEl && footer && header && viewer);
        if (!canContinue)
            return;

        this.isHeaderAndFooterVisible = true;

        header.classList.remove('show-to-hide');
        viewer.classList.remove('navigation-hidden');
        header.classList.add('hide-to-show');
        viewer.classList.add('navigation-visible');

        if (footer) {
            footer.style.transition = 'none';
            footer.style.opacity = '0';
            footer.style.transform = 'translateY(3.5rem)';
            footer.classList.remove('show-to-hide');
            footer.classList.add('hide-to-show');

            await this.safeCenterThumb();

            requestAnimationFrame(() => {
                footer.style.transition = '';
                footer.style.opacity = '1';
                footer.style.transform = '';
                this.fixVideoPosition();
            });
        }

        if (this.thumbs) {
            setTimeout(() => {
                this.thumbs.update();
            }, 50);
        }

        this.setHideTimeout();
    }

    private async hideHeaderAndFooter() {
        if (!this.isHeaderAndFooterVisible)
            return;

        const footer = this.footer;
        const header = this.header;
        const viewer = this.imageViewer;
        const canContinue = (!this.thumbsEl && header && viewer) || (this.thumbsEl && footer && header && viewer);
        if (!canContinue)
            return;

        this.isHeaderAndFooterVisible = false;



        header.classList.remove('hide-to-show');
        viewer.classList.remove('navigation-visible');
        header.classList.add('show-to-hide');
        viewer.classList.add('navigation-hidden');

        if (footer) {
            footer.style.transition = 'none';
            footer.style.transform = 'translateY(3.5rem)';
            footer.classList.remove('hide-to-show');
            footer.classList.add('show-to-hide');

            await this.safeCenterThumb();

            requestAnimationFrame(() => {
                footer.style.transition = '';
                footer.style.transform = '';
                this.fixVideoPosition();
            });

        }

        if (this.thumbs) {
            setTimeout(() => {
                this.thumbs.update();
            }, 50);
        }
    }

    private toggleHeaderAndFooterVisibility() {
        if (this.isHeaderAndFooterVisible) {
            void this.hideHeaderAndFooter();
        } else {
            void this.showHeaderAndFooter();
        }
    }

    // Event handlers

    private onZoomedSlideSwipe(swiper: Swiper, event: PointerEvent) {
        if (swiper.zoom.scale > 1) {
            if (!swiper)
                return;

            const activeIndex = swiper.activeIndex;
            const activeSlide = swiper.slides[activeIndex];
            const image = activeSlide.querySelector('img')!;
            const prevButton = swiper.navigation.prevEl;
            const nextButton = swiper.navigation.nextEl;

            if (prevButton && prevButton.contains(event.target as Node) && !prevButton.classList.contains('.swiper-button-disabled'))
                return;

            if (nextButton && nextButton.contains(event.target as Node) && !nextButton.classList.contains('.swiper-button-disabled'))
                return;

            const rect = image.getBoundingClientRect();
            const windowWidth = window.innerWidth;

            if (rect.right - rect.left <= windowWidth)
                return;

            swiper.allowSlidePrev = rect.left > -10;
            swiper.allowSlideNext = rect.right - windowWidth < 10;
        }
    }

    private onClick(event: PointerEvent | MouseEvent, isDouble: boolean = false) {
        if (isDouble)
            return;

        const { pageY } = event;
        const cursorInHeaderArea = pageY <= this.header.offsetHeight;
        const cursorInFooterArea = this.footer
            ? this.overlay.offsetHeight - pageY <= this.footer.offsetHeight
            : false;
        if (this.isHeaderAndFooterVisible && (cursorInHeaderArea || cursorInFooterArea))
            return;

        const target = event.target as HTMLElement;

        if (this.clickTimeout) {
            clearTimeout(this.clickTimeout);
            this.clickTimeout = null;
            return;
        }

        this.clickTimeout = window.setTimeout(() => {
            this.clickTimeout = null;
            if (target.classList.contains('media-swiper')) {
                // click on prev / next buttons
            } else if (target.classList.contains('swiper-zoom-container')) {
                // click outside image/video
                void this.blazorRef.invokeMethodAsync('Close');
            } else {
                // click on image/video
                this.toggleHeaderAndFooterVisibility();
            }
        }, this.CLICK_DELAY);
    }

    private onYouTubePlayerStateChange(event: CustomEvent<YT.OnStateChangeEvent>): void {
        switch (event.detail.data) {
            case YT.PlayerState.PAUSED:
            case YT.PlayerState.ENDED:
                void this.showHeaderAndFooter();
                break;
            case YT.PlayerState.PLAYING:
                void this.hideHeaderAndFooter();
                break;
        }
    }

    private async onSlideChange(swiper: Swiper): Promise<void> {
        this.updateVideoPlayback();
        if (this.maxVideoWidth != 0 || this.maxVideoWidth != 0 || this.videoRatio != 0)
            this.maxVideoWidth = this.maxVideoHeight = this.videoRatio = 0;
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
                            let control = video.parentElement!.querySelector('.video-control');
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
        const control = video.parentElement!.querySelector('.video-control') as HTMLElement;
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
        const spinner = container.querySelector('.spinner-icon-wrapper')!;
        plug.remove();
        spinner.remove();
    }

    private onVideoLoaded(event: Event, video: HTMLMediaElement) {
        const wrapper = video.closest('.video-wrapper')!;
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
        const wrapper = video.closest('.video-wrapper') as HTMLElement;
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
            let maxWidth = progressBar.offsetWidth;
            if (buffered == duration) {
                clearInterval(bufferId);
                progressBar.style.setProperty('--data-media-loaded', `${maxWidth}px`);
                return;
            }
            let loadedValue = buffered / duration;
            progressBar.style.setProperty('--data-media-loaded', `${loadedValue * maxWidth}px`);
        }, 200);
    }

    private onUpdateProgressBarDebounced: (event: Event, video: HTMLVideoElement) => void = debounce(
        (event: Event, video: HTMLVideoElement) => this.updateProgressBar(event, video),
        200
    );

    private updateProgressBar(event: Event, video: HTMLVideoElement) {
        const wrapper = video.closest('.video-wrapper') as HTMLElement;
        if (!wrapper)
            return;

        const control = wrapper.querySelector('.video-control') as HTMLElement;
        if (!control)
            return;

        const progressBar = control.querySelector('.c-progress-bar') as HTMLElement;
        const loadedBar = window.getComputedStyle(progressBar, ':before');
        if (!loadedBar)
            return;

        let maxWidth = progressBar.offsetWidth;
        progressBar.style.setProperty('--data-media-loaded', `${maxWidth}px`);
    }

    private updateTimeline(video: HTMLMediaElement, control: HTMLElement, progressBar: HTMLProgressElement) {
        let current = video.currentTime;
        let percentage = Math.round(current / video.duration * 100);
        progressBar.value = percentage;
        progressBar.innerHTML = percentage + '% played';
        let currentTimeDiv = control.querySelector('.c-current')!;
        currentTimeDiv.innerHTML = this.formatTime(current);
        let durationDiv = control.querySelector('.c-duration')!;
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
