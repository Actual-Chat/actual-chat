import './image-viewer-modal.css';

const LogScope = 'ImageViewer';

export class ImageViewer {
    private blazorRef: DotNet.DotNetObject;
    private imageViewer: HTMLElement;
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
    private screenHeight: number = 0;

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
    }

    private round = (value: number) : number => {
        return Math.round(value);
    }

    private alignHorizontal = (windowWidth: number, windowHeight: number) => {
        let rect = this.imageViewer.getBoundingClientRect();
        let imageLeft = this.round(rect.left);
        let imageRight = this.round(rect.right);
        let imageTop = this.round(rect.top);
        let imageBottom = this.round(rect.bottom);
        let imageWidth = this.round(rect.width);
        let imageHeight = this.round(rect.height);
        if ((windowWidth / 2 - imageLeft) != (imageRight - windowWidth / 2)) {
            let newImageLeft = (windowWidth - imageWidth) / 2;
            this.imageViewer.style.left = newImageLeft + 'px';
        }
        if ((windowHeight / 2 - imageTop) != (imageBottom - windowHeight / 2)) {
            let newImageTop = (windowHeight - imageHeight) / 2;
            this.imageViewer.style.top = newImageTop + 'px';
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
                if (newMaxHeight > windowHeight * 2 || newMaxWidth > windowWidth * 2) {
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
                let newMaWidth = width / this.multiplier;
                let newMaxHeight = height / this.multiplier;
                if (newWidth < 80) {
                    newWidth = 80;
                    newMaxHeight = height;
                    newMaWidth = newWidth;
                }
                this.image.style.width = newWidth + 'px';
                this.image.style.maxHeight = newMaxHeight + 'px';
                this.image.style.maxWidth = newMaWidth + 'px';
            }
            this.alignHorizontal(windowWidth, windowHeight);
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
            this.startY = event.clientY;
            this.startX = event.clientX;
            this.startImageTop = imageRect.top;
            this.startImageLeft = imageRect.left;
            this.deltaY = this.startY - this.startImageTop;
            this.deltaX = this.startX - this.startImageLeft;
            window.addEventListener('mousemove', this.onImageMove, { passive: false });
        } else {
            event.preventDefault();
        }
    });

    private onImageMove = ((event: MouseEvent & { target: Element; }) => {
        event.preventDefault();
        let y = event.pageY;
        let x = event.pageX;
        let top = 0;
        let left = 0;
        let rect = this.imageViewer.getBoundingClientRect();
        if (this.round(rect.left) >= 0 && this.round(rect.right) <= window.innerWidth) {
            left = this.round(rect.left);
        } else {
            left = x - this.deltaX;
        }
        if (this.round(rect.top) >= this.headerBottom && this.round(rect.bottom) <= this.footerTop) {
            top = this.round(rect.top);
        } else {
            top = y - this.deltaY;
        }
        this.imageViewer.style.top = top + 'px';
        this.imageViewer.style.left = left + 'px';
    });

    private onImageMoveDisable = ((event: MouseEvent & { target: Element; }) => {
        window.removeEventListener('mousemove', this.onImageMove);
    });

    public dispose() {
        window.removeEventListener('wheel', this.onImageZoom);
    }
}

