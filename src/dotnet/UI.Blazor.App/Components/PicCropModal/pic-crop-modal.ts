import { Disposable } from 'disposable';
import { fromEvent, Subject, takeUntil, filter } from 'rxjs';
import { preventDefaultForEvent } from 'event-handling';
import type { IUploadStreamSource } from 'UI.Blazor/Services/FileUploads/web-uploads';

const MAX_SOURCE_SIZE = 2048;
const EXPORT_SIZE = 512;
const ZOOM_STEP = 0.02;
const MAX_ZOOM_FACTOR = 4; // max 4× zoom relative to initial fit
const ROTATE_STEP = 1; // degrees per tap on a rotation button
const ROTATE_HOLD_DELAY = 300; // ms a rotation button must be held before it spins continuously
const ROTATE_SPEED = 30; // degrees per second once a held rotation button starts spinning
const ZOOM_SPEED = 0.5; // scale units per second for continuous button zoom
const SQUARE_CORNER_RADIUS = 32; // rounded-2xl equivalent for square viewport

export function clickFileInput(input: HTMLInputElement): void {
    input.click();
}

export function createBlobUrlFromInput(input: HTMLInputElement): string | null {
    const file = input.files?.[0];
    return file ? URL.createObjectURL(file) : null;
}

export function clearFileInput(input: HTMLInputElement): void {
    input.value = '';
}

export class PicCropModal implements Disposable, IUploadStreamSource {
    private readonly canvas: HTMLCanvasElement;
    private readonly ctx: CanvasRenderingContext2D;
    private readonly previewCanvases: HTMLCanvasElement[];
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly img: HTMLImageElement;
    private readonly isSquare: boolean;
    private readonly viewportAspectRatio: number; // 0 = square/circle, >0 = W:H ratio
    private readonly hasBlur: boolean;
    private blurRadius = 0;

    // The avatar the modal opened on. "Revert to previous" returns here; empty for a marble.
    private readonly seedUrl: string;
    // Object URLs we created and must revoke; the seed/CDN url is owned by the caller.
    private readonly ownedUrls = new Set<string>();
    private hasSource = false;

    private sourceCanvas: HTMLCanvasElement | null = null;
    private offsetX = 0;
    private offsetY = 0;
    private scale = 1;
    private minScale = 1; // set in computeScaleLimits — image must cover the viewport
    private maxScale = 1; // set in computeScaleLimits — prevents excessive pixelation
    private rotation = 0; // degrees, free rotation
    private flipX = false; // horizontal mirror
    private dragging = false;
    private lastX = 0;
    private lastY = 0;
    private croppedBlob: Blob | null = null;
    private animationId: number | null = null;
    private rotateDirection = 0; // -1 = CCW, 0 = none, 1 = CW
    private rotateHoldTimer: number | null = null;
    private zoomDirection = 0; // -1 = out, 0 = none, 1 = in
    private lastAnimTime = 0;
    private disposed$: Subject<void> = new Subject<void>();

    // Pinch-to-zoom/rotate state
    private pinching = false;
    private pinchInitialDist = 0;
    private pinchInitialAngle = 0;
    private pinchInitialScale = 0;
    private pinchInitialRotation = 0;
    private pinchInitialOffsetX = 0;
    private pinchInitialOffsetY = 0;

    public static create(
        canvas: HTMLCanvasElement,
        blobUrl: string,
        blazorRef: DotNet.DotNetObject,
        isSquare = false,
        viewportAspectRatio = 0,
        hasBlur = false,
    ): PicCropModal {
        return new PicCropModal(canvas, blobUrl, blazorRef, isSquare, viewportAspectRatio, hasBlur);
    }

    constructor(
        canvas: HTMLCanvasElement,
        blobUrl: string,
        blazorRef: DotNet.DotNetObject,
        isSquare = false,
        viewportAspectRatio = 0,
        hasBlur = false,
    ) {
        this.canvas = canvas;
        this.ctx = canvas.getContext('2d')!;
        this.blazorRef = blazorRef;
        this.isSquare = isSquare;
        this.viewportAspectRatio = viewportAspectRatio;
        this.hasBlur = hasBlur;
        this.seedUrl = blobUrl;

        const modal = canvas.closest('.pic-crop-modal')!;
        this.previewCanvases = Array.from(modal.querySelectorAll<HTMLCanvasElement>('.c-preview canvas'));

        this.img = new Image();
        // The source is served from the CDN origin, and without this the canvas it is drawn into
        // is tainted - toBlob() then throws a SecurityError when the crop is applied.
        this.img.crossOrigin = 'anonymous';
        this.img.onload = () => this.onImageLoaded();
        this.img.onerror = () => {
            void blazorRef.invokeMethodAsync('OnImageLoadError');
        };
        if (blobUrl)
            this.img.src = blobUrl;
        else
            this.render();

        // Mouse drag (pan)
        fromEvent<MouseEvent>(canvas, 'mousedown').pipe(
            takeUntil(this.disposed$),
        ).subscribe(e => this.handlePointerDown(e.clientX, e.clientY, e));

        fromEvent<MouseEvent>(window, 'mousemove').pipe(
            takeUntil(this.disposed$),
            filter(() => this.dragging),
        ).subscribe(e => this.handlePointerMove(e.clientX, e.clientY, e));

        fromEvent(window, 'mouseup').pipe(
            takeUntil(this.disposed$),
        ).subscribe(() => this.handlePointerUp());

        // Touch: 1 finger = pan, 2 fingers = pinch zoom+rotate
        fromEvent<TouchEvent>(canvas, 'touchstart', { passive: false } as AddEventListenerOptions).pipe(
            takeUntil(this.disposed$),
        ).subscribe(e => {
            if (e.touches.length === 2) {
                this.handlePinchStart(e);
            } else if (e.touches.length === 1 && !this.pinching) {
                this.handlePointerDown(e.touches[0].clientX, e.touches[0].clientY, e);
            }
        });

        fromEvent<TouchEvent>(window, 'touchmove', { passive: false } as AddEventListenerOptions).pipe(
            takeUntil(this.disposed$),
        ).subscribe(e => {
            if (this.pinching && e.touches.length === 2) {
                this.handlePinchMove(e);
            } else if (this.dragging && e.touches.length === 1) {
                this.handlePointerMove(e.touches[0].clientX, e.touches[0].clientY, e);
            }
        });

        fromEvent<TouchEvent>(window, 'touchend').pipe(
            takeUntil(this.disposed$),
        ).subscribe(e => {
            if (this.pinching && e.touches.length < 2)
                this.pinching = false;
            else
                this.handlePointerUp();
        });

        // Wheel zoom
        fromEvent<WheelEvent>(canvas, 'wheel', { passive: false } as AddEventListenerOptions).pipe(
            takeUntil(this.disposed$),
        ).subscribe(e => {
            preventDefaultForEvent(e);
            if (e.deltaY < 0)
                this.zoomIn();
            else
                this.zoomOut();
        });

        // Rotation buttons: a tap steps by ROTATE_STEP; holding past ROTATE_HOLD_DELAY spins.
        const rotateButtons: [string, number][] = [
            ['.btn-rotate-ccw', -1],
            ['.btn-rotate-cw', 1],
        ];
        for (const [selector, dir] of rotateButtons) {
            const btn = modal.querySelector(selector);
            if (!btn)
                continue;
            fromEvent(btn, 'pointerdown').pipe(
                takeUntil(this.disposed$),
            ).subscribe(() => {
                if (!this.hasSource)
                    return;
                this.nudgeRotation(dir * ROTATE_STEP);
                this.rotateHoldTimer = window.setTimeout(() => {
                    this.rotateDirection = dir;
                    this.startContinuousAction();
                }, ROTATE_HOLD_DELAY);
            });
            const stop = () => {
                if (this.rotateHoldTimer !== null) {
                    clearTimeout(this.rotateHoldTimer);
                    this.rotateHoldTimer = null;
                }
                this.rotateDirection = 0;
                this.stopContinuousActionIfIdle();
            };
            fromEvent(btn, 'pointerup').pipe(takeUntil(this.disposed$)).subscribe(stop);
            fromEvent(btn, 'pointerleave').pipe(takeUntil(this.disposed$)).subscribe(stop);
        }

        // Zoom slider
        const zoomSlider = modal.querySelector<HTMLInputElement>('.zoom-range');
        if (zoomSlider) {
            fromEvent(zoomSlider, 'input').pipe(
                takeUntil(this.disposed$),
            ).subscribe(() => {
                if (!this.hasSource)
                    return;
                this.scale = this.minScale * (Number(zoomSlider.value) / 100);
                this.scale = Math.max(this.minScale, Math.min(this.maxScale, this.scale));
                this.clampOffset();
                this.render();
            });
        }

        // Blur slider
        if (hasBlur) {
            const blurSlider = modal.querySelector<HTMLInputElement>('.blur-range')!;
            fromEvent(blurSlider, 'input').pipe(
                takeUntil(this.disposed$),
            ).subscribe(() => {
                this.blurRadius = Number(blurSlider.value);
                blurSlider.parentElement?.style.setProperty('--progress', String(this.blurRadius / 16));
                this.render();
            });
        }
    }

    /** Replaces the source image, e.g. after an upload or a regenerate. Marks this as an editable source. */
    public setImage(blobUrl: string, owned = false): void {
        if (owned)
            this.ownedUrls.add(blobUrl);
        this.img.src = blobUrl;
    }

    /** Returns to the avatar the modal opened on (empty for a marble), discarding any upload. */
    public revertToPrevious(): void {
        if (this.seedUrl)
            this.img.src = this.seedUrl;
        else
            this.clearImage();
    }

    private clearImage(): void {
        this.hasSource = false;
        this.sourceCanvas = null;
        this.img.removeAttribute('src');
        this.render();
        void this.blazorRef.invokeMethodAsync('OnSourceChanged', false);
    }

    public mirror(): void {
        if (!this.hasSource)
            return;
        this.flipX = !this.flipX;
        // Mirror around the viewport centre, not the image centre: reflect the offset across the
        // vertical axis through that centre. The flip runs in the rotated frame, so the reflection
        // axis is tilted by the rotation - hence the 2θ terms (identity R(θ)·diag(-1,1)·R(-θ)).
        const rad = 2 * this.rotation * Math.PI / 180;
        const cos = Math.cos(rad);
        const sin = Math.sin(rad);
        const ox = this.offsetX;
        const oy = this.offsetY;
        this.offsetX = -cos * ox - sin * oy;
        this.offsetY = -sin * ox + cos * oy;
        this.clampOffset();
        this.render();
    }

    /** Resets pan/zoom/rotation/mirror to the initial cover fit of the current source. */
    public reset(): void {
        if (!this.hasSource)
            return;
        this.rotation = 0;
        this.flipX = false;
        this.offsetX = 0;
        this.offsetY = 0;
        this.computeScaleLimits();
        this.fitToViewport();
        this.render();
    }

    public getBlob(): Blob {
        if (!this.croppedBlob)
            throw new Error('PicCropModal: no cropped blob available.');
        return this.croppedBlob;
    }

    public getCroppedBlobSize(): number {
        if (!this.croppedBlob)
            throw new Error('PicCropModal: no cropped blob available.');
        return this.croppedBlob.size;
    }

    // Rotates the view by deltaDeg around the viewport centre (the point currently under the crop),
    // not the image centre - so whatever is framed stays framed while the image spins beneath it.
    // Keeping that point fixed means rotating the offset vector by the same angle.
    private rotateBy(deltaDeg: number): void {
        const rad = deltaDeg * Math.PI / 180;
        const cos = Math.cos(rad);
        const sin = Math.sin(rad);
        const ox = this.offsetX;
        const oy = this.offsetY;
        this.offsetX = ox * cos - oy * sin;
        this.offsetY = ox * sin + oy * cos;
        this.rotation += deltaDeg;
    }

    private nudgeRotation(deltaDeg: number): void {
        if (!this.hasSource)
            return;
        this.rotateBy(deltaDeg);
        this.clampOffset();
        this.render();
    }

    public zoomIn(): void {
        if (!this.hasSource)
            return;
        this.scale = Math.min(this.maxScale, this.scale + ZOOM_STEP);
        this.render();
    }

    public zoomOut(): void {
        if (!this.hasSource)
            return;
        this.scale = Math.max(this.minScale, this.scale - ZOOM_STEP);
        this.clampOffset();
        this.render();
    }

    private startContinuousAction(): void {
        if (!this.hasSource)
            return;
        this.lastAnimTime = performance.now();
        this.animationId ??= requestAnimationFrame(now => this.continuousActionStep(now));
    }

    private stopContinuousActionIfIdle(): void {
        if (this.rotateDirection === 0 && this.zoomDirection === 0) {
            if (this.animationId !== null) {
                cancelAnimationFrame(this.animationId);
                this.animationId = null;
            }
        }
    }

    private continuousActionStep(now: number): void {
        const dt = (now - this.lastAnimTime) / 1000;
        this.lastAnimTime = now;

        if (this.rotateDirection !== 0)
            this.rotateBy(this.rotateDirection * ROTATE_SPEED * dt);
        if (this.zoomDirection !== 0) {
            this.scale += this.zoomDirection * ZOOM_SPEED * dt;
            this.scale = Math.max(this.minScale, Math.min(this.maxScale, this.scale));
        }

        this.clampOffset();
        this.render();

        if (this.rotateDirection !== 0 || this.zoomDirection !== 0)
            this.animationId = requestAnimationFrame(t => this.continuousActionStep(t));
        else
            this.animationId = null;
    }

    public async exportCrop(): Promise<boolean> {
        const blob = await this.renderCrop();
        if (!blob)
            return false;
        this.croppedBlob = blob;
        return true;
    }

    public dispose(): void {
        if (this.disposed$.closed)
            return;

        if (this.animationId !== null) {
            cancelAnimationFrame(this.animationId);
            this.animationId = null;
        }
        if (this.rotateHoldTimer !== null) {
            clearTimeout(this.rotateHoldTimer);
            this.rotateHoldTimer = null;
        }
        this.disposed$.next();
        this.disposed$.complete();
        for (const url of this.ownedUrls)
            URL.revokeObjectURL(url);
        this.ownedUrls.clear();
    }

    private onImageLoaded(): void {
        this.hasSource = true;
        this.sourceCanvas = null;

        // Downscale if needed
        const img = this.img;
        if (img.naturalWidth > MAX_SOURCE_SIZE || img.naturalHeight > MAX_SOURCE_SIZE) {
            const ratio = Math.min(MAX_SOURCE_SIZE / img.naturalWidth, MAX_SOURCE_SIZE / img.naturalHeight);
            const w = Math.round(img.naturalWidth * ratio);
            const h = Math.round(img.naturalHeight * ratio);
            this.sourceCanvas = document.createElement('canvas');
            this.sourceCanvas.width = w;
            this.sourceCanvas.height = h;
            const ctx = this.sourceCanvas.getContext('2d')!;
            ctx.drawImage(img, 0, 0, w, h);
        }

        this.rotation = 0;
        this.flipX = false;
        this.offsetX = 0;
        this.offsetY = 0;
        this.computeScaleLimits();
        this.fitToViewport();
        this.render();
        void this.blazorRef.invokeMethodAsync('OnSourceChanged', true);
    }

    private getSourceWidth(): number {
        return this.sourceCanvas ? this.sourceCanvas.width : this.img.naturalWidth;
    }

    private getSourceHeight(): number {
        return this.sourceCanvas ? this.sourceCanvas.height : this.img.naturalHeight;
    }

    private getSource(): CanvasImageSource {
        return this.sourceCanvas ?? this.img;
    }

    // Returns the bounding box of the image after rotation
    private getRotatedDimensions(): { w: number; h: number } {
        const sw = this.getSourceWidth();
        const sh = this.getSourceHeight();
        const rad = Math.abs(this.rotation * Math.PI / 180);
        const cos = Math.abs(Math.cos(rad));
        const sin = Math.abs(Math.sin(rad));
        return { w: sw * cos + sh * sin, h: sw * sin + sh * cos };
    }

    // Computes scale limits based on the source size and viewport.
    private computeScaleLimits(): void {
        const { vw, vh } = this.getViewportSize();
        const sw = this.getSourceWidth();
        const sh = this.getSourceHeight();
        if (sw === 0 || sh === 0)
            return;

        this.minScale = Math.max(vw / sw, vh / sh);
        this.maxScale = this.minScale * MAX_ZOOM_FACTOR;
    }

    // Sets scale to fit the image at the current rotation
    private fitToViewport(): void {
        const { vw, vh } = this.getViewportSize();
        const { w, h } = this.getRotatedDimensions();
        if (w === 0 || h === 0)
            return;

        this.scale = Math.max(vw / w, vh / h);
    }

    private getViewportRadius(): number {
        return Math.min(this.canvas.width, this.canvas.height) * 0.5;
    }

    // Returns the viewport dimensions: {vw, vh} in canvas pixels
    private getViewportSize(): { vw: number; vh: number } {
        if (this.viewportAspectRatio > 0) {
            const vw = this.canvas.width;
            const vh = vw / this.viewportAspectRatio;
            return { vw, vh };
        }
        const r = this.getViewportRadius();
        return { vw: r * 2, vh: r * 2 };
    }

    // Traces the viewport shape path (circle, rounded square, or rectangle) without calling fill/stroke/clip.
    private traceViewportPath(ctx: CanvasRenderingContext2D, cx: number, cy: number, r: number, vw?: number, vh?: number): void {
        if (this.viewportAspectRatio > 0) {
            const w = vw ?? this.canvas.width;
            const h = vh ?? w / this.viewportAspectRatio;
            ctx.roundRect(cx - w / 2, cy - h / 2, w, h, 8);
        } else if (this.isSquare) {
            const mainR = this.getViewportRadius();
            const cornerR = mainR > 0 ? SQUARE_CORNER_RADIUS * (r / mainR) : SQUARE_CORNER_RADIUS;
            ctx.roundRect(cx - r, cy - r, r * 2, r * 2, cornerR);
        } else {
            ctx.arc(cx, cy, r, 0, Math.PI * 2);
        }
    }

    // Clamp offset so the viewport stays fully inside the rotated image rectangle.
    // Projects offset onto image-local axes and clamps along each axis independently.
    private clampOffset(): void {
        const sw = this.getSourceWidth();
        const sh = this.getSourceHeight();
        const { vw, vh } = this.getViewportSize();
        const halfVW = vw / 2;
        const halfVH = vh / 2;
        const rad = this.rotation * Math.PI / 180;
        const cos = Math.cos(rad);
        const sin = Math.sin(rad);

        const maxU = Math.max(0, sw * this.scale / 2 - halfVW);
        const maxV = Math.max(0, sh * this.scale / 2 - halfVH);

        let u = this.offsetX * cos + this.offsetY * sin;
        let v = -this.offsetX * sin + this.offsetY * cos;

        u = Math.max(-maxU, Math.min(maxU, u));
        v = Math.max(-maxV, Math.min(maxV, v));

        this.offsetX = u * cos - v * sin;
        this.offsetY = u * sin + v * cos;
    }

    private render(): void {
        if (this.disposed$.closed)
            return;

        const { width, height } = this.canvas;
        const ctx = this.ctx;
        const cx = width / 2;
        const cy = height / 2;
        const r = this.getViewportRadius();

        ctx.clearRect(0, 0, width, height);

        if (this.hasSource)
            this.drawTransformedImage(ctx, cx, cy);

        // Draw darkened overlay with viewport cutout
        ctx.save();
        ctx.fillStyle = 'rgba(0, 0, 0, 0.6)';
        ctx.beginPath();
        ctx.rect(0, 0, width, height);
        this.traceViewportPath(ctx, cx, cy, r);
        ctx.fill('evenodd');
        ctx.restore();

        // Draw viewport border
        ctx.save();
        ctx.strokeStyle = 'rgba(255, 255, 255, 0.5)';
        ctx.lineWidth = 2;
        ctx.beginPath();
        this.traceViewportPath(ctx, cx, cy, r);
        ctx.stroke();
        ctx.restore();

        if (this.hasSource)
            this.renderGrid(ctx, cx, cy, r);

        this.renderPreviews();
        this.updateZoomUi();
        this.updateAngleUi();
    }

    private drawTransformedImage(
        ctx: CanvasRenderingContext2D,
        cx: number,
        cy: number,
    ): void {
        const source = this.getSource();
        const sw = this.getSourceWidth();
        const sh = this.getSourceHeight();

        ctx.save();
        ctx.translate(cx + this.offsetX, cy + this.offsetY);
        ctx.rotate((this.rotation * Math.PI) / 180);
        ctx.scale(this.flipX ? -this.scale : this.scale, this.scale);
        ctx.drawImage(source, -sw / 2, -sh / 2, sw, sh);
        ctx.restore();
    }

    private renderPreviews(): void {
        for (const preview of this.previewCanvases)
            this.renderPreview(preview);
    }

    private renderPreview(previewCanvas: HTMLCanvasElement): void {
        const pw = previewCanvas.width;
        const ph = previewCanvas.height;
        const pCtx = previewCanvas.getContext('2d')!;

        pCtx.clearRect(0, 0, pw, ph);
        if (!this.hasSource)
            return;

        pCtx.save();
        pCtx.beginPath();
        this.traceViewportPath(pCtx, pw / 2, ph / 2, Math.min(pw, ph) / 2, pw, ph);
        pCtx.clip();

        if (this.blurRadius > 0)
            pCtx.filter = `blur(${this.blurRadius * (pw / this.canvas.width)}px)`;

        const { vw, vh } = this.getViewportSize();
        const previewScale = Math.min(pw / vw, ph / vh);

        pCtx.translate(pw / 2, ph / 2);
        pCtx.scale(previewScale, previewScale);
        pCtx.translate(this.offsetX, this.offsetY);
        pCtx.rotate((this.rotation * Math.PI) / 180);

        const source = this.getSource();
        const sw = this.getSourceWidth();
        const sh = this.getSourceHeight();
        pCtx.scale(this.flipX ? -this.scale : this.scale, this.scale);
        pCtx.drawImage(source, -sw / 2, -sh / 2, sw, sh);

        pCtx.restore();
    }

    private renderCrop(): Promise<Blob | null> {
        const { vw, vh } = this.getViewportSize();
        const exportW = EXPORT_SIZE;
        const exportH = Math.round(EXPORT_SIZE * (vh / vw));
        const offscreen = document.createElement('canvas');
        offscreen.width = exportW;
        offscreen.height = exportH;
        const ctx = offscreen.getContext('2d')!;

        const exportScale = exportW / vw;

        if (this.blurRadius > 0)
            ctx.filter = `blur(${this.blurRadius * exportScale}px)`;

        ctx.translate(exportW / 2, exportH / 2);
        ctx.scale(exportScale, exportScale);
        ctx.translate(this.offsetX, this.offsetY);
        ctx.rotate((this.rotation * Math.PI) / 180);

        const source = this.getSource();
        const sw = this.getSourceWidth();
        const sh = this.getSourceHeight();
        ctx.scale(this.flipX ? -this.scale : this.scale, this.scale);
        ctx.drawImage(source, -sw / 2, -sh / 2, sw, sh);

        return new Promise<Blob | null>(resolve => {
            offscreen.toBlob(blob => resolve(blob), 'image/png');
        });
    }

    // Rule-of-thirds grid clipped to the viewport shape.
    private renderGrid(ctx: CanvasRenderingContext2D, cx: number, cy: number, r: number): void {
        const { vw, vh } = this.getViewportSize();
        const left = cx - vw / 2;
        const top = cy - vh / 2;

        ctx.save();
        ctx.beginPath();
        this.traceViewportPath(ctx, cx, cy, r, vw, vh);
        ctx.clip();

        ctx.strokeStyle = 'rgba(255, 255, 255, 0.35)';
        ctx.lineWidth = 1;
        ctx.beginPath();
        for (let i = 1; i <= 2; i++) {
            const x = left + (vw * i) / 3;
            ctx.moveTo(x, top);
            ctx.lineTo(x, top + vh);
            const y = top + (vh * i) / 3;
            ctx.moveTo(left, y);
            ctx.lineTo(left + vw, y);
        }
        ctx.stroke();
        ctx.restore();
    }

    private updateZoomUi(): void {
        const modal = this.canvas.closest('.pic-crop-modal');
        if (!modal)
            return;
        const percent = this.minScale > 0 ? Math.round((this.scale / this.minScale) * 100) : 100;
        const slider = modal.querySelector<HTMLInputElement>('.zoom-range');
        if (slider) {
            slider.value = String(percent);
            const min = Number(slider.min) || 100;
            const max = Number(slider.max) || 100 * MAX_ZOOM_FACTOR;
            const progress = max > min ? (percent - min) / (max - min) : 0;
            slider.parentElement?.style.setProperty('--progress', String(progress));
        }
        const value = modal.querySelector<HTMLElement>('.c-zoom-value');
        if (value)
            value.textContent = `${percent}%`;
    }

    // Shows the tilt relative to the original, normalized to (-180, 180]; hidden at 0.
    private updateAngleUi(): void {
        const modal = this.canvas.closest('.pic-crop-modal');
        const chip = modal?.querySelector<HTMLElement>('.c-angle');
        if (!chip)
            return;
        let angle = ((this.rotation % 360) + 360) % 360;
        if (angle > 180)
            angle -= 360;
        const rounded = Math.round(angle);
        chip.hidden = !this.hasSource || rounded === 0;
        chip.textContent = `${rounded}°`;
    }

    private toCanvasCoords(clientX: number, clientY: number): [number, number] {
        const rect = this.canvas.getBoundingClientRect();
        const scaleX = this.canvas.width / rect.width;
        const scaleY = this.canvas.height / rect.height;
        return [(clientX - rect.left) * scaleX, (clientY - rect.top) * scaleY];
    }

    private handlePointerDown(clientX: number, clientY: number, e: Event): void {
        if (!this.hasSource)
            return;
        preventDefaultForEvent(e);
        this.dragging = true;
        const [cx, cy] = this.toCanvasCoords(clientX, clientY);
        this.lastX = cx;
        this.lastY = cy;
    }

    private handlePointerMove(clientX: number, clientY: number, e: Event): void {
        preventDefaultForEvent(e);
        if (!this.dragging)
            return;
        const [cx, cy] = this.toCanvasCoords(clientX, clientY);
        const dx = cx - this.lastX;
        const dy = cy - this.lastY;
        this.lastX = cx;
        this.lastY = cy;
        this.applyPan(dx, dy);
    }

    private handlePointerUp(): void {
        this.dragging = false;
    }

    private applyPan(dx: number, dy: number): void {
        this.offsetX += dx;
        this.offsetY += dy;
        this.clampOffset();
        this.render();
    }

    // Two-finger pinch: zoom + rotate simultaneously

    private handlePinchStart(e: TouchEvent): void {
        if (!this.hasSource)
            return;
        preventDefaultForEvent(e);
        this.dragging = false;
        this.rotateDirection = 0;
        this.zoomDirection = 0;
        this.stopContinuousActionIfIdle();

        this.pinching = true;
        const [t0, t1] = [e.touches[0], e.touches[1]];
        this.pinchInitialDist = this.touchDistance(t0, t1);
        this.pinchInitialAngle = this.touchAngle(t0, t1);
        this.pinchInitialScale = this.scale;
        this.pinchInitialRotation = this.rotation;
        this.pinchInitialOffsetX = this.offsetX;
        this.pinchInitialOffsetY = this.offsetY;
    }

    private handlePinchMove(e: TouchEvent): void {
        preventDefaultForEvent(e);
        const [t0, t1] = [e.touches[0], e.touches[1]];

        const dist = this.touchDistance(t0, t1);
        const scaleRatio = dist / this.pinchInitialDist;
        this.scale = Math.max(this.minScale, Math.min(this.maxScale, this.pinchInitialScale * scaleRatio));

        const angle = this.touchAngle(t0, t1);
        const angleDelta = (angle - this.pinchInitialAngle) * 180 / Math.PI;
        this.rotation = this.pinchInitialRotation + angleDelta;

        // Rotate around the viewport centre: spin the initial offset by the same delta (see rotateBy).
        const rad = angleDelta * Math.PI / 180;
        const cos = Math.cos(rad);
        const sin = Math.sin(rad);
        this.offsetX = this.pinchInitialOffsetX * cos - this.pinchInitialOffsetY * sin;
        this.offsetY = this.pinchInitialOffsetX * sin + this.pinchInitialOffsetY * cos;

        this.clampOffset();
        this.render();
    }

    private touchDistance(t0: Touch, t1: Touch): number {
        return Math.hypot(t1.clientX - t0.clientX, t1.clientY - t0.clientY);
    }

    private touchAngle(t0: Touch, t1: Touch): number {
        return Math.atan2(t1.clientY - t0.clientY, t1.clientX - t0.clientX);
    }
}
