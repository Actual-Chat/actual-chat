import { Disposable } from 'disposable';
import { fromEvent, Subject, takeUntil, filter } from 'rxjs';
import { preventDefaultForEvent } from 'event-handling';
import type { IUploadStreamSource } from 'UI.Blazor/Services/FileUploads/web-uploads';

const MAX_SOURCE_SIZE = 2048;
const EXPORT_SIZE = 512;
const ZOOM_STEP = 0.03;
const MAX_ZOOM_FACTOR = 4; // max 4× zoom relative to initial fit
const ROTATE_ANIM_DURATION = 350; // ms for button rotation animation
const HANDLE_SIZE = 12; // chevron handle size in pixels
const HANDLE_HIT_RADIUS = 24; // hit area for grabbing the handle

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

export class AvatarCropModal implements Disposable, IUploadStreamSource {
    private readonly canvas: HTMLCanvasElement;
    private readonly ctx: CanvasRenderingContext2D;
    private readonly previewCanvas: HTMLCanvasElement;
    private readonly previewCtx: CanvasRenderingContext2D;
    private readonly img: HTMLImageElement;
    private readonly blobUrl: string;

    private sourceCanvas: HTMLCanvasElement | null = null;
    private offsetX = 0;
    private offsetY = 0;
    private scale = 1;
    private minScale = 1; // set in fitInitialScale — image must cover the viewport
    private maxScale = 1; // set in fitInitialScale — prevents excessive pixelation
    private rotation = 0; // degrees, free rotation
    private dragging = false;
    private rotationDragging = false; // true when dragging the float handle
    private lastX = 0;
    private lastY = 0;
    private croppedBlob: Blob | null = null;
    private animationId: number | null = null;
    private disposed$: Subject<void> = new Subject<void>();

    // Pinch-to-zoom/rotate state
    private pinching = false;
    private pinchInitialDist = 0;
    private pinchInitialAngle = 0;
    private pinchInitialScale = 0;
    private pinchInitialRotation = 0;

    public static create(
        canvas: HTMLCanvasElement,
        previewCanvas: HTMLCanvasElement,
        blobUrl: string,
        blazorRef: DotNet.DotNetObject,
    ): AvatarCropModal {
        return new AvatarCropModal(canvas, previewCanvas, blobUrl, blazorRef);
    }

    constructor(
        canvas: HTMLCanvasElement,
        previewCanvas: HTMLCanvasElement,
        blobUrl: string,
        blazorRef: DotNet.DotNetObject,
    ) {
        this.canvas = canvas;
        this.ctx = canvas.getContext('2d')!;
        this.previewCanvas = previewCanvas;
        this.previewCtx = previewCanvas.getContext('2d')!;
        this.blobUrl = blobUrl;

        this.img = new Image();
        this.img.onload = () => this.onImageLoaded();
        this.img.onerror = () => {
            void blazorRef.invokeMethodAsync('OnImageLoadError');
        };
        this.img.src = blobUrl;

        // Mouse drag (pan or rotation)
        fromEvent<MouseEvent>(canvas, 'mousedown').pipe(
            takeUntil(this.disposed$),
        ).subscribe(e => this.handlePointerDown(e.clientX, e.clientY, e));

        fromEvent<MouseEvent>(window, 'mousemove').pipe(
            takeUntil(this.disposed$),
            filter(() => this.dragging || this.rotationDragging),
        ).subscribe(e => this.handlePointerMove(e.clientX, e.clientY, e));

        fromEvent(window, 'mouseup').pipe(
            takeUntil(this.disposed$),
        ).subscribe(() => this.handlePointerUp());

        // Touch: 1 finger = pan/rotation handle, 2 fingers = pinch zoom+rotate
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
            } else if ((this.dragging || this.rotationDragging) && e.touches.length === 1) {
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
    }

    public getBlob(): Blob {
        if (!this.croppedBlob)
            throw new Error('AvatarCropModal: no cropped blob available.');
        return this.croppedBlob;
    }

    public getCroppedBlobSize(): number {
        if (!this.croppedBlob)
            throw new Error('AvatarCropModal: no cropped blob available.');
        return this.croppedBlob.size;
    }

    public zoomIn(): void {
        this.scale = Math.min(this.maxScale, this.scale + ZOOM_STEP);
        this.render();
    }

    public zoomOut(): void {
        this.scale = Math.max(this.minScale, this.scale - ZOOM_STEP);
        this.clampOffset();
        this.render();
    }

    public rotate(): void {
        // If already animating, skip
        if (this.animationId !== null)
            return;

        // If already at a 90° multiple, advance by 90; otherwise snap to next 90° clockwise
        const mod = ((this.rotation % 90) + 90) % 90;
        let target: number;
        if (mod < 0.5 || mod > 89.5)
            target = Math.round(this.rotation / 90) * 90 + 90;
        else
            target = Math.ceil(this.rotation / 90) * 90;

        this.animateRotation(this.rotation, target);
    }

    private animateRotation(from: number, to: number): void {
        const startTime = performance.now();
        const startScale = this.scale;

        // Pre-compute target scale
        const origRotation = this.rotation;
        this.rotation = to;
        const viewportSize = this.getViewportRadius() * 2;
        const { w, h } = this.getRotatedDimensions();
        const targetScale = (w > 0 && h > 0) ? Math.max(viewportSize / w, viewportSize / h) : startScale;
        this.rotation = origRotation;

        const step = (now: number) => {
            const elapsed = now - startTime;
            const t = Math.min(1, elapsed / ROTATE_ANIM_DURATION);
            // ease-out cubic
            const ease = 1 - Math.pow(1 - t, 3);

            this.rotation = from + (to - from) * ease;
            this.scale = startScale + (targetScale - startScale) * ease;
            this.offsetX = this.offsetX * (1 - ease * 0.3); // gradually reduce offset
            this.offsetY = this.offsetY * (1 - ease * 0.3);
            this.clampOffset();
            this.render();

            if (t < 1) {
                this.animationId = requestAnimationFrame(step);
            } else {
                this.rotation = to % 360;
                this.scale = targetScale;
                this.offsetX = 0;
                this.offsetY = 0;
                this.clampOffset();
                this.animationId = null;
                this.render();
            }
        };
        this.animationId = requestAnimationFrame(step);
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
        this.disposed$.next();
        this.disposed$.complete();
        URL.revokeObjectURL(this.blobUrl);
    }

    private onImageLoaded(): void {
        // Downscale if needed (9A)
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

        this.computeScaleLimits();
        this.fitToViewport();
        this.render();
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

    // Computes scale limits once based on the worst-case rotation angle.
    // minScale = viewportDiameter / min(sourceW, sourceH) — guarantees coverage at any angle.
    private computeScaleLimits(): void {
        const viewportSize = this.getViewportRadius() * 2;
        const sw = this.getSourceWidth();
        const sh = this.getSourceHeight();
        if (sw === 0 || sh === 0)
            return;

        this.minScale = viewportSize / Math.min(sw, sh);
        this.maxScale = this.minScale * MAX_ZOOM_FACTOR;
    }

    // Sets scale to fit the image at the current rotation
    private fitToViewport(): void {
        const viewportSize = this.getViewportRadius() * 2;
        const { w, h } = this.getRotatedDimensions();
        if (w === 0 || h === 0)
            return;

        this.scale = Math.max(viewportSize / w, viewportSize / h);
    }

    private getViewportRadius(): number {
        return Math.min(this.canvas.width, this.canvas.height) * 0.5;
    }

    // Clamp offset so the viewport circle stays fully inside the rotated image rectangle.
    // Projects offset onto image-local axes and clamps along each axis independently.
    private clampOffset(): void {
        const sw = this.getSourceWidth();
        const sh = this.getSourceHeight();
        const r = this.getViewportRadius();
        const rad = this.rotation * Math.PI / 180;
        const cos = Math.cos(rad);
        const sin = Math.sin(rad);

        // Max offset along image-local X and Y axes
        const maxU = Math.max(0, sw * this.scale / 2 - r);
        const maxV = Math.max(0, sh * this.scale / 2 - r);

        // Project canvas offset onto image-local axes
        let u = this.offsetX * cos + this.offsetY * sin;
        let v = -this.offsetX * sin + this.offsetY * cos;

        u = Math.max(-maxU, Math.min(maxU, u));
        v = Math.max(-maxV, Math.min(maxV, v));

        // Convert back to canvas coordinates
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

        // Draw the image centered with current transform
        this.drawTransformedImage(ctx, cx, cy);

        // Draw darkened overlay with circular cutout
        ctx.save();
        ctx.fillStyle = 'rgba(0, 0, 0, 0.6)';
        ctx.beginPath();
        ctx.rect(0, 0, width, height);
        ctx.arc(cx, cy, r, 0, Math.PI * 2, true); // counterclockwise = cutout
        ctx.fill();
        ctx.restore();

        // Draw circle border
        ctx.save();
        ctx.strokeStyle = 'rgba(255, 255, 255, 0.5)';
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.arc(cx, cy, r, 0, Math.PI * 2);
        ctx.stroke();
        ctx.restore();

        // Draw dashed crosshair lines rotated with the image
        this.renderCrosshair(ctx, cx, cy, r);

        // Draw rotation float handle
        this.renderFloat(ctx, cx, cy, r);

        this.renderPreview();
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
        ctx.scale(this.scale, this.scale);
        ctx.drawImage(source, -sw / 2, -sh / 2, sw, sh);
        ctx.restore();
    }

    private renderPreview(): void {
        const size = this.previewCanvas.width;
        const pCtx = this.previewCtx;

        pCtx.clearRect(0, 0, size, size);

        // Clip to circle
        pCtx.save();
        pCtx.beginPath();
        pCtx.arc(size / 2, size / 2, size / 2, 0, Math.PI * 2);
        pCtx.clip();

        // Draw the same transformed image, scaled to preview size
        const r = this.getViewportRadius();
        const previewScale = size / (r * 2);

        pCtx.translate(size / 2, size / 2);
        pCtx.scale(previewScale, previewScale);
        pCtx.translate(this.offsetX, this.offsetY);
        pCtx.rotate((this.rotation * Math.PI) / 180);

        const source = this.getSource();
        const sw = this.getSourceWidth();
        const sh = this.getSourceHeight();
        pCtx.scale(this.scale, this.scale);
        pCtx.drawImage(source, -sw / 2, -sh / 2, sw, sh);

        pCtx.restore();
    }

    private renderCrop(): Promise<Blob | null> {
        const offscreen = document.createElement('canvas');
        offscreen.width = EXPORT_SIZE;
        offscreen.height = EXPORT_SIZE;
        const ctx = offscreen.getContext('2d')!;

        const r = this.getViewportRadius();
        const exportScale = EXPORT_SIZE / (r * 2);

        ctx.translate(EXPORT_SIZE / 2, EXPORT_SIZE / 2);
        ctx.scale(exportScale, exportScale);
        ctx.translate(this.offsetX, this.offsetY);
        ctx.rotate((this.rotation * Math.PI) / 180);

        const source = this.getSource();
        const sw = this.getSourceWidth();
        const sh = this.getSourceHeight();
        ctx.scale(this.scale, this.scale);
        ctx.drawImage(source, -sw / 2, -sh / 2, sw, sh);

        return new Promise<Blob | null>(resolve => {
            offscreen.toBlob(blob => resolve(blob), 'image/png');
        });
    }

    private renderCrosshair(ctx: CanvasRenderingContext2D, cx: number, cy: number, r: number): void {
        const rad = this.rotation * Math.PI / 180;
        // Brighter when aligned to 0/90/180/270
        const aligned = this.isAlignedTo90();
        const alpha = aligned ? 0.8 : 0.35;
        const lineWidth = aligned ? 1.5 : 1;

        ctx.save();
        ctx.translate(cx, cy);
        ctx.rotate(rad);
        ctx.setLineDash([4, 6]);
        ctx.strokeStyle = `rgba(255, 255, 255, ${alpha})`;
        ctx.lineWidth = lineWidth;

        // Vertical line
        ctx.beginPath();
        ctx.moveTo(0, -r);
        ctx.lineTo(0, r);
        ctx.stroke();

        // Horizontal line
        ctx.beginPath();
        ctx.moveTo(-r, 0);
        ctx.lineTo(r, 0);
        ctx.stroke();

        ctx.restore();
    }

    // Returns true when rotation is within ~1° of a 90° multiple
    private isAlignedTo90(): boolean {
        const mod = ((this.rotation % 90) + 90) % 90; // normalize to [0, 90)
        return mod < 1 || mod > 89;
    }

    // Draws a triangle pointing toward the center, with red border and white fill
    private renderFloat(ctx: CanvasRenderingContext2D, cx: number, cy: number, r: number): void {
        const { fx, fy } = this.getFloatPosition(cx, cy, r);
        const toCenter = Math.atan2(cy - fy, cx - fx);
        const s = HANDLE_SIZE;

        ctx.save();
        ctx.translate(fx, fy);
        ctx.rotate(toCenter);

        // Triangle pointing right (toward center after rotation)
        ctx.beginPath();
        ctx.moveTo(s * 0.6, 0);        // tip toward center
        ctx.lineTo(-s * 0.5, -s * 0.6); // top-left
        ctx.lineTo(-s * 0.5, s * 0.6);  // bottom-left
        ctx.closePath();

        ctx.fillStyle = 'white';
        ctx.fill();
        ctx.strokeStyle = '#ef4444';
        ctx.lineWidth = 2.5;
        ctx.lineJoin = 'round';
        ctx.stroke();

        ctx.restore();
    }

    // Handle position: inset from circle edge so it's fully visible inside
    private getFloatPosition(cx: number, cy: number, r: number): { fx: number; fy: number } {
        const floatAngle = (this.rotation - 90) * Math.PI / 180;
        const inset = r - HANDLE_SIZE * 0.9 + 2; // pull inside the circle, +2px outward
        return {
            fx: cx + inset * Math.cos(floatAngle),
            fy: cy + inset * Math.sin(floatAngle),
        };
    }

    private toCanvasCoords(clientX: number, clientY: number): [number, number] {
        const rect = this.canvas.getBoundingClientRect();
        const scaleX = this.canvas.width / rect.width;
        const scaleY = this.canvas.height / rect.height;
        return [(clientX - rect.left) * scaleX, (clientY - rect.top) * scaleY];
    }

    private isNearFloat(clientX: number, clientY: number): boolean {
        const [canvasX, canvasY] = this.toCanvasCoords(clientX, clientY);

        const cx = this.canvas.width / 2;
        const cy = this.canvas.height / 2;
        const r = this.getViewportRadius();
        const { fx, fy } = this.getFloatPosition(cx, cy, r);

        const dx = canvasX - fx;
        const dy = canvasY - fy;
        return dx * dx + dy * dy <= HANDLE_HIT_RADIUS * HANDLE_HIT_RADIUS;
    }

    private handlePointerDown(clientX: number, clientY: number, e: Event): void {
        preventDefaultForEvent(e);
        if (this.isNearFloat(clientX, clientY)) {
            this.rotationDragging = true;
        } else {
            this.dragging = true;
            const [cx, cy] = this.toCanvasCoords(clientX, clientY);
            this.lastX = cx;
            this.lastY = cy;
        }
    }

    private handlePointerMove(clientX: number, clientY: number, e: Event): void {
        preventDefaultForEvent(e);
        if (this.rotationDragging) {
            this.applyRotationDrag(clientX, clientY);
        } else if (this.dragging) {
            const [cx, cy] = this.toCanvasCoords(clientX, clientY);
            const dx = cx - this.lastX;
            const dy = cy - this.lastY;
            this.lastX = cx;
            this.lastY = cy;
            this.applyPan(dx, dy);
        }
    }

    private handlePointerUp(): void {
        this.dragging = false;
        this.rotationDragging = false;
    }

    private applyRotationDrag(clientX: number, clientY: number): void {
        const [canvasX, canvasY] = this.toCanvasCoords(clientX, clientY);

        const cx = this.canvas.width / 2;
        const cy = this.canvas.height / 2;

        // Angle from center to pointer, then +90 because float is at "top" of vertical line
        const angle = Math.atan2(canvasY - cy, canvasX - cx) * 180 / Math.PI;
        this.rotation = angle + 90;
        this.clampOffset();
        this.render();
    }

    private applyPan(dx: number, dy: number): void {
        this.offsetX += dx;
        this.offsetY += dy;
        this.clampOffset();
        this.render();
    }

    // Two-finger pinch: zoom + rotate simultaneously

    private handlePinchStart(e: TouchEvent): void {
        preventDefaultForEvent(e);
        // Cancel any single-finger drag
        this.dragging = false;
        this.rotationDragging = false;
        // Cancel any button-rotate animation
        if (this.animationId !== null) {
            cancelAnimationFrame(this.animationId);
            this.animationId = null;
        }

        this.pinching = true;
        const [t0, t1] = [e.touches[0], e.touches[1]];
        this.pinchInitialDist = this.touchDistance(t0, t1);
        this.pinchInitialAngle = this.touchAngle(t0, t1);
        this.pinchInitialScale = this.scale;
        this.pinchInitialRotation = this.rotation;
    }

    private handlePinchMove(e: TouchEvent): void {
        preventDefaultForEvent(e);
        const [t0, t1] = [e.touches[0], e.touches[1]];

        // Scale: ratio of current distance to initial distance
        const dist = this.touchDistance(t0, t1);
        const scaleRatio = dist / this.pinchInitialDist;
        this.scale = Math.max(this.minScale, Math.min(this.maxScale, this.pinchInitialScale * scaleRatio));

        // Rotation: delta angle between fingers
        const angle = this.touchAngle(t0, t1);
        const angleDelta = (angle - this.pinchInitialAngle) * 180 / Math.PI;
        this.rotation = this.pinchInitialRotation + angleDelta;

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
