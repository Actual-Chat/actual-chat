// Blurred backdrop drawn behind a contain-fitted main video tile
// (.remote-video-bg in video-panel.css). WebGL is preferred so blur happens in
// the tiny bg-canvas pixel space; 2D canvas is the fallback.

import { CanvasTarget } from './canvas-target';
import { BgBlurPerfTracker } from './bg-blur-stats';

export const BG_CANVAS_WIDTH = 48;
export const BG_DRAW_INTERVAL_MS = 100;
export const BG_FILTER = 'blur(3px) saturate(1.2)';
export const BG_CANVAS_OVERDRAW_PX = 4;
let isWebGlCanvasCopySupportedCached: boolean | null = null;

export class BgCanvasRenderer {
    private target: BgCanvasRenderTarget;
    private fallback: CanvasBgRenderTarget | null = null;

    constructor(private readonly bgCanvas: HTMLCanvasElement) {
        this.target = WebGlBgRenderTarget.create(bgCanvas) ?? this.getFallback();
    }

    draw(source: CanvasImageSource, width: number, height: number, mirrorX = false): void {
        try {
            this.target.draw(source, width, height, mirrorX);
        } catch {
            // Only reachable from WebGL copy-mode (direct-WebGL mode is not allowed -
            // see WebGlBgRenderTarget.create). The scratch WebGL canvas is separate
            // from bgCanvas, so swapping to a Canvas2D target on bgCanvas is safe.
            const fallback = this.getFallback();
            if (this.target === fallback)
                return;

            this.target.dispose();
            this.target = fallback;
            this.target.draw(source, width, height, mirrorX);
        }
    }

    clear(): void {
        this.target.clear();
    }

    dispose(): void {
        this.target.dispose();
    }

    private getFallback(): CanvasBgRenderTarget {
        this.fallback ??= new CanvasBgRenderTarget(this.bgCanvas);
        return this.fallback;
    }
}

/** Drives the .remote-video-bg canvas from a visible `<video>` or `<canvas>`. */
export class BgCanvasPainter {
    private readonly target: BgCanvasRenderer;
    private timer: ReturnType<typeof setInterval> | null = null;
    private getSource: (() => SourceLike | null) | null = null;
    private readonly perf = new BgBlurPerfTracker('webgl');

    constructor(bgCanvas: HTMLCanvasElement) {
        this.target = new BgCanvasRenderer(bgCanvas);
    }

    /** Start the pump. `getSource` is re-read every tick so a late-arriving
     *  `<video>` track or canvas swap is picked up live. No-op when already
     *  running with the same source — callers (refreshBgPump) invoke start()
     *  on every latency-tap / viewport-check pass, and resetting the timer
     *  on each call collapses the effective rate to whatever cadence the
     *  caller fires at. */
    start(getSource: () => SourceLike | null): void {
        if (this.timer !== null && this.getSource === getSource)
            return;

        this.getSource = getSource;
        this.stop();
        this.timer = setInterval(() => this.tick(), BG_DRAW_INTERVAL_MS);
        this.tick();
    }

    stop(): void {
        if (this.timer !== null) {
            clearInterval(this.timer);
            this.timer = null;
        }
        this.target.clear();
    }

    dispose(): void {
        this.stop();
        this.getSource = null;
        this.target.dispose();
    }

    private tick(): void {
        const src = this.getSource?.();
        if (!src) return;
        const sw = readSourceWidth(src);
        const sh = readSourceHeight(src);
        if (sw <= 0 || sh <= 0) return;
        const bgW = BG_CANVAS_WIDTH;
        const bgH = Math.max(1, Math.round(bgW * sh / sw));
        const t0 = performance.now();
        this.target.draw(src as CanvasImageSource, bgW, bgH);
        this.perf.sample(performance.now() - t0);
    }
}

export type SourceLike = HTMLCanvasElement | HTMLVideoElement;

function readSourceWidth(src: SourceLike): number {
    if (src instanceof HTMLVideoElement) return src.videoWidth;
    return src.width;
}

function readSourceHeight(src: SourceLike): number {
    if (src instanceof HTMLVideoElement) return src.videoHeight;
    return src.height;
}

interface BgCanvasRenderTarget {
    readonly kind: 'webgl' | 'canvas2d';
    draw(source: CanvasImageSource, width: number, height: number, mirrorX: boolean): void;
    clear(): void;
    dispose(): void;
}

class CanvasBgRenderTarget implements BgCanvasRenderTarget {
    readonly kind = 'canvas2d' as const;
    private readonly target: CanvasTarget;

    constructor(bgCanvas: HTMLCanvasElement) {
        this.target = new CanvasTarget(bgCanvas, false, BG_FILTER, BG_CANVAS_OVERDRAW_PX);
    }

    draw(source: CanvasImageSource, width: number, height: number, mirrorX: boolean): void {
        this.target.draw(source, width, height, mirrorX);
    }

    clear(): void {
        this.target.clear();
    }

    dispose(): void {
        this.clear();
    }
}

class WebGlBgRenderTarget implements BgCanvasRenderTarget {
    readonly kind = 'webgl' as const;
    private readonly element: HTMLCanvasElement;
    private readonly output: HTMLCanvasElement;
    private readonly outputCtx: CanvasRenderingContext2D;
    private readonly gl: WebGLRenderingContext;
    private readonly program: WebGLProgram;
    private readonly positionBuffer: WebGLBuffer;
    private readonly sourceTexture: WebGLTexture;
    private readonly tempTextures: [WebGLTexture, WebGLTexture];
    private readonly framebuffers: [WebGLFramebuffer, WebGLFramebuffer];
    private readonly attribPosition: number;
    private readonly uniformTexture: WebGLUniformLocation | null;
    private readonly uniformTexelOffset: WebGLUniformLocation | null;
    private readonly uniformCropX: WebGLUniformLocation | null;
    private readonly uniformCropY: WebGLUniformLocation | null;
    private readonly uniformMirrorX: WebGLUniformLocation | null;
    private readonly uniformBlurAxis: WebGLUniformLocation | null;
    private readonly uniformSaturate: WebGLUniformLocation | null;
    private pixelBuffer: Uint8Array | null = null;
    private cachedImageData: ImageData | null = null;
    private textureWidth = 0;
    private textureHeight = 0;

    // Returns null whenever direct-WebGL would be required (i.e. when 2D
    // copy-output isn't supported), so the caller can fall back to Canvas2D
    // safely. Mixing WebGL and 2D contexts on the same canvas isn't possible,
    // so we never attach WebGL directly to the user-visible canvas.
    static create(output: HTMLCanvasElement): BgCanvasRenderTarget | null {
        if (!isWebGlCanvasCopySupported())
            return null;

        // No `desynchronized: true` here — Chromium WebView (MAUI Android,
        // some WebView2 builds) either rejects the hint outright or renders
        // into a buffer the compositor doesn't surface, leaving the bg canvas
        // visibly empty. Standard compositing is fine for a 10 Hz update.
        const outputCtx = output.getContext('2d', { alpha: true });
        if (!outputCtx)
            return null;

        const scratch = document.createElement('canvas');
        // alpha:true (was: false). With alpha:false WebGL spec lets readPixels
        // return any value for the alpha component; some GPU drivers return 0,
        // which makes putImageData write fully-transparent pixels. The shader
        // writes gl_FragColor.a = 1.0 in both branches, so alpha:true preserves
        // it. Same reasoning for omitting `desynchronized: true`.
        const contextAttrs: WebGLContextAttributes = {
            alpha: true,
            antialias: false,
            depth: false,
            preserveDrawingBuffer: false,
            premultipliedAlpha: true,
            stencil: false,
        };
        const gl = getWebGlContext(scratch, contextAttrs);
        if (!gl)
            return null;

        try {
            return new WebGlBgRenderTarget(scratch, output, outputCtx, gl);
        } catch {
            gl.getExtension('WEBGL_lose_context')?.loseContext();
            return null;
        }
    }

    private constructor(
        element: HTMLCanvasElement,
        output: HTMLCanvasElement,
        outputCtx: CanvasRenderingContext2D,
        gl: WebGLRenderingContext,
    ) {
        this.element = element;
        this.output = output;
        this.outputCtx = outputCtx;
        this.gl = gl;
        this.program = createProgram(gl, VERTEX_SHADER_SOURCE, FRAGMENT_SHADER_SOURCE);
        this.positionBuffer = requireGlObject(gl.createBuffer(), 'position buffer');
        this.sourceTexture = createTexture(gl);
        this.tempTextures = [createTexture(gl), createTexture(gl)];
        this.framebuffers = [
            requireGlObject(gl.createFramebuffer(), 'framebuffer'),
            requireGlObject(gl.createFramebuffer(), 'framebuffer'),
        ];
        this.attribPosition = gl.getAttribLocation(this.program, 'a_position');
        // Uniform locations may be null if the compiler optimized the uniform
        // out (e.g. a future shader edit drops a reference). WebGL ignores
        // null locations in uniform* setters, so we just store and pass through.
        this.uniformTexture = gl.getUniformLocation(this.program, 'u_texture');
        this.uniformTexelOffset = gl.getUniformLocation(this.program, 'u_texelOffset');
        this.uniformCropX = gl.getUniformLocation(this.program, 'u_cropX');
        this.uniformCropY = gl.getUniformLocation(this.program, 'u_cropY');
        this.uniformMirrorX = gl.getUniformLocation(this.program, 'u_mirrorX');
        this.uniformBlurAxis = gl.getUniformLocation(this.program, 'u_blurAxis');
        this.uniformSaturate = gl.getUniformLocation(this.program, 'u_saturate');

        gl.bindBuffer(gl.ARRAY_BUFFER, this.positionBuffer);
        gl.bufferData(
            gl.ARRAY_BUFFER,
            new Float32Array([
                -1, -1,
                1, -1,
                -1, 1,
                -1, 1,
                1, -1,
                1, 1,
            ]),
            gl.STATIC_DRAW);
    }

    draw(source: CanvasImageSource, width: number, height: number, mirrorX: boolean): void {
        if (width <= 0 || height <= 0)
            return;

        const gl = this.gl;
        this.resize(width, height);
        gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, 1);
        gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, 0);
        gl.activeTexture(gl.TEXTURE0);
        gl.bindTexture(gl.TEXTURE_2D, this.sourceTexture);
        gl.texImage2D(
            gl.TEXTURE_2D,
            0,
            gl.RGBA,
            gl.RGBA,
            gl.UNSIGNED_BYTE,
            source as TexImageSource);

        const cropX = BG_CANVAS_OVERDRAW_PX / Math.max(1, width + 2 * BG_CANVAS_OVERDRAW_PX);
        const cropY = BG_CANVAS_OVERDRAW_PX / Math.max(1, height + 2 * BG_CANVAS_OVERDRAW_PX);
        this.drawPass(this.sourceTexture, this.framebuffers[0], 0, cropX, cropY, mirrorX, 1);
        this.drawPass(this.tempTextures[0], this.framebuffers[1], 1, 0, 0, false, 1);
        this.drawPass(this.tempTextures[1], this.framebuffers[0], 2, 0, 0, false, 1);
        this.drawPass(this.tempTextures[0], this.framebuffers[1], 1, 0, 0, false, 1);
        this.drawPass(this.tempTextures[1], null, 2, 0, 0, false, 1.2);

        if (this.output.width !== width || this.output.height !== height) {
            this.output.width = width;
            this.output.height = height;
        }
        this.outputCtx.save();
        try {
            this.outputCtx.setTransform(1, 0, 0, 1, 0, 0);
            this.outputCtx.filter = 'none';
            this.outputCtx.globalAlpha = 1;
            this.outputCtx.globalCompositeOperation = 'copy';
            this.outputCtx.putImageData(this.readOutputImageData(width, height), 0, 0);
        } finally {
            this.outputCtx.restore();
        }
    }

    clear(): void {
        if (this.output.width > 0 && this.output.height > 0)
            this.outputCtx.clearRect(0, 0, this.output.width, this.output.height);
        const gl = this.gl;
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
        gl.viewport(0, 0, this.element.width, this.element.height);
        gl.clearColor(0, 0, 0, 0);
        gl.clear(gl.COLOR_BUFFER_BIT);
    }

    dispose(): void {
        const gl = this.gl;
        gl.deleteTexture(this.sourceTexture);
        for (const texture of this.tempTextures)
            gl.deleteTexture(texture);
        for (const framebuffer of this.framebuffers)
            gl.deleteFramebuffer(framebuffer);
        gl.deleteBuffer(this.positionBuffer);
        gl.deleteProgram(this.program);
        gl.getExtension('WEBGL_lose_context')?.loseContext();
    }

    private resize(width: number, height: number): void {
        if (this.element.width !== width || this.element.height !== height) {
            this.element.width = width;
            this.element.height = height;
        }
        if (this.textureWidth === width && this.textureHeight === height)
            return;

        const gl = this.gl;
        this.textureWidth = width;
        this.textureHeight = height;
        this.pixelBuffer = null;
        this.cachedImageData = null;
        for (let i = 0; i < this.tempTextures.length; i++) {
            gl.bindTexture(gl.TEXTURE_2D, this.tempTextures[i]);
            gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
            gl.bindFramebuffer(gl.FRAMEBUFFER, this.framebuffers[i]);
            gl.framebufferTexture2D(
                gl.FRAMEBUFFER,
                gl.COLOR_ATTACHMENT0,
                gl.TEXTURE_2D,
                this.tempTextures[i],
                0);
            const status = gl.checkFramebufferStatus(gl.FRAMEBUFFER);
            if (status !== gl.FRAMEBUFFER_COMPLETE)
                throw new Error(`bg canvas WebGL framebuffer incomplete: 0x${status.toString(16)}`);
        }
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    }

    private drawPass(
        texture: WebGLTexture,
        framebuffer: WebGLFramebuffer | null,
        blurAxis: number,
        cropX: number,
        cropY: number,
        mirrorX: boolean,
        saturate: number,
    ): void {
        const gl = this.gl;
        gl.useProgram(this.program);
        gl.bindFramebuffer(gl.FRAMEBUFFER, framebuffer);
        gl.viewport(0, 0, this.element.width, this.element.height);
        gl.bindBuffer(gl.ARRAY_BUFFER, this.positionBuffer);
        gl.enableVertexAttribArray(this.attribPosition);
        gl.vertexAttribPointer(this.attribPosition, 2, gl.FLOAT, false, 0, 0);
        gl.activeTexture(gl.TEXTURE0);
        gl.bindTexture(gl.TEXTURE_2D, texture);
        gl.uniform1i(this.uniformTexture, 0);
        gl.uniform2f(
            this.uniformTexelOffset,
            1 / Math.max(1, this.element.width),
            1 / Math.max(1, this.element.height));
        gl.uniform1f(this.uniformCropX, cropX);
        gl.uniform1f(this.uniformCropY, cropY);
        gl.uniform1f(this.uniformMirrorX, mirrorX ? 1 : 0);
        gl.uniform1f(this.uniformBlurAxis, blurAxis);
        gl.uniform1f(this.uniformSaturate, saturate);
        gl.drawArrays(gl.TRIANGLES, 0, 6);
    }

    private readOutputImageData(width: number, height: number): ImageData {
        const gl = this.gl;
        const rowStride = width * 4;
        this.pixelBuffer ??= new Uint8Array(rowStride * height);
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
        gl.readPixels(0, 0, width, height, gl.RGBA, gl.UNSIGNED_BYTE, this.pixelBuffer);

        this.cachedImageData ??= this.outputCtx.createImageData(width, height);
        const dst = this.cachedImageData.data;
        for (let y = 0; y < height; y++) {
            const srcRow = (height - 1 - y) * rowStride;
            const dstRow = y * rowStride;
            dst.set(this.pixelBuffer.subarray(srcRow, srcRow + rowStride), dstRow);
        }
        return this.cachedImageData;
    }
}

function createTexture(gl: WebGLRenderingContext): WebGLTexture {
    const texture = requireGlObject(gl.createTexture(), 'texture');
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    return texture;
}

function createProgram(
    gl: WebGLRenderingContext,
    vertexSource: string,
    fragmentSource: string,
): WebGLProgram {
    let vertexShader: WebGLShader | null = null;
    let fragmentShader: WebGLShader | null = null;
    let program: WebGLProgram | null = null;
    try {
        vertexShader = createShader(gl, gl.VERTEX_SHADER, vertexSource);
        fragmentShader = createShader(gl, gl.FRAGMENT_SHADER, fragmentSource);
        program = requireGlObject(gl.createProgram(), 'program');
        gl.attachShader(program, vertexShader);
        gl.attachShader(program, fragmentShader);
        gl.linkProgram(program);
        if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
            const message = gl.getProgramInfoLog(program) ?? 'unknown link error';
            throw new Error(`bg canvas WebGL program link failed: ${message}`);
        }
        return program;
    } catch (e) {
        if (program)
            gl.deleteProgram(program);
        throw e;
    } finally {
        if (vertexShader)
            gl.deleteShader(vertexShader);
        if (fragmentShader)
            gl.deleteShader(fragmentShader);
    }
}

function createShader(gl: WebGLRenderingContext, type: number, source: string): WebGLShader {
    const shader = requireGlObject(gl.createShader(type), 'shader');
    gl.shaderSource(shader, source);
    gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
        const message = gl.getShaderInfoLog(shader) ?? 'unknown compile error';
        gl.deleteShader(shader);
        throw new Error(`bg canvas WebGL shader compile failed: ${message}`);
    }
    return shader;
}

function requireGlObject<T>(value: T | null, name: string): T {
    if (value === null)
        throw new Error(`bg canvas WebGL ${name} unavailable`);
    return value;
}

function getWebGlContext(
    canvas: HTMLCanvasElement,
    contextAttrs: WebGLContextAttributes,
): WebGLRenderingContext | null {
    const gl = canvas.getContext('webgl', contextAttrs);
    if (gl)
        return gl;

    return canvas.getContext('experimental-webgl', contextAttrs) as WebGLRenderingContext | null;
}

function isWebGlCanvasCopySupported(): boolean {
    if (isWebGlCanvasCopySupportedCached !== null)
        return isWebGlCanvasCopySupportedCached;
    try {
        const source = document.createElement('canvas');
        source.width = 2;
        source.height = 2;
        const gl = getWebGlContext(source, {
            alpha: true,
            antialias: false,
            depth: false,
            preserveDrawingBuffer: false,
            premultipliedAlpha: true,
            stencil: false,
        });
        const target = document.createElement('canvas');
        target.width = 2;
        target.height = 2;
        const ctx = target.getContext('2d', { alpha: true });
        if (!gl || !ctx) {
            isWebGlCanvasCopySupportedCached = false;
            return isWebGlCanvasCopySupportedCached;
        }

        gl.viewport(0, 0, 2, 2);
        gl.clearColor(1, 0, 0, 1);
        gl.clear(gl.COLOR_BUFFER_BIT);

        const pixels = new Uint8Array(16);
        gl.readPixels(0, 0, 2, 2, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
        const imageData = ctx.createImageData(2, 2);
        imageData.data.set(pixels);
        ctx.putImageData(imageData, 0, 0);

        const data = ctx.getImageData(0, 0, 1, 1).data;
        isWebGlCanvasCopySupportedCached = data[0] > 200 && data[1] < 40 && data[2] < 40 && data[3] > 200;
        gl.getExtension('WEBGL_lose_context')?.loseContext();
    } catch {
        isWebGlCanvasCopySupportedCached = false;
    }
    return isWebGlCanvasCopySupportedCached;
}

const VERTEX_SHADER_SOURCE = `
attribute vec2 a_position;
varying vec2 v_uv;

void main() {
    v_uv = a_position * 0.5 + 0.5;
    gl_Position = vec4(a_position, 0.0, 1.0);
}
`;

const FRAGMENT_SHADER_SOURCE = `
precision mediump float;

uniform sampler2D u_texture;
uniform vec2 u_texelOffset;
uniform float u_cropX;
uniform float u_cropY;
uniform float u_mirrorX;
uniform float u_blurAxis;
uniform float u_saturate;
varying vec2 v_uv;

vec2 cropUv(vec2 uv) {
    if (u_mirrorX > 0.5) {
        uv.x = 1.0 - uv.x;
    }
    return vec2(
        mix(u_cropX, 1.0 - u_cropX, uv.x),
        mix(u_cropY, 1.0 - u_cropY, uv.y)
    );
}

void main() {
    vec2 uv = cropUv(v_uv);
    if (u_blurAxis < 0.5) {
        vec4 source = texture2D(u_texture, uv);
        float gray = dot(source.rgb, vec3(0.2126, 0.7152, 0.0722));
        source.rgb = mix(vec3(gray), source.rgb, u_saturate);
        source.a = 1.0;
        gl_FragColor = source;
        return;
    }

    // 9-tap separable blur. JS runs the horizontal + vertical pair twice in
    // the downsampled bg-canvas pixel space. Side taps use a 2px-ish step and
    // land between pixel centers to avoid preserving too much source detail.
    vec2 axis = u_blurAxis < 1.5 ? vec2(u_texelOffset.x, 0.0) : vec2(0.0, u_texelOffset.y);
    vec4 color = texture2D(u_texture, uv) * 0.2270270270;
    color += texture2D(u_texture, uv + axis * 1.5) * 0.1945945946;
    color += texture2D(u_texture, uv - axis * 1.5) * 0.1945945946;
    color += texture2D(u_texture, uv + axis * 3.5) * 0.1216216216;
    color += texture2D(u_texture, uv - axis * 3.5) * 0.1216216216;
    color += texture2D(u_texture, uv + axis * 5.5) * 0.0540540541;
    color += texture2D(u_texture, uv - axis * 5.5) * 0.0540540541;
    color += texture2D(u_texture, uv + axis * 7.5) * 0.0162162162;
    color += texture2D(u_texture, uv - axis * 7.5) * 0.0162162162;
    float gray = dot(color.rgb, vec3(0.2126, 0.7152, 0.0722));
    color.rgb = mix(vec3(gray), color.rgb, u_saturate);
    color.a = 1.0;
    gl_FragColor = color;
}
`;
