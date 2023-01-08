import './image-viewer-modal.css';

const LogScope = 'ImageViewer';

export class ImageViewer {
    private blazorRef: DotNet.DotNetObject;
    private imageViewer: HTMLElement;
    private overlay: HTMLElement;
    private image: HTMLElement;
    private readonly multiplier: number = 1.1;

    static create(imageViewer: HTMLElement, blazorRef: DotNet.DotNetObject): ImageViewer {
        return new ImageViewer(imageViewer, blazorRef);
    }

    constructor(imageViewer: HTMLElement, blazorRef: DotNet.DotNetObject) {
        this.imageViewer = imageViewer;
        this.image = imageViewer.querySelector('img');
        this.blazorRef = blazorRef;
        this.overlay = this.imageViewer.closest('.bm-container');
        window.addEventListener('wheel', this.onImageZoom, {passive: false});
        // window.addEventListener('keyup', this.onControlUp);
    }

    private onImageZoom = ((event: WheelEvent & { target: Element; }) => {
        if (event.ctrlKey) {
            let delta = event.deltaY;
            if (delta < 0) {
                // up
                event.preventDefault();
                let width = this.image.getBoundingClientRect().width;
                let newWidth = width * this.multiplier;
                this.image.style.width = newWidth + 'px';
            } else {
                // down
                event.preventDefault();
                let width = this.image.getBoundingClientRect().width;
                let newWidth = width / this.multiplier;
                if (newWidth < 80) {
                    newWidth = 80;
                }
                this.image.style.width = newWidth + 'px';
            }
        }
    });

    public dispose() {
        window.removeEventListener('keyup', this.onImageZoom);
    }
}

