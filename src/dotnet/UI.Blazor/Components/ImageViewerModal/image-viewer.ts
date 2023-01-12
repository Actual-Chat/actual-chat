const LogScope = 'ImageViewer';

export class ImageViewer {
    private blazorRef: DotNet.DotNetObject;
    private readonly imageViewer: HTMLElement;
    private overlay: HTMLElement;
    private image: HTMLElement;
    private readonly multiplier: number = 1.4;
    private startY: number = 0;
    private startX: number = 0;
    private deltaY: number = 0;
    private deltaX: number = 0;
    private startImageTop: number = 0;
    private startImageLeft: number = 0;
    private headerBottom: number = 0;
    private footerTop: number = 0;

    static create(imageViewer: HTMLElement, blazorRef: DotNet.DotNetObject): ImageViewer {
        return new ImageViewer(imageViewer, blazorRef);
    }

    constructor(imageViewer: HTMLElement, blazorRef: DotNet.DotNetObject) {
        this.imageViewer = imageViewer;
        this.image = imageViewer.querySelector('img');
        this.blazorRef = blazorRef;
        this.overlay = this.imageViewer.closest('.bm-container');
        window.addEventListener('wheel', this.onImageZoom, { passive: false });
        this.imageViewer.addEventListener('mousedown', this.onImageCapture, { passive: false });
        window.addEventListener('mouseup', this.onImageMoveDisable, { passive: false });
        this.imageViewer.classList.add('bg-01');
    }

    private round = (value: number) : number => {
        return Math.round(value);
    }

    private centerImageX = (delta: number) => {
        let rect = this.imageViewer.getBoundingClientRect();
        let imageLeft = this.round(rect.left);
        let imageRight = this.round(rect.right);
        let imageWidth = this.round(rect.width);
        let windowWidth = window.innerWidth;
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
        let rect = this.imageViewer.getBoundingClientRect();
        let imageTop = this.round(rect.top);
        let imageBottom = this.round(rect.bottom);
        let imageHeight = this.round(rect.height);
        let windowHeight = window.innerHeight;
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

    private onImageZoom = ((event: WheelEvent & { target: Element; }) => {
        if (event.ctrlKey) {
            let delta = event.deltaY;
            let windowWidth = window.innerWidth;
            let windowHeight = window.innerHeight;
            let width = this.image.getBoundingClientRect().width;
            let height = this.image.getBoundingClientRect().height;
            if (delta < 0) {
                // up
                event.preventDefault();
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
                event.preventDefault();
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
        }
    });

    private onImageCapture = ((event: MouseEvent & { target: Element; }) => {
        let parent = this.imageViewer.parentElement;
        this.headerBottom = this.round(parent.querySelector('.image-viewer-header').getBoundingClientRect().bottom);
        this.footerTop = this.round(parent.querySelector('.image-viewer-footer').getBoundingClientRect().top);
        let imageRect = this.imageViewer.getBoundingClientRect();
        let imageTop = this.round(imageRect.top);
        let imageBottom = this.round(imageRect.bottom);
        let imageLeft = this.round(imageRect.left);
        let imageRight = this.round(imageRect.right);
        if (imageTop < this.headerBottom || imageBottom > this.footerTop || imageLeft < 0 || imageRight > window.innerWidth) {
            this.getImageAndMouseX(event, imageRect);
            this.getImageAndMouseY(event, imageRect);
            window.addEventListener('mousemove', this.onImageMove, { passive: false });
        } else {
            event.preventDefault();
        }
    });

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

    private onImageMove = ((event: MouseEvent & { target: Element; }) => {
        event.preventDefault();
        let rect = this.imageViewer.getBoundingClientRect();
        let topPosition = this.getTop(event, rect);
        let leftPosition = this.getLeft(event, rect);
        this.imageViewer.style.top = topPosition + 'px';
        this.imageViewer.style.left = leftPosition + 'px';
    });

    private onImageMoveDisable = ((event: MouseEvent & { target: Element; }) => {
        window.removeEventListener('mousemove', this.onImageMove);
    });

    public dispose() {
        window.removeEventListener('wheel', this.onImageZoom);
        if (this.imageViewer != null) {
            this.imageViewer.removeEventListener('mousedown', this.onImageCapture);
        }
        window.removeEventListener('mouseup', this.onImageMoveDisable);
    }
}

