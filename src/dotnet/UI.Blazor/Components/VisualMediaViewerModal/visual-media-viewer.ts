import { fromEvent, Subject, takeUntil } from 'rxjs';
import { clearTimeout, setTimeout } from 'timerQueue';
import { Swiper } from 'swiper';
import { debounce } from 'actuallab-core';

// TODO(Andrey) fix eslint errors
interface SwiperElement extends HTMLElement {
    // eslint-disable-next-line
    swiper: any;
    initialize: () => void;
}

export class VisualMediaViewer {
    private readonly disposed$: Subject<void> = new Subject<void>();
    private readonly overlay: HTMLElement;
    private readonly header: HTMLElement;
    private readonly footer: HTMLElement | undefined;
    private isHeaderAndFooterVisible = true;
    private readonly jumpTime: number = 5;
    private videos: NodeListOf<HTMLVideoElement>;
    private imageContainers: NodeListOf<HTMLElement>;
    private maxVideoWidth = 0;
    private maxVideoHeight = 0;
    private videoRatio = 0;
    private readonly headerHeight: number = 0;
    private windowWidth = 0;
    private windowHeight = 0;
    private hideTimeoutId: number | null = null;
    private hideLockedUntil = 0;
    // eslint-disable-next-line
    private swiperEl: any;
    // eslint-disable-next-line
    private thumbsEl: any;
    // eslint-disable-next-line
    private swiper: any;
    // eslint-disable-next-line
    private thumbs: any;
    private isHiding = false;
    private hideBlocked = false;
    private isDoubleClick = false;
    private isDraggingThumb = false;
    private isReindexing = false;
    private readonly wiredMedia: WeakSet<Element> = new WeakSet<Element>();
    // Swipe-to-dismiss / info panel state
    private isDismissing = false;
    private dismissStartY = 0;
    private dismissStartX = 0;
    private dismissLocked = false;
    private isInfoOpen = false;
    private readonly infoPanel: HTMLElement | null;
    // Stored transform values when info panel is open (for reverse animation)
    private infoTranslateY = 0;
    private infoScale = 1;

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
        // @ts-expect-error TODO(Andrey): fix eslint error
        this.footer = this.overlay.querySelector('.image-viewer-footer')!;
        this.videos = this.imageViewer.querySelectorAll<HTMLVideoElement>('video.video-original');
        this.infoPanel = this.overlay.querySelector<HTMLElement>('.media-info-panel');

        // eslint-disable-next-line
        this.swiperEl = document.querySelector('.media-swiper')!;
        // eslint-disable-next-line
        this.thumbsEl = document.querySelector('.media-preview-swiper')!;

        const mainSwiperParams = {
            touchRatio: 1,
            touchAngle: 45,
            resistanceRatio: 0.85,
            threshold: 10,
            keyboard: { enabled: true, onlyInViewport: false },
        };
        Object.assign(this.swiperEl, mainSwiperParams);
        // eslint-disable-next-line
        this.swiperEl.initialize();
        // eslint-disable-next-line
        this.swiper = this.swiperEl.swiper as SwiperElement;


        if (this.thumbsEl) {
            const footerSwiperParams = {
                slidesPerView: 'auto',
                watchSlidesProgress: true,
                freeMode: true,
            }
            Object.assign(this.thumbsEl, footerSwiperParams);
            // eslint-disable-next-line
            this.thumbsEl.initialize();
            // eslint-disable-next-line
            this.thumbs = this.thumbsEl.swiper as SwiperElement;
            void this.initThumbs();
        }

        // eslint-disable-next-line
        this.swiper.on('init', () => this.safeCenterThumb());

        // eslint-disable-next-line
        this.swiper.on('slideChange', async () => {
            // eslint-disable-next-line
            void this.onSlideChange(this.swiper);
            await this.safeCenterThumb();
        });

        // eslint-disable-next-line
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
            this.wiredMedia.add(video);
        });

        this.imageContainers = this.imageViewer.querySelectorAll('.image-container');
        [...this.imageContainers].forEach((container: HTMLElement) => {
            this.addImageListeners(container);
            this.wiredMedia.add(container);
        });

        fromEvent(this.overlay, 'click')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: PointerEvent) => this.onClick(event));

        fromEvent(this.header, 'transitionend')
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => this.onHideTransitionEnd());

        if (this.footer) {
            fromEvent(this.footer, 'transitionend')
                .pipe(takeUntil(this.disposed$))
                .subscribe(() => this.onHideTransitionEnd());
        }

        this.initSwipeToDismiss();

        // Auto-hide after 3s if the user doesn't interact
        this.setInitialHideTimeout();
        this.updateVideoPlayback();
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        // Pause all videos and notify audio focus system
        [...this.videos].forEach((video: HTMLMediaElement) => {
            if (!video.paused) {
                video.pause();
            }
        });
        // Notify that external media playback stopped when modal closes
        void this.blazorRef.invokeMethodAsync('OnVideoPlaybackStopped');

        if (this.hideTimeoutId !== null) {
            clearTimeout(this.hideTimeoutId);
            this.hideTimeoutId = null;
        }

        this.disposed$.next();
        this.disposed$.complete();
    }

    // Called from Blazor after the rendered slide window changes (items appended/prepended
    // or a synthetic attachment upgraded to its resolved one). When activeIndex >= 0 a
    // prepend shifted indices, so the active slide is re-pointed without animation or a
    // recursive load-more (guarded by isReindexing) before media is re-measured.
    public onWindowChanged(activeIndex = -1): void {
        this.wireNewMedia();
        // eslint-disable-next-line @typescript-eslint/no-unsafe-call,@typescript-eslint/no-unsafe-member-access
        this.swiper.update();
        if (activeIndex >= 0) {
            this.isReindexing = true;
            // eslint-disable-next-line @typescript-eslint/no-unsafe-call,@typescript-eslint/no-unsafe-member-access
            this.swiper.slideTo(activeIndex, 0, false);
            requestAnimationFrame(() => this.isReindexing = false);
        }
        if (this.thumbs)
            // eslint-disable-next-line @typescript-eslint/no-unsafe-call,@typescript-eslint/no-unsafe-member-access
            this.thumbs.update();
        // The active slide's dimensions may have just resolved (synthetic 0×0 → real),
        // so re-measure it for correct video scaling.
        this.maxVideoWidth = this.maxVideoHeight = this.videoRatio = 0;
        const activeVideo = this.imageViewer.querySelector<HTMLVideoElement>('.swiper-slide-active video.video-original');
        if (activeVideo)
            this.setMaxSize(activeVideo);
        void this.safeCenterThumb();
        this.updateVideoPlayback();
    }

    // State machine:
    //   VIEWING ──swipe up / info btn──► INFO_OPEN
    //   VIEWING ──swipe down───────────► CLOSE
    //   INFO_OPEN ──swipe down─────────► VIEWING
    //   INFO_OPEN ──close btn──────────► CLOSE

    private openInfoPanel() {
        if (this.isInfoOpen || !this.infoPanel)
            return;

        this.isInfoOpen = true;
        const swiper = this.swiper as Swiper;
        swiper.allowTouchMove = false;

        // Info panel slides up
        this.infoPanel.style.transition = 'transform 300ms ease-out';
        this.infoPanel.style.transform = 'translateY(0)';
        this.infoPanel.classList.add('is-open');

        // Hide header, footer, navigation
        this.header.style.transition = 'opacity 200ms ease-out';
        this.header.style.opacity = '0';
        this.header.style.pointerEvents = 'none';
        if (this.footer) {
            this.footer.style.visibility = 'hidden';
        }
        this.imageViewer.classList.remove('navigation-visible');
        this.imageViewer.classList.add('navigation-hidden');

        setTimeout(() => {
            if (this.infoPanel) this.infoPanel.style.transition = '';
            this.header.style.transition = '';
        }, 350);
    }

    private closeInfoPanel() {
        if (!this.isInfoOpen || !this.infoPanel)
            return;

        this.isInfoOpen = false;
        const swiper = this.swiper as Swiper;
        swiper.allowTouchMove = true;

        // Slide content back to center + reset img scale
        const swiperHost = this.swiperEl as HTMLElement;
        const content = swiperHost.querySelector<HTMLElement>('.swiper-slide-active .swiper-zoom-container');
        if (content) {
            content.style.transition = 'transform 300ms ease-out';
            content.style.transform = '';
            const imgEl = content.querySelector<HTMLElement>('img.image-original')
                ?? content.querySelector<HTMLElement>('img.image-plug')
                ?? content.querySelector<HTMLElement>('video.video-original');
            if (imgEl) {
                imgEl.style.transition = 'transform 300ms ease-out';
                imgEl.style.transform = '';
                imgEl.style.transformOrigin = '';
                const imgRef = imgEl;
                setTimeout(() => { imgRef.style.transition = ''; }, 350);
            }
        }

        // Info panel slides down
        this.infoPanel.style.transition = 'transform 300ms ease-out';
        this.infoPanel.style.transform = '';
        this.infoPanel.classList.remove('is-open');

        // Restore header, footer, navigation
        this.header.style.transition = 'opacity 300ms ease-out';
        this.header.style.opacity = '';
        this.header.style.pointerEvents = '';
        // Re-center thumbs before showing footer to avoid jump
        void this.safeCenterThumb().then(() => {
            if (this.footer)
                this.footer.style.visibility = '';
        });
        this.imageViewer.classList.remove('navigation-hidden');
        this.imageViewer.classList.add('navigation-visible');

        setTimeout(() => {
            if (content) {
                content.style.transition = '';
                content.style.transform = '';
                content.style.transformOrigin = '';
            }
            if (this.infoPanel) this.infoPanel.style.transition = '';
            this.header.style.transition = '';
        }, 350);
    }

    private initSwipeToDismiss() {
        const dismissThreshold = 120;
        const dragDeadzone = 15;
        const modalFrame = this.overlay.querySelector<HTMLElement>('.modal-frame')!;
        const swiperHost = this.swiperEl as HTMLElement;
        const swiper = this.swiper as Swiper;
        let lastDeltaY = 0;
        let gestureMaxUp = 0;
        let gestureImgHeight = 0;
        let gestureImgTop = 0;
        let savedThumbsTransform = ''; // lock thumbs position during gesture

        const measureGestureParams = () => {
            const content = getSlideContent();
            if (!content) return;
            for (const sel of ['img.image-original', 'img.image-plug', 'video.video-original']) {
                const el = content.querySelector<HTMLElement>(sel);
                if (el) {
                    const rect = el.getBoundingClientRect();
                    if (rect.height > 0) {
                        gestureImgHeight = rect.height;
                        gestureImgTop = rect.top;
                        // Image bottom must not go above (screenCenter + 2rem overlap)
                        const stopAt = window.innerHeight / 2 + 32;
                        gestureMaxUp = Math.max(0, rect.bottom - stopAt);
                        return;
                    }
                }
            }
        };

        const getSlideContent = (): HTMLElement | null => {
            const activeSlide = swiperHost.querySelector<HTMLElement>('.swiper-slide-active');
            return activeSlide?.querySelector<HTMLElement>('.swiper-zoom-container') ?? null;
        };

        const getVideoControl = (): HTMLElement | null => {
            const activeSlide = swiperHost.querySelector<HTMLElement>('.swiper-slide-active');
            return activeSlide?.querySelector<HTMLElement>('.video-control') ?? null;
        };

        const resetDismiss = () => {
            this.isDismissing = false;
            this.dismissLocked = false;
            this.dismissStartY = 0;
            this.dismissStartX = 0;
            lastDeltaY = 0;
            gestureMaxUp = 0;
            gestureImgHeight = 0;
            gestureImgTop = 0;
            savedThumbsTransform = '';
        };

        const restoreSwiper = () => {
            swiper.allowTouchMove = true;
        };

        const clearStyles = () => {
            const content = getSlideContent();
            if (content) {
                content.style.transition = '';
                content.style.transform = '';
                // Clear scale from img element
                const imgEl = content.querySelector<HTMLElement>('img.image-original')
                    ?? content.querySelector<HTMLElement>('img.image-plug')
                    ?? content.querySelector<HTMLElement>('video.video-original');
                if (imgEl) {
                    imgEl.style.transform = '';
                    imgEl.style.transformOrigin = '';
                    imgEl.style.transition = '';
                }
            }
            const vc = getVideoControl();
            if (vc) {
                vc.style.transition = '';
                vc.style.transform = '';
            }
            modalFrame.style.transition = '';
            modalFrame.style.opacity = '';
            this.header.style.opacity = '';
            this.header.style.transition = '';
            this.header.style.pointerEvents = '';
            if (this.footer) {
                this.footer.style.visibility = '';
                this.footer.style.transition = '';
            }
            if (this.infoPanel) {
                this.infoPanel.style.transition = '';
                this.infoPanel.style.transform = '';
            }
        };

        const onTouchStart = (e: TouchEvent) => {
            if (swiper.zoom.scale > 1)
                return;

            if (e.touches.length > 1)
                return;

            const target = e.target as HTMLElement;
            if (target.closest('.image-viewer-header')
                || target.closest('.image-viewer-footer')
                || target.closest('.video-control')
                || (!this.isInfoOpen && target.closest('.media-info-panel')))
                return;

            const touch = e.touches[0];
            this.dismissStartY = touch.clientY;
            this.dismissStartX = touch.clientX;
            this.isDismissing = false;
            this.dismissLocked = false;
            lastDeltaY = 0;
            measureGestureParams();
            // Lock thumbs position so Swiper doesn't shift them during gesture
            // eslint-disable-next-line @typescript-eslint/no-unsafe-member-access
            if (this.thumbs?.wrapperEl)
                // eslint-disable-next-line @typescript-eslint/no-unsafe-member-access
                savedThumbsTransform = (this.thumbs.wrapperEl as HTMLElement).style.transform;
        };

        const onTouchMove = (e: TouchEvent) => {
            if (this.dismissStartY === 0 || this.dismissLocked)
                return;

            if (e.touches.length > 1) {
                if (this.isDismissing) {
                    restoreSwiper();
                    clearStyles();
                }
                resetDismiss();
                return;
            }

            const touch = e.touches[0];
            const deltaY = touch.clientY - this.dismissStartY;
            const deltaX = touch.clientX - this.dismissStartX;

            if (!this.isDismissing) {
                if (Math.abs(deltaX) > Math.abs(deltaY)) {
                    this.dismissLocked = true;
                    return;
                }
                if (Math.abs(deltaY) < dragDeadzone)
                    return;

                // Swipe up while info is open — ignore (panel handles its own scroll)
                // Swipe up while viewing — this will open info panel on release
                // Swipe down while info is open — this will close info panel on release
                // Swipe down while viewing — this will dismiss
                this.isDismissing = true;
                swiper.allowTouchMove = false;
            }

            if (e.cancelable) {
                e.preventDefault();
                e.stopPropagation();
            }

            lastDeltaY = deltaY;

            if (!this.isInfoOpen && deltaY > 0) {
                // Swipe down — dismiss feedback
                const content = getSlideContent();
                if (content)
                    content.style.transform = `translateY(${deltaY}px)`;
                const vc = getVideoControl();
                if (vc)
                    vc.style.transform = `translateY(${-deltaY}px)`;
                const progress = Math.min(deltaY / 300, 1);
                modalFrame.style.opacity = `${1 - progress * 0.6}`;
                // Header/footer fade out 3x faster
                const fastFade = `${1 - Math.min(deltaY / 150, 1)}`;
                this.header.style.opacity = fastFade;
                if (this.footer)
                    this.footer.style.visibility = parseFloat(fastFade) < 0.01 ? 'hidden' : '';
            } else if (!this.isInfoOpen && deltaY < 0) {
                // Swipe up — image moves up, clamped so bottom stays at screen center + 2rem
                const absDelta = Math.abs(deltaY);
                const clampedDelta = gestureMaxUp > 0
                    ? Math.max(deltaY, -gestureMaxUp)
                    : 0;

                const content = getSlideContent();
                if (content) {
                    // translateY always on the container
                    content.style.transform = `translateY(${clampedDelta}px)`;

                    // After image hits the clamp, scale the img element itself
                    const topAfterTranslate = gestureImgTop + clampedDelta;
                    const imgEl = content.querySelector<HTMLElement>('img.image-original')
                        ?? content.querySelector<HTMLElement>('img.image-plug')
                        ?? content.querySelector<HTMLElement>('video.video-original');
                    if (imgEl && topAfterTranslate > 0 && absDelta > gestureMaxUp && gestureImgHeight > 0) {
                        const extraDelta = absDelta - gestureMaxUp;
                        const growNeeded = topAfterTranslate;
                        const scaleProgress = Math.min(extraDelta / growNeeded, 1);
                        const scale = 1 + (topAfterTranslate / gestureImgHeight) * scaleProgress;
                        imgEl.style.transformOrigin = 'center bottom';
                        imgEl.style.transform = `scale(${scale})`;
                    } else if (imgEl) {
                        imgEl.style.transform = '';
                        imgEl.style.transformOrigin = '';
                    }
                }
                const vc = getVideoControl();
                if (vc)
                    vc.style.transform = `translateY(${-clampedDelta}px)`;

                // Info panel slides up (slower — 2.5x threshold range)
                const infoPanelProgress = Math.min(absDelta / (dismissThreshold * 2.5), 1);
                if (this.infoPanel)
                    this.infoPanel.style.transform = `translateY(${100 * (1 - infoPanelProgress)}%)`;

                // Header/footer fade out 2x faster
                const fastFade = `${1 - Math.min(absDelta / 150, 1)}`;
                this.header.style.opacity = fastFade;
                if (this.footer)
                    this.footer.style.visibility = parseFloat(fastFade) < 0.01 ? 'hidden' : '';
                this.imageViewer.classList.remove('navigation-visible');
                this.imageViewer.classList.add('navigation-hidden');
            } else if (this.isInfoOpen && deltaY > 0) {
                // Swipe down while info is open — reverse all transforms proportionally
                const progress = Math.min(deltaY / dismissThreshold, 1);

                // Image translateY: from infoTranslateY back toward 0
                const content = getSlideContent();
                if (content) {
                    const currentTranslate = this.infoTranslateY * (1 - progress);
                    content.style.transform = `translateY(${currentTranslate}px)`;
                }

                // Image scale: from infoScale back toward 1
                if (this.infoScale > 1) {
                    const imgEl = content?.querySelector<HTMLElement>('img.image-original')
                        ?? content?.querySelector<HTMLElement>('img.image-plug')
                        ?? content?.querySelector<HTMLElement>('video.video-original');
                    if (imgEl) {
                        const currentScale = 1 + (this.infoScale - 1) * (1 - progress);
                        imgEl.style.transformOrigin = 'center bottom';
                        imgEl.style.transform = `scale(${currentScale})`;
                    }
                }

                // Info panel slides down
                if (this.infoPanel)
                    this.infoPanel.style.transform = `translateY(${100 * progress}%)`;

                // Header/footer fade back in
                const fade = `${Math.min(progress / 0.5, 1)}`;
                this.header.style.opacity = fade;
                if (this.footer)
                    this.footer.style.visibility = parseFloat(fade) > 0.5 ? '' : 'hidden';
            }

            // Keep thumbs locked during gesture
            // eslint-disable-next-line @typescript-eslint/no-unsafe-member-access
            if (savedThumbsTransform && this.thumbs?.wrapperEl)
                // eslint-disable-next-line @typescript-eslint/no-unsafe-member-access
                (this.thumbs.wrapperEl as HTMLElement).style.transform = savedThumbsTransform;
        };

        const finishGesture = (deltaY: number) => {
            restoreSwiper();
            const content = getSlideContent();
            const vc = getVideoControl();

            // Swipe UP while viewing → snap to final info-open state
            if (!this.isInfoOpen && deltaY < -dismissThreshold) {
                // Calculate final transform values
                const finalTranslateY = gestureMaxUp > 0 ? -gestureMaxUp : 0;
                let finalScale = 1;
                if (gestureImgHeight > 0) {
                    const topAtClamp = gestureImgTop + finalTranslateY;
                    if (topAtClamp > 0)
                        finalScale = 1 + topAtClamp / gestureImgHeight;
                }

                // Store for reverse animation
                this.infoTranslateY = finalTranslateY;
                this.infoScale = finalScale;

                // Animate content to final position
                if (content) {
                    content.style.transition = 'transform 200ms ease-out';
                    content.style.transform = `translateY(${finalTranslateY}px)`;
                }
                const imgEl = content?.querySelector<HTMLElement>('img.image-original')
                    ?? content?.querySelector<HTMLElement>('img.image-plug')
                    ?? content?.querySelector<HTMLElement>('video.video-original');
                if (imgEl && finalScale > 1) {
                    imgEl.style.transition = 'transform 200ms ease-out';
                    imgEl.style.transformOrigin = 'center bottom';
                    imgEl.style.transform = `scale(${finalScale})`;
                }
                if (vc) {
                    vc.style.transition = 'transform 200ms ease-out';
                    vc.style.transform = '';
                }

                setTimeout(() => {
                    if (content) content.style.transition = '';
                    if (imgEl) imgEl.style.transition = '';
                    if (vc) vc.style.transition = '';
                }, 250);

                resetDismiss();
                this.openInfoPanel();
                return;
            }

            // Swipe DOWN while info is open → finish closing info panel
            if (this.isInfoOpen && deltaY > dismissThreshold) {
                this.isInfoOpen = false;
                swiper.allowTouchMove = true;

                // Animate remaining distance
                if (content) {
                    content.style.transition = 'transform 200ms ease-out';
                    content.style.transform = '';
                    content.style.transformOrigin = '';
                }
                if (this.infoPanel) {
                    this.infoPanel.style.transition = 'transform 200ms ease-out';
                    this.infoPanel.style.transform = '';
                    this.infoPanel.classList.remove('is-open');
                }
                this.header.style.transition = 'opacity 200ms ease-out';
                this.header.style.opacity = '';
                this.header.style.pointerEvents = '';
                void this.safeCenterThumb().then(() => {
                    if (this.footer)
                        this.footer.style.visibility = '';
                });
                this.imageViewer.classList.remove('navigation-hidden');
                this.imageViewer.classList.add('navigation-visible');
                setTimeout(() => clearStyles(), 250);
                resetDismiss();
                return;
            }

            // Swipe DOWN while viewing → dismiss
            if (!this.isInfoOpen && deltaY >= dismissThreshold) {
                const targetY = window.innerHeight;
                if (content) {
                    content.style.transition = 'transform 200ms ease-out';
                    content.style.transform = `translateY(${targetY}px)`;
                }
                if (vc) {
                    vc.style.transition = 'transform 200ms ease-out';
                    vc.style.transform = `translateY(${-targetY}px)`;
                }
                modalFrame.style.transition = 'opacity 200ms ease-out';
                modalFrame.style.opacity = '0';
                setTimeout(() => {
                    void this.blazorRef.invokeMethodAsync('Close');
                }, 200);
                resetDismiss();
                return;
            }

            // Didn't reach threshold — snap back
            if (this.isInfoOpen) {
                // Snap back to info-open state
                if (content) {
                    content.style.transition = 'transform 200ms ease-out';
                    content.style.transform = `translateY(${this.infoTranslateY}px)`;
                }
                const imgEl = content?.querySelector<HTMLElement>('img.image-original')
                    ?? content?.querySelector<HTMLElement>('img.image-plug')
                    ?? content?.querySelector<HTMLElement>('video.video-original');
                if (imgEl && this.infoScale > 1) {
                    imgEl.style.transition = 'transform 200ms ease-out';
                    imgEl.style.transformOrigin = 'center bottom';
                    imgEl.style.transform = `scale(${this.infoScale})`;
                }
                if (this.infoPanel) {
                    this.infoPanel.style.transition = 'transform 200ms ease-out';
                    this.infoPanel.style.transform = 'translateY(0)';
                }
                // Keep header/footer hidden
                this.header.style.transition = 'opacity 200ms ease-out';
                this.header.style.opacity = '0';
                if (this.footer) {
                    this.footer.style.visibility = 'hidden';
                }
                setTimeout(() => {
                    if (content) content.style.transition = '';
                    if (imgEl) imgEl.style.transition = '';
                    if (this.infoPanel) this.infoPanel.style.transition = '';
                    this.header.style.transition = '';
                }, 250);
            } else {
                // Snap back to normal viewing state
                if (content) {
                    content.style.transition = 'transform 200ms ease-out';
                    content.style.transform = '';
                }
                if (vc) {
                    vc.style.transition = 'transform 200ms ease-out';
                    vc.style.transform = '';
                }
                modalFrame.style.transition = 'opacity 200ms ease-out';
                modalFrame.style.opacity = '';
                this.header.style.transition = 'opacity 200ms ease-out';
                this.header.style.opacity = '';
                if (this.footer) {
                    this.footer.style.visibility = '';
                }
                if (this.infoPanel) {
                    this.infoPanel.style.transition = 'transform 200ms ease-out';
                    this.infoPanel.style.transform = '';
                }
                this.imageViewer.classList.remove('navigation-hidden');
                this.imageViewer.classList.add('navigation-visible');
                setTimeout(() => clearStyles(), 250);
            }

            resetDismiss();
        };

        const onTouchEnd = (_e: TouchEvent) => {
            if (!this.isDismissing) {
                resetDismiss();
                return;
            }
            finishGesture(lastDeltaY);
        };

        const onTouchCancel = () => {
            if (this.isDismissing) {
                restoreSwiper();
                clearStyles();
            }
            resetDismiss();
        };

        this.overlay.addEventListener('touchstart', onTouchStart, { capture: true, passive: true });
        this.overlay.addEventListener('touchmove', onTouchMove, { capture: true, passive: false });
        this.overlay.addEventListener('touchend', onTouchEnd, { capture: true, passive: true });
        this.overlay.addEventListener('touchcancel', onTouchCancel, { capture: true, passive: true });

        this.disposed$.subscribe(() => {
            this.overlay.removeEventListener('touchstart', onTouchStart, { capture: true });
            this.overlay.removeEventListener('touchmove', onTouchMove, { capture: true });
            this.overlay.removeEventListener('touchend', onTouchEnd, { capture: true });
            this.overlay.removeEventListener('touchcancel', onTouchCancel, { capture: true });
        });
    }

    private onHideTransitionEnd() {
        if (!this.isHiding)
            return;

        this.isHiding = false;
        this.hideBlocked = false;
    }

    private async initThumbs() {
        ['imagesReady', 'resize', 'update'].forEach(event =>
            // eslint-disable-next-line
            this.thumbs.on(event, () => this.safeCenterThumb())
        );

        // eslint-disable-next-line
        this.thumbs.update();
        await this.safeCenterThumb();
    }

    private safeCenterThumb(attempt = 0): Promise<void> {
        return new Promise(resolve => {
            if (!this.swiper || !this.thumbs) {
                resolve();
                return;
            }

            // eslint-disable-next-line
            const index = this.swiper.realIndex ?? this.swiper.activeIndex;
            if (index === null || index === undefined) {
                resolve();
                return;
            }
            // eslint-disable-next-line
            const slide = this.thumbs.slides[index];
            if (!slide) {
                resolve();
                return;
            }

            // eslint-disable-next-line
            const swiperWidth = this.thumbs.width;
            // eslint-disable-next-line
            const slideLeft = slide.offsetLeft;
            // eslint-disable-next-line
            const slideWidth = slide.scrollWidth;

            if ((!swiperWidth || !slideWidth) && attempt < 10) {
                setTimeout(() => {
                    void this.safeCenterThumb(attempt + 1).then(resolve);
                }, 50);
                return;
            }

            if (!swiperWidth || !slideWidth) {
                resolve();
                return;
            }

            const target = slideLeft - (swiperWidth / 2) + (slideWidth / 2);

            // eslint-disable-next-line
            this.thumbs.setTranslate(-target);
            // eslint-disable-next-line
            this.thumbs.updateProgress();
            // eslint-disable-next-line
            this.thumbs.updateActiveIndex();
            // eslint-disable-next-line
            this.thumbs.updateSlidesClasses();
            // eslint-disable-next-line
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

        const headerHeight = this.isHeaderAndFooterVisible ? this.headerHeight : 0;

        // eslint-disable-next-line
        const videoWrapper = activeSlide.querySelector('.video-wrapper') as HTMLElement;
        // eslint-disable-next-line
        if (!videoWrapper)
            return;

        videoWrapper.style.setProperty('--header-height', `${headerHeight}px`);
    }

    private setInitialHideTimeout() {
        const cancelEvents = ['click', 'pointerdown'] as const;
        const cancel = () => {
            if (this.hideTimeoutId !== null) {
                clearTimeout(this.hideTimeoutId);
                this.hideTimeoutId = null;
            }
            for (const ev of cancelEvents)
                this.overlay.removeEventListener(ev, cancel, true);
        };

        for (const ev of cancelEvents)
            this.overlay.addEventListener(ev, cancel, { capture: true, once: true });

        this.hideTimeoutId = window.setTimeout(() => {
            this.hideTimeoutId = null;
            cancel();
            void this.hideHeaderAndFooter();
        }, 3000);
    }

    private async showHeaderAndFooter() {
        if (this.hideBlocked || this.isHiding)
            return;

        if (this.isHeaderAndFooterVisible)
            return;

        if (Date.now() < this.hideLockedUntil)
            return;

        const footer = this.footer;
        const header = this.header;
        const viewer = this.imageViewer;
        // eslint-disable-next-line
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

        if (this.thumbs && !this.isDismissing && !this.isInfoOpen) {
            setTimeout(() => {
                // eslint-disable-next-line @typescript-eslint/no-unsafe-call,@typescript-eslint/no-unsafe-member-access
                this.thumbs.update();
            }, 50);
        }
    }

    private async hideHeaderAndFooter() {
        if (!this.isHeaderAndFooterVisible)
            return;

        this.hideLockedUntil = Date.now() + 250;

        const footer = this.footer;
        const header = this.header;
        const viewer = this.imageViewer;
        // eslint-disable-next-line @typescript-eslint/no-unsafe-assignment,@typescript-eslint/no-unnecessary-condition
        const canContinue = (!this.thumbsEl && header && viewer) || (this.thumbsEl && footer && header && viewer);
        if (!canContinue)
            return;

        this.isHeaderAndFooterVisible = false;
        this.isHiding = true;
        this.hideBlocked = true;

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

        if (this.thumbs && !this.isDismissing && !this.isInfoOpen) {
            setTimeout(() => {
                // eslint-disable-next-line @typescript-eslint/no-unsafe-call,@typescript-eslint/no-unsafe-member-access
                this.thumbs.update();
            }, 50);
        }
    }

    private toggleHeaderAndFooterVisibility() {
        if (this.isHiding || this.hideBlocked)
            return;

        if (this.isHeaderAndFooterVisible) {
            void this.hideHeaderAndFooter();
        } else {
            void this.showHeaderAndFooter();
        }
    }

    // Event handlers

    private onZoomedSlideSwipe(swiper: Swiper, event: PointerEvent) {
        if (swiper.zoom.scale > 1) {
            // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
            if (!swiper)
                return;

            const activeIndex = swiper.activeIndex;
            const activeSlide = swiper.slides[activeIndex];
            const image = activeSlide.querySelector('img')!;
            const prevButton = swiper.navigation.prevEl;
            const nextButton = swiper.navigation.nextEl;

            // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
            if (prevButton && prevButton.contains(event.target as Node) && !prevButton.classList.contains('.swiper-button-disabled'))
                return;

            // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
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

    private onClick(event: PointerEvent | MouseEvent): void {
        if (this.isDismissing) return;
        if (this.isDoubleClick) {
            this.isDoubleClick = false;
            return;
        }

        const target = event.target as HTMLElement;
        if (target.closest('.video-control'))
            return; // let video controls handle their own clicks
        if (target.closest('.image-viewer-header') || target.closest('.image-viewer-footer'))
            return; // let header/footer handle their own clicks

        if (target.classList.contains('media-swiper')) {
            // click on prev / next buttons
        } else if (target.classList.contains('swiper-zoom-container')
            || target.classList.contains('video-wrapper')) {
            // click outside image/video
            void this.blazorRef.invokeMethodAsync('Close');
        } else {
            // click on image/video — toggle tools visibility
            this.toggleHeaderAndFooterVisibility();
        }
    }

    private wireNewMedia(): void {
        const videos = this.imageViewer.querySelectorAll<HTMLVideoElement>('video.video-original');
        videos.forEach(video => {
            if (this.wiredMedia.has(video))
                return;

            this.wiredMedia.add(video);
            this.setMaxSize(video);
            this.addVideoListeners(video);
            this.videoPlugHandler(video);
        });
        this.videos = videos;
        const containers = this.imageViewer.querySelectorAll<HTMLElement>('.image-container');
        containers.forEach(container => {
            if (this.wiredMedia.has(container))
                return;

            this.wiredMedia.add(container);
            this.addImageListeners(container);
        });
        this.imageContainers = containers;
    }

    private async onSlideChange(swiper: Swiper): Promise<void> {
        this.updateVideoPlayback();
        if (this.maxVideoWidth != 0 || this.maxVideoHeight != 0 || this.videoRatio != 0)
            this.maxVideoWidth = this.maxVideoHeight = this.videoRatio = 0;
        if (this.isReindexing)
            return;

        await this.blazorRef.invokeMethodAsync('SlideChanged', swiper.activeIndex);
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
                    void video.play()
                        .then(() => {
                            this.hideSpinner(video);
                            this.fixVideoPosition();
                            const control = video.parentElement!.querySelector('.video-control');
                            if (control?.classList.contains('hide-control')) {
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

    private calculateSize(
        originalWidth: number,
        originalHeight: number,
        screenWidth: number = window.innerWidth,
        screenHeight: number = window.innerHeight): { width: number; height: number } {
        const originalRatio = originalWidth / originalHeight;

        let width = originalWidth;
        let height = originalHeight;

        if (screenWidth < width) {
            width = screenWidth;
            height = width / originalRatio;
        }

        if (screenHeight < height) {
            height = screenHeight;
            width = height * originalRatio;
        }

        return { width, height };
    }

    private videoPlugHandler(video: HTMLMediaElement) {
        const wrapper = video.closest('.video-wrapper')!;
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (!wrapper)
            return;

        const thumbnailWrapper = wrapper.querySelector('.video-thumbnail-wrapper')!;
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (!thumbnailWrapper)
            return;

        const thumbnail = wrapper.querySelector('.video-thumbnail')!;
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (!thumbnail)
            return;

        const spinner = wrapper.querySelector('.spinner-icon-wrapper')!;

        if (video.readyState == 4) {
            thumbnailWrapper.remove();
            // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
            if (spinner)
                spinner.remove();
            return;
        }

        const imagePlug = thumbnail.querySelector('image-skeleton')!;
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (imagePlug) {
            // @ts-expect-error TODO(Andrey) fix eslint error
            // eslint-disable-next-line @typescript-eslint/no-unsafe-member-access
            const originalWidth = Number(thumbnail.dataset.width);
            // @ts-expect-error TODO(Andrey) fix eslint error
            // eslint-disable-next-line @typescript-eslint/no-unsafe-member-access
            const originalHeight = Number(thumbnail.dataset.height);
            const { width, height } = this.calculateSize(originalWidth, originalHeight);

            // @ts-expect-error TODO(Andrey) fix eslint error
            // eslint-disable-next-line @typescript-eslint/no-unsafe-member-access,@typescript-eslint/restrict-plus-operands
            thumbnail.style.width = width + 'px';
            // @ts-expect-error TODO(Andrey) fix eslint error
            // eslint-disable-next-line @typescript-eslint/no-unsafe-member-access,@typescript-eslint/restrict-plus-operands
            thumbnail.style.height = height + 'px';
        }
    }

    private addVideoListeners(video: HTMLMediaElement) {
        const control = video.parentElement!.querySelector('.video-control')!;
        const playBtn = control.querySelector('.play-btn')!;
        const rewindBtn = control.querySelector('.rewind-btn')!;
        const forwardBtn = control.querySelector('.forward-btn')!;
        const progressBar = control.querySelector('progress')!;
        const thumb = control.querySelector('.c-thumb')!;
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (!control || !playBtn || !rewindBtn || !forwardBtn || !progressBar || !thumb) {
            // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
            if (control)
                control.remove();
            video.controls = true;
        }

        fromEvent(video, 'loadeddata')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: Event) => this.onVideoLoaded(event, video));

        fromEvent(video, 'play')
            .pipe(takeUntil(this.disposed$))
            // @ts-expect-error TODO(Andrey) fix eslint error
            .subscribe((event: Event) => this.playAndPauseHandler(event, playBtn));

        fromEvent(video, 'pause')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: Event) => {
                // @ts-expect-error TODO(Andrey) fix eslint error
                this.playAndPauseHandler(event, playBtn);
                void this.blazorRef.invokeMethodAsync('OnVideoPlaybackStopped');
            });

        fromEvent(video, 'ended')
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => {
                void this.blazorRef.invokeMethodAsync('OnVideoPlaybackStopped');
            });

        fromEvent(video, 'timeupdate')
            .pipe(takeUntil(this.disposed$))
            // @ts-expect-error TODO(Andrey) fix eslint error
            .subscribe(() => this.updateTimeline(video, control, progressBar));

        fromEvent(playBtn, 'click')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: PointerEvent | MouseEvent) => this.onPlayBtnClick(event, video));

        fromEvent(rewindBtn, 'click')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: PointerEvent | MouseEvent) => this.onJumpBtnClick(event, video, false));

        fromEvent(forwardBtn, 'click')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: PointerEvent | MouseEvent) => this.onJumpBtnClick(event, video, true));

        this.dragThumb(video, progressBar);
    }

    private dragThumb(video: HTMLMediaElement, progressBar: HTMLProgressElement) {
        let rect: DOMRect | null = null;
        const line2 = progressBar.closest('.c-line-2');

        const updateThumbPosition = (percent: number) => {
            if (line2 instanceof HTMLElement)
                line2.style.setProperty('--progress', String(percent / 100));
            progressBar.value = percent;
        };

        const onLinePointerDown = (e: PointerEvent) => {
            e.preventDefault();
            e.stopPropagation();

            rect = progressBar.getBoundingClientRect();
            const offsetX = e.clientX - rect.left;
            let percent = (offsetX / rect.width) * 100;
            percent = Math.max(0, Math.min(100, percent));

            updateThumbPosition(percent);
            this.isDraggingThumb = true;
            line2?.classList.add('thumb-active');
            document.body.classList.add('no-select');
        };

        const onPointerMove = (e: PointerEvent) => {
            if (!this.isDraggingThumb || !rect)
                return;

            const offsetX = e.clientX - rect.left;
            let percent = (offsetX / rect.width) * 100;
            percent = Math.max(0, Math.min(100, percent));
            updateThumbPosition(percent);
        };

        const onPointerUp = (e: PointerEvent) => {
            if (!this.isDraggingThumb) return;

            if (rect && Number.isFinite(video.duration) && video.duration > 0) {
                const offsetX = e.clientX - rect.left;
                let percent = (offsetX / rect.width) * 100;
                percent = Math.max(0, Math.min(100, percent));
                video.currentTime = (percent / 100) * video.duration;
            }

            setTimeout(() => {
                this.isDraggingThumb = false;
                line2?.classList.remove('thumb-active');
            }, 50);

            document.body.classList.remove('no-select');
        };

        const progressContainer = progressBar.closest('.c-line-2')!;
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (!progressContainer)
            return;

        fromEvent(progressContainer, 'pointerdown')
            .pipe(takeUntil(this.disposed$))
            .subscribe(onLinePointerDown);

        fromEvent(document, 'pointermove')
            .pipe(takeUntil(this.disposed$))
            .subscribe(onPointerMove);

        fromEvent(document, 'pointerup')
            .pipe(takeUntil(this.disposed$))
            .subscribe(onPointerUp);
    }

    private addImageListeners(container: HTMLElement) {
        const original = container.querySelector('.image-original')!;
        const plug = container.querySelector('.image-plug')!;
        const spinner = container.querySelector('.spinner-icon-wrapper');
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (!original)
            return;

        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (!plug)
            return;

        // @ts-expect-error TODO(Andrey) fix eslint error
        if (original.complete) {
            plug.remove();
            if (spinner)
                spinner.remove();
            return;
        }

        // @ts-expect-error TODO(Andrey) fix eslint error
        // eslint-disable-next-line @typescript-eslint/no-unsafe-member-access
        const originalWidth = Number(plug.dataset.width);
        // @ts-expect-error TODO(Andrey) fix eslint error
        // eslint-disable-next-line @typescript-eslint/no-unsafe-member-access
        const originalHeight = Number(plug.dataset.height);
        const { width } = this.calculateSize(originalWidth, originalHeight);
        // @ts-expect-error TODO(Andrey) fix eslint error
        plug.width = width;

        fromEvent(original, 'load')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: Event) => this.onImageLoaded(event, container));
    }

    private onImageLoaded(event: Event, container: HTMLElement) {
        const plug = container.querySelector('.image-plug')!;
        const spinner = container.querySelector('.spinner-icon-wrapper')!;
        plug.remove();
        spinner.remove();
    }

    private onVideoLoaded(event: Event, video: HTMLMediaElement) {
        const wrapper = video.closest('.video-wrapper')!;
        const thumbnailWrapper = wrapper.querySelector('.video-thumbnail-wrapper');
        const spinner = wrapper.querySelector('.spinner-icon-wrapper');
        const control = wrapper.querySelector('.video-control')!;
        if (thumbnailWrapper)
            thumbnailWrapper.remove();
        if (spinner)
            spinner.remove();
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (control) {
            control.classList.remove('invisible');
            control.classList.add('has-mini-progress');
        }
        this.updateLoading(video);
    }

    private updateLoading(video: HTMLMediaElement) {
        const wrapper = video.closest('.video-wrapper')!;
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (!wrapper)
            return;

        const control = wrapper.querySelector('.video-control')!;
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (!control)
            return;

        const progressBar = control.querySelector('.c-progress-bar')!;
        const loadedBar = window.getComputedStyle(progressBar, ':before');
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (!loadedBar)
            return;

        const duration = video.duration;
        const bufferId = setInterval(() => {
            const buffered = video.buffered.end(0);
            // @ts-expect-error TODO(Andrey) fix eslint error
            // eslint-disable-next-line @typescript-eslint/no-unsafe-assignment
            const maxWidth = progressBar.offsetWidth;
            if (buffered == duration) {
                clearInterval(bufferId);
                // @ts-expect-error TODO(Andrey) fix eslint error
                // eslint-disable-next-line @typescript-eslint/no-unsafe-call,@typescript-eslint/no-unsafe-member-access
                progressBar.style.setProperty('--data-media-loaded', `${maxWidth}px`);
                return;
            }
            const loadedValue = buffered / duration;
            // @ts-expect-error TODO(Andrey) fix eslint error
            // eslint-disable-next-line @typescript-eslint/no-unsafe-call,@typescript-eslint/no-unsafe-member-access
            progressBar.style.setProperty('--data-media-loaded', `${loadedValue * maxWidth}px`);
        }, 200);
    }

    private onUpdateProgressBarDebounced: (event: Event, video: HTMLVideoElement) => void = debounce(
        (event: Event, video: HTMLVideoElement) => this.updateProgressBar(event, video),
        200
    );

    private updateProgressBar(event: Event, video: HTMLVideoElement) {
        const wrapper = video.closest('.video-wrapper')!;
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (!wrapper)
            return;

        const control = wrapper.querySelector('.video-control')!;
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (!control)
            return;

        const progressBar = control.querySelector('.c-progress-bar')!;
        const loadedBar = window.getComputedStyle(progressBar, ':before');
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (!loadedBar)
            return;

        // @ts-expect-error TODO(Andrey) fix eslint error
        // eslint-disable-next-line @typescript-eslint/no-unsafe-assignment
        const maxWidth = progressBar.offsetWidth;
        // @ts-expect-error TODO(Andrey) fix eslint error
        // eslint-disable-next-line @typescript-eslint/no-unsafe-member-access,@typescript-eslint/no-unsafe-call
        progressBar.style.setProperty('--data-media-loaded', `${maxWidth}px`);
    }

    private updateTimeline(video: HTMLMediaElement, control: HTMLElement, progressBar: HTMLProgressElement) {
        const current = video.currentTime;
        const fraction = video.duration > 0 ? current / video.duration : 0;
        progressBar.value = fraction * 100;
        progressBar.innerHTML = `${Math.round(fraction * 100)}% played`;

        if (!this.isDraggingThumb) {
            const line2 = control.querySelector('.c-line-2');
            if (line2 instanceof HTMLElement)
                line2.style.setProperty('--progress', String(fraction));
        }
        const currentTimeDiv = control.querySelector('.c-current')!;
        currentTimeDiv.innerHTML = this.formatTime(current);
        const durationDiv = control.querySelector('.c-duration')!;
        durationDiv.innerHTML = this.formatTime(video.duration);
    }

    private formatTime(time: number) : string {
        let minutes = '';
        let seconds = '';
        const minNum = Math.floor((time / 60));
        const secNum = Math.round(time - (minNum * 60));
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
        if (video.paused) {
            void video.play();
        } else {
            video.pause();
        }
    }

    private onJumpBtnClick(
        event: PointerEvent | MouseEvent,
        video: HTMLMediaElement,
        forward: boolean) {
        event.stopPropagation();
        const timeDelta = forward ? this.jumpTime : -this.jumpTime;
        video.currentTime += timeDelta
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
