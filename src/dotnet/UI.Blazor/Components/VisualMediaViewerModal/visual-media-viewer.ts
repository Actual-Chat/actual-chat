import { preventDefaultForEvent } from 'event-handling';
import { fromEvent, Subject, takeUntil } from 'rxjs';

import { Log } from 'logging';
import { ScreenSize } from '../../Services/ScreenSize/screen-size';
import { setTimeout } from 'timerQueue';

const { debugLog } = Log.get('VisualMediaViewer');

class MoveState {
    imageRect: DOMRect;
    viewerRect: DOMRect;
    distance: number;

    constructor(
        imageRect = null,
        viewerRect = null,
        distance = 0) {
        this.imageRect = imageRect;
        this.viewerRect = viewerRect;
        this.distance = distance;
    }
}

export class VisualMediaViewer {
    private readonly disposed$: Subject<void> = new Subject<void>();
    private readonly overlay: HTMLElement;
    private media: HTMLElement;
    private readonly header: HTMLElement;
    private readonly footer: HTMLElement;
    private footerCarousel: HTMLElement;
    private nextButton: HTMLElement = null;
    private prevButton: HTMLElement = null;
    private mediaArray: HTMLElement[] = null;
    private movePoints: PointerEvent[] = null;
    private stopMovement: boolean = true;
    private readonly fadingRate: number = 1.05;
    private readonly stopInertialRate: number = 1;
    private speedX: number;
    private speedY: number;
    private readonly multiplier: number = 1.25;
    private startY: number = 0;
    private startX: number = 0;
    private deltaY: number = 0;
    private deltaX: number = 0;
    private prevY: number = 0;
    private prevX: number = 0;
    private touchStartCoords: number[] = [0, 0];
    private isMovementStarted: boolean = false;
    private startDistance: number = 0;
    private startImageRect: DOMRect;
    private startImageTop: number = 0;
    private startImageLeft: number = 0;
    private headerBottom: number = 0;
    private footerTop: number = 0;
    private isHeaderAndFooterVisible: boolean = true;
    private isHeaderAndFooterVisibilityForced: boolean = false;
    private points: PointerEvent[] = new Array<PointerEvent>();
    private minWidth: number = 100;
    private minHeight: number = 100;
    private readonly maxWidth: number = 0;
    private readonly maxHeight: number = 0;

    private curState: MoveState = new MoveState();
    private prevState: MoveState = new MoveState();

    static create(imageViewer: HTMLElement, blazorRef: DotNet.DotNetObject): VisualMediaViewer {
        return new VisualMediaViewer(imageViewer, blazorRef);
    }

    constructor(
        private readonly imageViewer: HTMLElement,
        private readonly blazorRef: DotNet.DotNetObject
    ) {
        this.onScreenSizeChange();
        ScreenSize.event$
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => this.onScreenSizeChange());

        this.media = imageViewer.querySelector('.active');
        if (this.media instanceof HTMLVideoElement)
            void (this.media as HTMLVideoElement).play();
        this.mediaArray = [...imageViewer.querySelectorAll<HTMLElement>(":scope .c-full-media")];
        for (const element of this.mediaArray) {
            const id = element.id;
            const cachedId = 'cached:' + id;
            if (element instanceof HTMLImageElement){
                const imageElement = element as HTMLImageElement;
                if (imageElement.complete && imageElement.naturalWidth !== 0) {
                    const cachedImageElement = document.getElementById(cachedId);
                    if (cachedImageElement)
                        cachedImageElement.remove();
                }
                else {
                    element.classList.add('hidden');
                    element.addEventListener('load', (e) => {
                        element.classList.remove('hidden');

                        const cachedImageElement = document.getElementById(cachedId);
                        if (cachedImageElement)
                            cachedImageElement.remove();
                    });
                }
            }
        }

        this.overlay = this.imageViewer.closest('.modal-overlay');
        this.header = this.overlay.querySelector('.image-viewer-header');
        this.headerBottom = this.round(this.header.getBoundingClientRect().bottom);
        this.footer = this.overlay.querySelector('.image-viewer-footer');
        this.footerTop = this.round(this.footer.getBoundingClientRect().top);
        setTimeout(() => {
            this.hideHeaderAndFooter();
            fromEvent(this.overlay, 'mousemove')
                .pipe(takeUntil(this.disposed$))
                .subscribe((event: MouseEvent) => this.onMouseMove(event));
        }, 3000);
        this.maxHeight = window.innerHeight * 3;
        this.maxWidth = window.innerWidth * 3;

        if (this.getOriginWidthAndHeight() == false)
            return;

        let vh = window.innerHeight * 0.01;
        document.documentElement.style.setProperty('--vh', `${vh}px`);

        this.initCarousels();

        if (this.mediaArray != null && this.mediaArray.length > 1) {
            this.prevButton = this.overlay.querySelector('.c-previous');
            this.nextButton = this.overlay.querySelector('.c-next');
            this.controlButtonsVisibilityToggle();
            this.prevButton.classList.remove('invisible');
            this.nextButton.classList.remove('invisible');

            fromEvent(this.prevButton, 'pointerdown')
                .pipe(takeUntil(this.disposed$))
                .subscribe((event: PointerEvent) => this.onPreviousButtonClick(event));

            fromEvent(this.nextButton, 'pointerdown')
                .pipe(takeUntil(this.disposed$))
                .subscribe((event: PointerEvent) => this.onNextButtonClick(event));
        }

        fromEvent(this.overlay, 'wheel')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: WheelEvent) => this.onWheel(event));

        fromEvent(window, 'pointerdown')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: PointerEvent) => this.onPointerDown(event));

        fromEvent(window, 'pointerup')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: PointerEvent) => this.onPointerUp(event));

        fromEvent(window, 'pointercancel')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: PointerEvent) => this.onPointerUp(event));

        fromEvent(document, 'keydown')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: KeyboardEvent) => this.onKeyDown(event));
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private round = (value: number) : number => {
        return Math.round(value);
    }

    private logInfo(text: string) {
        return this.blazorRef.invokeMethodAsync('LogJS', text);
    }

    private onScreenSizeChange() {
        let vh = window.innerHeight * 0.01;
        document.documentElement.style.setProperty('--vh', `${vh}px`);
        this.getOriginWidthAndHeight();
    }

    private getOriginWidthAndHeight() : boolean {
        if (this.media == null)
            return false;
        let tagName = this.media.tagName.toLowerCase();
        if (tagName == 'img' || tagName == 'video') {
            let screenHeight = this.footerTop - this.headerBottom;
            if (ScreenSize.isNarrow()) {
                screenHeight = window.innerHeight;
            }
            let screenWidth = window.innerWidth;
            let mediaHeight = this.round(Number(this.media.getAttribute('height')));
            let mediaWidth = this.round(Number(this.media.getAttribute('width')));
            let multiplier = 1;

            if (mediaWidth > screenWidth) {
                multiplier = screenWidth / mediaWidth;
                mediaWidth = screenWidth;
                mediaHeight = mediaHeight * multiplier;
            }
            if (mediaHeight > screenHeight) {
                multiplier = screenHeight / mediaHeight;
                mediaHeight = screenHeight;
                mediaWidth = mediaWidth * multiplier;
            }
            if (mediaWidth == 0 || mediaHeight == 0) {
                [mediaWidth, mediaHeight] = this.zeroSizeHandler(mediaWidth, mediaHeight);
            }
            if (mediaWidth != this.round(Number(this.media.getAttribute('width')))) {
                mediaWidth = this.round(mediaWidth);
                mediaHeight = this.round(mediaHeight);
                this.media.setAttribute('width', `${mediaWidth}`);
                this.media.setAttribute('height', `${mediaHeight}`);
            }
            this.media.style.width = this.imageViewer.style.width = mediaWidth + 'px';
            this.media.style.height = this.imageViewer.style.height = mediaHeight + 'px';
            this.centerMedia(mediaWidth, mediaHeight);
            this.imageViewer.classList.remove('invisible');
            this.minWidth = Math.round(mediaWidth * 0.8);
            this.minHeight = Math.round(mediaHeight * 0.8);
            return true;
        } else {
            return false;
        }
    }

    private zeroSizeHandler(width: number, height: number) : [newWidth: number, newHeight: number] {
        let h = this.footerTop - this.headerBottom;
        let w = window.innerWidth;
        return [this.round(w * 0.75), this.round(h * 0.75)];
    }

    private centerMedia(width: number, height: number) {
        let screenWidth = window.innerWidth;
        let screenHeight = window.innerHeight;
        let left = (screenWidth - width) / 2;
        let top = (screenHeight - height) / 2;
        this.imageViewer.style.left = left + 'px';
        this.imageViewer.style.top = top + 'px';
    }

    private initCarousels() {
        this.footerCarousel = this.footer.querySelector('.footer-gallery');
        let allFooterMedia = this.footerCarousel.querySelectorAll('.active, .inactive');
        if (!this.mediaArray || this.mediaArray.length < 2)
            return;

        fromEvent(allFooterMedia, 'pointerdown')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: PointerEvent) => this.onFooterMediaItemPointerDown(event));
    }

    private hideHeaderAndFooter() {
        if (!this.isHeaderAndFooterVisible)
            return;

        this.isHeaderAndFooterVisible = false;
        this.header.classList.remove('hide-to-show');
        this.header.classList.add('show-to-hide');
        this.footer.classList.remove('hide-to-show');
        this.footer.classList.add('show-to-hide');
    }

    private showHeaderAndFooter() {
        if (this.isHeaderAndFooterVisible)
            return;

        this.isHeaderAndFooterVisible = true;
        this.header.classList.remove('show-to-hide');
        this.header.classList.add('hide-to-show');
        this.footer.classList.remove('show-to-hide');
        this.footer.classList.add('hide-to-show');
    }

    private toggleFooterHeaderVisibility() {
        this.isHeaderAndFooterVisibilityForced = true;
        if (this.isHeaderAndFooterVisible) {
            this.hideHeaderAndFooter();
        } else {
            this.showHeaderAndFooter();
        }
    }

    private centerImageX = (oldViewerRect: DOMRect) => {
        const viewerRect = this.imageViewer.getBoundingClientRect();
        const viewerLeft = this.round(viewerRect.left);
        const viewerWidth = this.round(viewerRect.width);
        const windowWidth = window.innerWidth;
        let left = viewerLeft;

        if (viewerWidth < windowWidth
            || viewerWidth == windowWidth
            || oldViewerRect.width == windowWidth) {
            left = this.round((windowWidth - viewerWidth) / 2);
        } else {
            let oldLeftOffsetPercentage = oldViewerRect.left / (oldViewerRect.width - windowWidth);
            let viewerXOffset = viewerWidth - windowWidth;
            left = this.round(viewerXOffset * oldLeftOffsetPercentage);
        }
        this.imageViewer.style.left = left + 'px';
    }

    private centerImageY = (oldViewerRect: DOMRect) => {
        const viewerRect = this.imageViewer.getBoundingClientRect();
        const viewerTop = this.round(viewerRect.top);
        const viewerHeight = this.round(viewerRect.height);
        const windowHeight = window.innerHeight;
        let top = viewerTop;

        if (viewerHeight < windowHeight
            || (viewerHeight / windowHeight <= 1.03 && viewerHeight / windowHeight >= 0.97)
            || (oldViewerRect.height / windowHeight <= 1.03 && oldViewerRect.height / windowHeight >= 0.97)) {
            top = this.round((windowHeight - viewerHeight) / 2);
        } else {
            let oldTopOffsetPercentage = oldViewerRect.top / (oldViewerRect.height - windowHeight);
            let viewerYOffset = viewerHeight - windowHeight;
            top = this.round(viewerYOffset * oldTopOffsetPercentage);
        }
        this.imageViewer.style.top = top + 'px';
    }

    private getImageAndMouseX = (event: MouseEvent, rect: DOMRect) => {
        this.startX = event.clientX;
        this.startImageLeft = this.round(rect.left);
        this.deltaX = this.startX - this.startImageLeft;
    }

    private getImageAndMouseY = (event: MouseEvent, rect: DOMRect) => {
        this.startY = event.clientY;
        this.startImageTop = this.round(rect.top);
        this.deltaY = this.startY - this.startImageTop;
    }

    private getLeft = (event: MouseEvent, rect: DOMRect) : number => {
        let x = event.pageX;
        let roundLeft = this.round(rect.left);
        let roundRight = this.round(rect.right);
        let left = roundLeft;
        if (roundLeft >= 0 && roundRight <= window.innerWidth
            || roundLeft == 0 && rect.width > window.innerWidth && event.movementX >= 0
            || roundRight == window.innerWidth && rect.width > window.innerWidth && event.movementX <= 0) {
            left = roundLeft;
            this.getImageAndMouseX(event, rect);
        } else if (roundLeft > 0 && event.movementX > 0) {
            left = 0;
        } else if ((window.innerWidth - roundRight > 0) && event.movementX < 0) {
            left = window.innerWidth - rect.width;
        }
        else {
            left = x - this.deltaX;
        }
        return left;
    }

    private getTop = (event: MouseEvent, rect: DOMRect) : number => {
        let y = event.pageY;
        let roundTop = this.round(rect.top);
        let roundBottom = this.round(rect.bottom);
        let top = roundTop;
        if (roundTop >= 0 && roundBottom <= window.innerHeight
            || roundTop == 0 && rect.height > window.innerHeight && event.movementY >= 0
            || roundBottom <= window.innerHeight && rect.height > window.innerHeight && event.movementY <= 0) {
            top = roundTop;
            this.getImageAndMouseY(event, rect);
        } else if (roundTop > 0 && event.movementY >= 0) {
            top = 0;
        } else if (roundBottom < window.innerHeight && event.movementY <= 0) {
            top = window.innerHeight - rect.height;
        } else {
            top = y - this.deltaY;
        }
        return top;
    }

    private getDistance() : number {
        let x = Math.abs(this.points[0].clientX - this.points[1].clientX);
        let y = Math.abs(this.points[0].clientY - this.points[1].clientY);
        return Math.sqrt(x*x + y*y);
    }

    private removeEvent(event: PointerEvent) {
        const index = this.points.findIndex(
            (e) => e.pointerId === event.pointerId
        );
        this.points.splice(index, 1);
    }

    // Event handlers

    private onMouseMove = (event: MouseEvent) => {
        if (this.isHeaderAndFooterVisibilityForced === true)
            return;

        const { pageY } = event;
        const cursorInHeaderArea = pageY <= this.header.offsetHeight;
        const cursorInFooterArea = this.overlay.offsetHeight - pageY <= this.footer.offsetHeight;
        if (cursorInHeaderArea || cursorInFooterArea) {
            this.showHeaderAndFooter();
        } else {
            this.hideHeaderAndFooter();
        }
    };

    private onWheel = (event: WheelEvent) => {
        this.wheelAndKeyboardScale(event,event.deltaY < 0);
    };

    private onPointerDown = (event: PointerEvent) => {
        switch (event.pointerType) {
            case 'mouse':
                this.points.push(event);
                let target = event.target as HTMLElement;
                if (this.isRequiredClass(target)) {
                    const viewerRect = this.imageViewer.getBoundingClientRect();
                    const viewerTop = this.round(viewerRect.top);
                    const viewerBottom = this.round(viewerRect.bottom);
                    const viewerLeft = this.round(viewerRect.left);
                    const viewerRight = this.round(viewerRect.right);
                    if (viewerTop < 0 || viewerBottom > window.innerHeight || viewerLeft < 0 || viewerRight > window.innerWidth) {
                        this.getImageAndMouseX(event, viewerRect);
                        this.getImageAndMouseY(event, viewerRect);
                        window.addEventListener('pointermove', this.onPointerMove)
                    } else {
                        preventDefaultForEvent(event);
                    }
                }
                break;
            case 'touch':
                this.stopMovement = true;
                this.movePoints = null;
                this.media.style.touchAction = 'none';
                this.imageViewer.style.touchAction = 'none';
                this.overlay.style.touchAction = 'none';
                this.points.push(event);
                this.touchStartCoords = [event.x, event.y];
                this.startImageRect = this.media.getBoundingClientRect();
                let imageRect = this.startImageRect;
                let viewerRect = this.imageViewer.getBoundingClientRect();
                this.curState = new MoveState(imageRect, viewerRect, 0);
                this.prevState = new MoveState(imageRect, viewerRect, 0);
                window.addEventListener('pointermove', this.onTouchableMove);
                if (this.points.length === 2) {
                    this.startDistance = this.getDistance();
                } else if (this.points.length === 1) {
                    this.startY = event.pageY;
                    this.startX = event.pageX;
                    this.prevY = this.startY;
                    this.prevX = this.startX;
                }
                break;
            default:
                break;
        }
    };

    private isRequiredClass(target: HTMLElement) : boolean {
        return target.classList.contains('image-viewer-content')
            || target.classList.contains('image-container')
            || target.classList.contains('video-container');
    }

    private onPointerMove = (event: PointerEvent) => {
        preventDefaultForEvent(event);
        let rect = this.imageViewer.getBoundingClientRect();
        let topPosition = this.getTop(event, rect);
        let leftPosition = this.getLeft(event, rect);
        this.imageViewer.style.top = topPosition + 'px';
        this.imageViewer.style.left = leftPosition + 'px';
    };

    private onPointerUp = (event: PointerEvent) => {
        window.removeEventListener('pointermove', this.onPointerMove);
        window.removeEventListener('pointermove', this.onTouchableMove);
        this.stopMovement = false;
        if (event.pointerType === 'touch') {
            if (this.points.length === 1) {
                let target = event.target as HTMLElement;
                let savedEvent = this.points.find(e => e.pointerId == event.pointerId);
                if (savedEvent != null
                    && (event.timeStamp - savedEvent.timeStamp < 500)
                    && this.isSameTouchCoords(event)) {
                    if (this.isRequiredClass(target)) {
                        this.toggleFooterHeaderVisibility();
                    } else if (!this.footer.contains(target) && !this.header.contains(target)) {
                        this.blazorRef.invokeMethodAsync('Close');
                    }
                }
                this.isMovementStarted = false;
                if (this.curState.imageRect.width > window.innerWidth || this.curState.imageRect.height > window.innerHeight) {
                    this.inertialMotionHandler(event);
                }
            }
            this.removeEvent(event);

            let imageWidth = this.round(this.media.getBoundingClientRect().width);
            let imageHeight = this.round(this.media.getBoundingClientRect().height);
            if (imageWidth < this.minWidth && imageHeight < this.minHeight) {
                this.media.style.width = this.minWidth + 'px';
                this.imageViewer.style.width = this.minWidth + 'px';
                let rect = this.media.getBoundingClientRect();
                this.imageViewer.style.left = ((window.innerWidth - rect.width) / 2) + 'px';
                this.imageViewer.style.top = ((window.innerHeight - rect.height) / 2) + 'px';
            }
        } else if (event.pointerType === 'mouse') {
            let savedEvent = this.points.find(e => e.pointerId == event.pointerId);
            let target = event.target as HTMLElement;
            let savedTarget = savedEvent.target as HTMLElement;

            if ((event.timeStamp - savedEvent.timeStamp < 500) && this.isSameCoords(event, savedEvent)) {
                if (this.isRequiredClass(target)) {
                    this.toggleFooterHeaderVisibility();
                } else if (this.isClickToClose(target, savedTarget)) {
                    this.blazorRef.invokeMethodAsync('Close');
                }
            }
            this.removeEvent(event);
        }
        this.curState.imageRect = this.curState.viewerRect = this.prevState.imageRect = this.prevState.viewerRect = this.media.getBoundingClientRect();
    };

    private onKeyDown(event: KeyboardEvent): void {
        if (event.ctrlKey) {
            if (event.key == "ArrowDown") {
                this.wheelAndKeyboardScale(event, false);
            } else if (event.key == "ArrowUp") {
                this.wheelAndKeyboardScale(event, true);
            }
        } else {
            if (event.key == "ArrowDown" || event.key == "PageDown" || event.key == "ArrowRight") {
                this.getNextMedia();
            } else if (event.key == "ArrowUp" || event.key == "PageUp" || event.key == "ArrowLeft") {
                this.getPreviousMedia();
            } else {
                return;
            }
        }
    }

    private getPreviousMedia() {
        let currentMediaIndex = this.mediaArray.indexOf(this.media);
        if (currentMediaIndex == 0)
            return;
        let newMedia = this.mediaArray[currentMediaIndex - 1];
        let footerMedia = this.footer.querySelector('.gallery-item.active') as HTMLElement;
        this.changeMedia(footerMedia, newMedia, newMedia);
    }

    private getNextMedia() {
        let currentMediaIndex = this.mediaArray.indexOf(this.media);
        if (currentMediaIndex == this.mediaArray.length - 1)
            return;
        let newMedia = this.mediaArray[currentMediaIndex + 1];
        let footerMedia = this.footer.querySelector('.gallery-item.active') as HTMLElement;
        this.changeMedia(footerMedia, newMedia, newMedia);
    }

    private isClickToClose(target: HTMLElement, savedTarget: HTMLElement) : boolean {
        if (this.footer.contains(target))
            return false;
        else if (this.header.contains(target))
            return false;
        else if (this.prevButton != null && (this.prevButton.contains(target) || this.prevButton.contains(savedTarget)))
            return false;
        else if (this.nextButton != null && (this.nextButton.contains(target) || this.nextButton.contains(savedTarget)))
            return false;
        return true;
    }

    private onTouchableMove = (event: PointerEvent) => {
        if (!this.isMovementStarted)
            this.isMovementStarted = true;
        preventDefaultForEvent(event);
        const index = this.points.findIndex(
            (e) => e.pointerId === event.pointerId
        );
        this.points[index] = event;

        let windowWidth = window.innerWidth;
        let windowHeight = window.innerHeight;

        if (this.points.length === 2) {
            // two touches (zoom)
            this.curState.distance = this.getDistance();
            let scale = this.curState.distance / this.startDistance;
            let curImageRect = this.curState.imageRect;
            let isTooBig = curImageRect.width >= this.maxWidth || curImageRect.height >= this.maxHeight;
            if (isTooBig && this.curState.distance >= this.prevState.distance) {
                // don't enlarge image if it is too big
                this.startDistance = this.curState.distance;
                this.startImageRect = curImageRect;
            } else {
                this.scaleImage(scale);
            }
            this.prevState.distance = this.curState.distance;
            this.prevState.imageRect = this.prevState.viewerRect = this.curState.imageRect;
            this.curState.imageRect = this.curState.viewerRect = this.media.getBoundingClientRect();
        } else if (this.points.length === 1 && this.imageViewer.contains(event.target as HTMLElement)) {
            // one touch on image (move image)
            let viewer = this.imageViewer;
            if (this.canMoveImage(this.startImageRect)) {
                this.updateMovementPoints(event);
                let rect = this.media.getBoundingClientRect();
                let imageTop = this.round(rect.top);
                let imageRight = this.round(rect.right);
                let imageBottom = this.round(rect.bottom);
                let imageLeft = this.round(rect.left);
                let deltaX = this.round(event.pageX - this.startX);
                let deltaY = this.round(event.pageY - this.startY);

                if (rect.width > windowWidth) {
                    if (event.pageX > this.prevX) {
                        // move right
                        if (imageLeft >= 0) {
                            viewer.style.left = 0 + 'px';
                            this.startX = event.pageX;
                        } else {
                            viewer.style.left = this.round(this.startImageRect.left + deltaX) + 'px';
                        }
                    } else if (event.pageX < this.prevX) {
                        // move left
                        if (imageRight <= windowWidth) {
                            viewer.style.left = this.round(windowWidth - this.startImageRect.width) + 'px';
                            this.startX = event.pageX;
                        } else {
                            viewer.style.left = this.round(this.startImageRect.left + deltaX) + 'px';
                        }
                    }
                    this.prevX = event.pageX;
                }

                if (rect.height > windowHeight) {
                    if (event.pageY > this.prevY) {
                        // move down
                        if (imageTop >= 0 && imageTop < 16) {
                            viewer.style.top = (this.startImageRect.top - this.startImageRect.top) + 'px';
                            this.startY = event.pageY;
                        } else {
                            viewer.style.top = this.round(this.startImageRect.top + deltaY) + 'px';
                        }
                    } else if (event.pageY < this.prevY) {
                        // move up
                        if (imageBottom <= windowHeight && imageBottom > windowHeight - 16) {
                            viewer.style.top = this.round(windowHeight - this.startImageRect.height) + 'px';
                            this.startY = event.pageY;
                        } else {
                            viewer.style.top = this.round(this.startImageRect.top + deltaY) + 'px';
                        }
                    }
                    this.prevY = event.pageY;
                }
                this.prevState.imageRect = rect;
            }
        }
    }

    private updateMovementPoints(event: PointerEvent) {
        if (this.movePoints == null) {
            this.movePoints = new Array(event);
        } else {
            if (this.movePoints.length >= 10)
                this.movePoints.shift();
            if (this.movePoints[this.movePoints.length - 1].timeStamp != event.timeStamp)
                this.movePoints.push(event);
        }
    }

    private inertialMotionHandler(event: PointerEvent) {
        let moves = this.movePoints;
        if (moves == null)
            return;
        let startX = moves[0].x;
        let startY = moves[0].y;
        let startTime = moves[0].timeStamp;
        let endX = moves[moves.length - 1].x;
        let endY = moves[moves.length - 1].y;
        let endTime = moves[moves.length - 1].timeStamp;

        let timeDelta = endTime - startTime;
        if (timeDelta == 0)
            return;
        let deltaX = endX - startX;
        let deltaY = endY - startY;
        this.speedX = (deltaX / timeDelta) * 10;
        this.speedY = (deltaY / timeDelta) * 10;
        this.inertialMotion();
    }

    private inertialMotion() {
        let timer = setInterval(() => {
            let timeToStop = Math.abs(this.speedX) < this.stopInertialRate && Math.abs(this.speedY) < this.stopInertialRate;
            let isAnotherTouch = this.stopMovement;

            if (timeToStop || isAnotherTouch) {
                clearInterval(timer);
                this.movePoints = null;
                return;
            }

            let rect = this.curState.viewerRect;
            if (rect.width > window.innerWidth) {
                if (rect.left >= 0) {
                    clearInterval(timer);
                    this.imageViewer.style.left = 0 + 'px';
                    return;
                }
                if (rect.right <= window.innerWidth) {
                    clearInterval(timer);
                    this.imageViewer.style.left = (window.innerWidth - rect.width) + 'px';
                    return;
                }
            } else {
                this.speedX = 0;
            }
            if (rect.height >= window.innerHeight) {
                if (rect.top > 0) {
                    clearInterval(timer);
                    this.imageViewer.style.top = 0 + 'px';
                    return;
                }
                if (rect.bottom <= window.innerHeight) {
                    clearInterval(timer);
                    this.imageViewer.style.top = (window.innerHeight - rect.height) + 'px';
                    return;
                }
            } else {
                this.speedY = 0;
            }
            this.moveImageViewer();
        }, 20);
    }

    private moveImageViewer() {
        let rect = this.curState.viewerRect;
        this.speedX = this.speedX / this.fadingRate;
        this.speedY = this.speedY / this.fadingRate;
        this.imageViewer.style.left = (rect.left + this.speedX) + 'px';
        this.imageViewer.style.top = (rect.top + this.speedY) + 'px';
        this.curState.viewerRect = this.imageViewer.getBoundingClientRect();
        this.curState.imageRect = this.media.getBoundingClientRect();
    }

    private scaleImage(scale: number) {
        this.prevState.imageRect = this.prevState.viewerRect = this.curState.imageRect;
        let newImageWidth = this.round(this.startImageRect.width * scale);
        let newImageHeight = this.round(this.startImageRect.height * scale);
        if (newImageHeight < this.minHeight && newImageWidth < this.minWidth) {
            return;
        }
        this.media.style.width = this.imageViewer.style.width = newImageWidth + 'px';
        this.media.style.height = this.imageViewer.style.height = newImageHeight + 'px';
        this.curState.imageRect = this.curState.viewerRect = this.media.getBoundingClientRect();
        this.centerImage();
        if (this.curState.imageRect.height > this.footerTop - this.headerBottom) {
            this.hideHeaderAndFooter();
        }
    }

    private canMoveImage(rect: DOMRect) : boolean {
        return rect.top < 0 || rect.right > window.innerWidth || rect.bottom > window.innerHeight || rect.left < 0;
    }

    private centerImage() {
        let curImageRect = this.curState.imageRect;
        let prevImageRect = this.prevState.imageRect;
        const imageLeft = this.round(curImageRect.left);
        const imageRight = this.round(curImageRect.right);
        const imageWidth = this.round(curImageRect.width);
        const windowWidth = window.innerWidth;
        const imageTop = this.round(curImageRect.top);
        const imageBottom = this.round(curImageRect.bottom);
        const imageHeight = this.round(curImageRect.height);
        const windowHeight = window.innerHeight;

        let left = imageLeft;
        let top = imageTop;
        let deltaX = (this.round(prevImageRect.width) - imageWidth) / 2;
        let deltaY = (this.round(prevImageRect.height) - imageHeight) / 2;
        let isScaleUp = curImageRect.width > prevImageRect.width;

        // center x
        if (curImageRect.width >= windowWidth && !isScaleUp) {
            left = prevImageRect.left + deltaX;
            if (curImageRect.left >= 0) {
                left = 0;
            } else if (curImageRect.right <= windowWidth) {
                left = windowWidth - curImageRect.width;
            }
        } else {
            if ((windowWidth / 2 - imageLeft) != (imageRight - windowWidth / 2)) {
                left = (this.prevState.imageRect.left + deltaX);
            }
        }

        // center y
        if (curImageRect.height >= windowHeight && !isScaleUp) {
            top = (this.prevState.imageRect.top + deltaY);
            if (curImageRect.top > 0) {
                top = 0;
            } else if (curImageRect.bottom <= windowHeight) {
                top = windowHeight - curImageRect.height;
            }
        } else {
            if ((windowHeight / 2 - imageTop) != (imageBottom - windowHeight / 2)) {
                top = (this.prevState.imageRect.top + deltaY);
            }
        }

        this.prevState.imageRect = curImageRect;
        this.imageViewer.style.left = left + 'px';
        this.imageViewer.style.top = top + 'px';
        this.curState.imageRect = this.curState.viewerRect = this.media.getBoundingClientRect();
    }

    private isSameCoords(event1: PointerEvent, event2: PointerEvent) : boolean {
        let result = false;
        let deltaX = Math.abs(event1.x - event2.x);
        let deltaY = Math.abs(event1.y - event2.y);
        if (deltaX < 10 && deltaY < 10)
            result = true;
        return result;
    }

    private isSameTouchCoords(event: PointerEvent) : boolean {
        let deltaX = Math.abs(event.x - this.touchStartCoords[0]);
        let deltaY = Math.abs(event.y - this.touchStartCoords[1]);
        return deltaX < 10 && deltaY < 10;
    }

    private controlButtonsVisibilityToggle() {
        if (this.mediaArray == null || this.mediaArray.length < 2)
            return;
        let currentMediaIndex = this.mediaArray.indexOf(this.media);
        if (currentMediaIndex == 0) {
            this.prevButton.classList.add('!hidden');
            this.nextButton.classList.remove('!hidden');
        } else if (currentMediaIndex == this.mediaArray.length - 1) {
            this.nextButton.classList.add('!hidden');
            this.prevButton.classList.remove('!hidden');
        } else {
            this.prevButton.classList.remove('!hidden');
            this.nextButton.classList.remove('!hidden');
        }
    }

    private onFooterMediaItemPointerDown = (event: PointerEvent) => {
        let newFooterMedia = event.currentTarget as HTMLElement;
        if (newFooterMedia == null || !newFooterMedia.classList.contains('gallery-item') || newFooterMedia.classList.contains('active'))
            return;
        let newMediaId = newFooterMedia.id;
        let newMedia = this.mediaArray.find(item => item.id == newMediaId) as HTMLElement;
        let footerMedia = this.footer.querySelector('.gallery-item.active') as HTMLElement;
        this.changeMedia(footerMedia, newFooterMedia, newMedia);
    }

    private onPreviousButtonClick = (event: PointerEvent) => {
        this.getPreviousMedia();
    }

    private onNextButtonClick = (event: PointerEvent) => {
        this.getNextMedia();
    }

    private changeMedia(footerMedia: HTMLElement, newFooterMedia: HTMLElement, newMedia: HTMLElement) {
        this.media.classList.replace('active', 'inactive');
        footerMedia.classList.replace('active', 'inactive');
        newMedia.classList.replace('inactive', 'active');
        newFooterMedia.classList.replace('inactive', 'active');
        if (this.media instanceof HTMLVideoElement)
            (this.media as HTMLVideoElement).pause();
        this.media = newMedia;
        if (this.media instanceof HTMLVideoElement)
            void (this.media as HTMLVideoElement).play();
        let newMediaId = newMedia.id;
        this.getOriginWidthAndHeight();
        this.controlButtonsVisibilityToggle();
        void this.blazorRef.invokeMethodAsync('ChangeMedia', newMediaId);
    }

    private wheelAndKeyboardScale(event: Event, scaleUp: boolean) {
        const viewerRect = this.imageViewer.getBoundingClientRect();
        const windowWidth = window.innerWidth;
        const windowHeight = window.innerHeight;
        const width = this.media.getBoundingClientRect().width;
        const height = this.media.getBoundingClientRect().height;
        let newWidth = width;
        let newHeight = height;
        preventDefaultForEvent(event);
        if (scaleUp) {
            newWidth = width * this.multiplier;
            newHeight = height * this.multiplier;
            if (newWidth / windowWidth <= 1.03 && newWidth / windowWidth >= 0.97) {
                newWidth = windowWidth;
            }
            if ((newHeight > windowHeight * 3 || newWidth > windowWidth * 3)) {
                return;
            } else {
                this.media.style.height = this.imageViewer.style.height = this.round(newHeight) + 'px';
                this.media.style.width = this.imageViewer.style.width = this.round(newWidth) + 'px';
                this.centerImageX(viewerRect);
                this.centerImageY(viewerRect);
            }
        } else {
            newWidth = width / this.multiplier;
            newHeight = height / this.multiplier;
            if (newWidth / windowWidth < 1.03 && newWidth / windowWidth > 0.97) {
                newWidth = windowWidth;
            }
            if (newWidth < 100 && newHeight < 100) {
                return;
            } else {
                this.media.style.height = this.imageViewer.style.height = this.round(newHeight) + 'px';
                this.media.style.width = this.imageViewer.style.width = this.round(newWidth) + 'px';
                this.centerImageX(viewerRect);
                this.centerImageY(viewerRect);
            }
        }
    }
}

