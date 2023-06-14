import { preventDefaultForEvent } from 'event-handling';
import { fromEvent, Subject, takeUntil, debounceTime } from 'rxjs';

import { Log } from 'logging';
import { setTimeout } from 'timerQueue';

const { debugLog } = Log.get('VisualMediaViewer');

export class VisualMediaViewer {
    private readonly disposed$: Subject<void> = new Subject<void>();
    private readonly overlay: HTMLElement;
    private readonly image: HTMLElement;
    private readonly header: HTMLElement;
    private readonly footer: HTMLElement;
    private readonly multiplier: number = 1.4;
    private startY: number = 0;
    private startX: number = 0;
    private deltaY: number = 0;
    private deltaX: number = 0;
    private prevY: number = 0;
    private prevX: number = 0;
    private readonly originImageWidth: number = 0;
    private readonly originImageHeight: number = 0;
    private movementStart: boolean = false;
    private startDistance: number = 0;
    private prevDistance: number = 0;
    private startImageRect: DOMRect;
    private prevImageRect: DOMRect;
    private startImageTop: number = 0;
    private startImageLeft: number = 0;
    private headerBottom: number = 0;
    private footerTop: number = 0;
    private isShowFooterAndHeader: boolean = false;
    private points: PointerEvent[] = new Array<PointerEvent>();
    private readonly minWidth: number = 0;
    private readonly minHeight: number = 0;
    private readonly maxWidth: number = 0;
    private readonly maxHeight: number = 0;

    static create(imageViewer: HTMLElement, blazorRef: DotNet.DotNetObject): VisualMediaViewer {
        return new VisualMediaViewer(imageViewer, blazorRef);
    }

    constructor(
        private readonly imageViewer: HTMLElement,
        private readonly blazorRef: DotNet.DotNetObject
    ) {
        this.image = imageViewer.querySelector('img');
        this.overlay = this.imageViewer.closest('.modal-overlay');
        this.header = this.overlay.querySelector('.image-viewer-header');
        this.footer = this.overlay.querySelector('.image-viewer-footer');
        this.footerHeaderToggle();
        this.originImageWidth = this.round(this.image.getBoundingClientRect().width);
        this.originImageHeight = this.round(this.image.getBoundingClientRect().height);
        this.maxHeight = window.innerHeight * 3;
        this.maxWidth = window.innerWidth * 3;
        this.minHeight = this.round(this.image.getBoundingClientRect().height);
        this.minWidth = this.round(this.image.getBoundingClientRect().width);

        fromEvent(window, 'wheel')
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

    private footerHeaderToggle() {
        this.isShowFooterAndHeader = !this.isShowFooterAndHeader;
        if (this.isShowFooterAndHeader) {
            this.footer.style.display = 'flex';
            this.header.style.display = 'flex';
        } else {
            this.footer.style.display = 'none';
            this.header.style.display = 'none';
        }
    }

    private centerImageX = (delta: number) => {
        const rect = this.imageViewer.getBoundingClientRect();
        const imageLeft = this.round(rect.left);
        const imageRight = this.round(rect.right);
        const imageWidth = this.round(rect.width);
        const windowWidth = window.innerWidth;
        if (imageWidth <= windowWidth * 1.2) {
            if ((windowWidth / 2 - imageLeft) != (imageRight - windowWidth / 2)) {
                let newImageLeft = (windowWidth - imageWidth) / 2;
                this.imageViewer.style.left = newImageLeft + 'px';
            }
        } else {
            if (imageLeft > 0) {
                this.imageViewer.style.left = 0 + 'px';
            } else if (imageRight < windowWidth) {
                this.imageViewer.style.left = imageLeft + (windowWidth - imageRight) + 'px';
            }
        }
    }

    private centerImageY = (delta: number) => {
        const rect = this.imageViewer.getBoundingClientRect();
        const imageTop = this.round(rect.top);
        const imageBottom = this.round(rect.bottom);
        const imageHeight = this.round(rect.height);
        const windowHeight = window.innerHeight;
        if (imageHeight <= windowHeight * 1.2) {
            if ((windowHeight / 2 - imageTop) != (imageBottom - windowHeight / 2)) {
                let newImageTop = (windowHeight - imageHeight) / 2;
                this.imageViewer.style.top = newImageTop + 'px';
            }
        } else {
            if (imageTop > this.headerBottom) {
                this.imageViewer.style.top = this.headerBottom + 'px';
            } else if (imageBottom < this.footerTop) {
                this.imageViewer.style.top = imageTop + (this.footerTop - imageBottom) + 'px';
            }
        }
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
        if (roundTop >= this.headerBottom && roundBottom <= this.footerTop
            || roundTop == this.headerBottom && rect.height > (window.innerHeight - this.headerBottom - this.footerTop) && event.movementY >= 0
            || roundBottom <= this.footerTop && rect.height > (window.innerHeight - this.headerBottom - this.footerTop) && event.movementY <= 0) {
            top = roundTop;
            this.getImageAndMouseY(event, rect);
        } else if (roundTop > this.headerBottom && event.movementY >= 0) {
            top = this.headerBottom;
        } else if (roundBottom < this.footerTop && event.movementY <= 0) {
            top = window.innerHeight - rect.height - this.footerTop;
        } else {
            top = y - this.deltaY;
        }
        return top;
    }

    private getDistance() : number {
        let x = Math.abs(this.points[0].clientX - this.points[1].clientX);
        let y = Math.abs(this.points[0].clientY - this.points[1].clientY);
        return this.round(Math.sqrt(x*x + y*y));
    }

    private removeEvent(event: PointerEvent) {
        const index = this.points.findIndex(
            (e) => e.pointerId === event.pointerId
        );
        this.points.splice(index, 1);
    }

    // Event handlers

    private onWheel = (event: WheelEvent) => {
        const delta = event.deltaY;
        const windowWidth = window.innerWidth;
        const windowHeight = window.innerHeight;
        const width = this.image.getBoundingClientRect().width;
        const height = this.image.getBoundingClientRect().height;
        preventDefaultForEvent(event);
        if (delta < 0) {
            // up
            let newWidth = width * this.multiplier;
            let newMaxWidth = width * this.multiplier;
            let newMaxHeight = height * this.multiplier;
            if ((newMaxHeight > windowHeight * 1.5 && newMaxWidth > windowWidth * 1.5)) {
                newWidth = newMaxWidth = width;
                newMaxHeight = height;
            }
            this.image.style.width = newWidth + 'px';
            this.image.style.maxHeight = newMaxHeight + 'px';
            this.image.style.maxWidth = newMaxWidth + 'px';
        } else {
            // down
            let newWidth = width / this.multiplier;
            let newMaxWidth = width / this.multiplier;
            let newMaxHeight = height / this.multiplier;
            if (newWidth < 80) {
                newWidth = 80;
                newMaxHeight = height;
                newMaxWidth = newWidth;
            }
            this.image.style.width = newWidth + 'px';
            this.image.style.maxHeight = newMaxHeight + 'px';
            this.image.style.maxWidth = newMaxWidth + 'px';
        }
        this.centerImageX(delta);
        this.centerImageY(delta);
    };

    private onPointerDown = (event: PointerEvent) => {
        if (event.pointerType == 'mouse' && this.imageViewer.contains(event.target as HTMLElement)) {
            const parent = this.imageViewer.parentElement;
            this.headerBottom = this.round(parent.querySelector('.image-viewer-header').getBoundingClientRect().bottom);
            this.footerTop = this.round(parent.querySelector('.image-viewer-footer').getBoundingClientRect().top);
            const imageRect = this.imageViewer.getBoundingClientRect();
            const imageTop = this.round(imageRect.top);
            const imageBottom = this.round(imageRect.bottom);
            const imageLeft = this.round(imageRect.left);
            const imageRight = this.round(imageRect.right);
            if (imageTop < this.headerBottom || imageBottom > this.footerTop || imageLeft < 0 || imageRight > window.innerWidth) {
                this.getImageAndMouseX(event, imageRect);
                this.getImageAndMouseY(event, imageRect);
                window.addEventListener('pointermove', this.onPointerMove);
            } else {
                preventDefaultForEvent(event);
            }
        } else if (event.pointerType == 'touch') {
            window.addEventListener('pointermove', this.onTouchableMove);
            this.image.style.touchAction = 'none';
            this.imageViewer.style.touchAction = 'none';
            this.overlay.style.touchAction = 'none';
            this.points.push(event);
            this.startImageRect = this.imageViewer.getBoundingClientRect();
            this.prevImageRect = this.startImageRect;
            if (this.points.length === 2) {
                this.startDistance = this.getDistance();
                this.prevDistance = this.getDistance();
            } else if (this.points.length === 1) {
                this.startY = event.pageY;
                this.startX = event.pageX;
                this.prevY = this.startY;
                this.prevX = this.startX;
                let target = event.target as HTMLElement;
                if (this.imageViewer.contains(target)) {
                    setTimeout(() => {
                        if (this.points.length < 2 && !this.movementStart)
                            this.footerHeaderToggle();
                    }, 50);
                } else if (!this.imageViewer.contains(target)
                    && !this.footer.contains(target)
                    && !this.header.contains(target)) {
                    setTimeout(() => {
                        if (this.points.length < 2 && !this.movementStart)
                            this.blazorRef.invokeMethodAsync('Close');
                    }, 50);
                }
            }
            window.addEventListener('pointermove', this.onTouchableMove);
        }
    };

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
        if (event.pointerType === 'touch') {
            this.removeEvent(event);
            if (this.points.length === 0) {
                window.removeEventListener('pointermove', this.onTouchableMove);
                this.movementStart = false;
            }
            let imageWidth = this.round(this.imageViewer.getBoundingClientRect().width);
            let imageHeight = this.round(this.imageViewer.getBoundingClientRect().height);
            if (imageWidth < this.minWidth || imageHeight < this.minHeight) {
                this.image.style.width = this.minWidth + 'px';
                this.image.style.maxWidth = this.minWidth + 'px';
                this.image.style.maxHeight = this.minHeight + 'px';
                this.imageViewer.style.left = ((window.innerWidth - this.originImageWidth) / 2) + 'px';
                this.imageViewer.style.top = ((window.innerHeight - this.originImageHeight) / 2) + 'px';
            }
        }
    };

    private onTouchableMove = (event: PointerEvent) => {
        if (!this.movementStart)
            this.movementStart = true;
        preventDefaultForEvent(event);
        const index = this.points.findIndex(
            (e) => e.pointerId === event.pointerId
        );
        this.points[index] = event;

        let windowWidth = window.innerWidth;
        let windowHeight = window.innerHeight;

        if (this.points.length === 2) {
            let distance = this.getDistance();
            let scale = distance / this.startDistance;
            let imageRect = this.image.getBoundingClientRect();
            if (imageRect.width >= this.maxWidth || imageRect.height >= this.maxHeight) {
                if (distance >= this.prevDistance) {
                    this.startDistance = distance;
                    this.startImageRect = imageRect;
                } else {
                    this.scaleImage(scale);
                }
            } else {
                this.scaleImage(scale);
            }
            this.prevDistance = distance;
            this.prevImageRect = imageRect;

        } else if (this.points.length === 1 && this.imageViewer.contains(event.target as HTMLElement)) {
            let viewer = this.imageViewer;
            let imageRect = this.startImageRect;
            if (this.canMoveImage(imageRect)) {
                let rect = this.image.getBoundingClientRect();
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
                            viewer.style.left = this.round(imageRect.left + deltaX) + 'px';
                        }
                    } else if (event.pageX < this.prevX) {
                        // move left
                        if (imageRight <= windowWidth) {
                            viewer.style.left = this.round(windowWidth - imageRect.width) + 'px';
                            this.startX = event.pageX;
                        } else {
                            viewer.style.left = this.round(imageRect.left + deltaX) + 'px';
                        }
                    }
                    this.prevX = event.pageX;
                }

                if (rect.height > windowHeight) {
                    if (event.pageY > this.prevY) {
                        // move down
                        if (imageTop >= 0 && imageTop < 16) {
                            viewer.style.top = (imageRect.top - imageRect.top) + 'px';
                            this.startY = event.pageY;
                        } else {
                            viewer.style.top = this.round(imageRect.top + deltaY) + 'px';
                        }
                    } else if (event.pageY < this.prevY) {
                        // move up
                        if (imageBottom <= windowHeight && imageBottom > windowHeight - 16) {
                            viewer.style.top = this.round(windowHeight - imageRect.height) + 'px';
                            this.startY = event.pageY;
                        } else {
                            viewer.style.top = this.round(imageRect.top + deltaY) + 'px';
                        }
                    }
                    this.prevY = event.pageY;
                }
                this.prevImageRect = rect;
            }
        } else {
            preventDefaultForEvent(event);
        }
    }

    private scaleImage(scale: number) {
        let newImageWidth = this.round(this.startImageRect.width * scale);
        let newImageHeight = this.round(this.startImageRect.height * scale);
        this.image.style.width = newImageWidth + 'px';
        this.image.style.maxWidth = newImageWidth + 'px';
        this.image.style.maxHeight = newImageHeight + 'px';
        this.centerImage();
    }

    private canMoveImage(rect: DOMRect) : boolean {
        return rect.top < 0 || rect.right > window.innerWidth || rect.bottom > window.innerHeight || rect.left < 0;
    }

    private centerImage() {
        let rect = this.imageViewer.getBoundingClientRect();
        const imageLeft = this.round(rect.left);
        const imageRight = this.round(rect.right);
        const imageWidth = this.round(rect.width);
        const windowWidth = window.innerWidth;
        const imageTop = this.round(rect.top);
        const imageBottom = this.round(rect.bottom);
        const imageHeight = this.round(rect.height);
        const windowHeight = window.innerHeight;
        const viewerRect = this.imageViewer.getBoundingClientRect();

        let left = imageLeft;
        let top = imageTop;
        let deltaX = (this.round(this.prevImageRect.width) - imageWidth) / 2;
        let deltaY = (this.round(this.prevImageRect.height) - imageHeight) / 2;

        // center x
        if (rect.width >= windowWidth && rect.width <= this.prevImageRect.width) {
            left = (this.prevImageRect.left + deltaX);
            if (viewerRect.left >= 0) {
                left = 0;
            } else if (viewerRect.right <= windowWidth) {
                left = windowWidth - rect.width;
            }
        } else {
            if ((windowWidth / 2 - imageLeft) != (imageRight - windowWidth / 2)) {
                left = (this.prevImageRect.left + deltaX);
            }
        }

        // center y
        if (rect.height >= windowHeight && rect.height <= this.prevImageRect.height) {
            top = (this.prevImageRect.top + deltaY);
            if (viewerRect.top > 0) {
                top = 0;
            } else if (viewerRect.bottom <= windowHeight) {
                top = windowHeight - rect.height;
            }
        } else {
            if ((windowHeight / 2 - imageTop) != (imageBottom - windowHeight / 2)) {
                top = (this.prevImageRect.top + deltaY);
            }
        }

        this.imageViewer.style.left = left + 'px';
        this.imageViewer.style.top = top + 'px';
    }
}

