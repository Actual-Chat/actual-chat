// Sender-side real pixel downscaler (WebGL2). Replaces the metadata-only
// `new VideoFrame(src, { displayWidth, displayHeight })` wrap that left
// codedWidth == source: HEVC HW encoders kept the full coded size and only
// set a conformance window, which Edge HEVC HW renders as a top-left crop.
// Producing a true coded == target frame per layer eliminates that.
//
// Cascade: tiers are downscaled top→bottom, each from the previous (larger)
// tier — ~½ steps read fewer texels and box-filter cleanly. The top tier is
// the normalized input itself (no GPU work); only lower tiers are resized.
//
// Upload/orientation convention mirrors `webgl-bg.ts` exactly (UNPACK_FLIP_Y
// + non-flipping VS), which is verified upright in production.

import { getLogs } from 'logging';
import { createProgram, createTexture, setupFullScreenQuad } from '../playback/webgl-helpers';
import type { DownscalerLike, LayerSpec } from '../operators/downscale';

const { warnLog } = getLogs('VideoPipeline');

const VERTEX_SHADER = `#version 300 es
layout(location = 0) in vec2 a_position;
out vec2 v_uv;
void main() {
  v_uv = (a_position + 1.0) * 0.5;
  gl_Position = vec4(a_position, 0.0, 1.0);
}
`;

const COPY_FRAGMENT = `#version 300 es
precision mediump float;
in vec2 v_uv;
uniform sampler2D u_texture;
out vec4 fragColor;
void main() {
  fragColor = texture(u_texture, v_uv);
}
`;

// Once WebGL2 downscale proves unavailable or the context is lost, fall back
// to Canvas2D for the rest of the session — re-probing would keep failing.
let webglDownscaleDisabled = false;

export function disableWebGlDownscale(): void {
    webglDownscaleDisabled = true;
}

export function probeWebGl2(): boolean {
    if (webglDownscaleDisabled)
        return false;
    try {
        const probe = new OffscreenCanvas(1, 1);
        return probe.getContext('webgl2') !== null;
    } catch {
        return false;
    }
}

export class WebGlDownscaler implements DownscalerLike {
    private readonly canvas: OffscreenCanvas;
    private gl: WebGL2RenderingContext | null = null;
    private program: WebGLProgram | null = null;
    private positionBuffer: WebGLBuffer | null = null;
    private sourceTexture: WebGLTexture | null = null;
    private uTexture: WebGLUniformLocation | null = null;
    private disposed = false;

    constructor() {
        this.canvas = new OffscreenCanvas(0, 0);
        const gl = this.canvas.getContext('webgl2', {
            alpha: false,
            antialias: false,
            depth: false,
            stencil: false,
            premultipliedAlpha: false,
            preserveDrawingBuffer: false,
        });
        if (!gl)
            throw new Error('WebGlDownscaler: webgl2 context unavailable');
        this.gl = gl;
        this.initGl();
    }

    // eslint-disable-next-line @typescript-eslint/require-await
    async process(input: VideoFrame, layers: readonly LayerSpec[]): Promise<VideoFrame[]> {
        const gl = this.gl;
        if (!gl)
            throw new Error('WebGlDownscaler: context unavailable');
        const topIdx = layers.length - 1;
        const results = new Array<VideoFrame | null>(layers.length).fill(null);
        // Ceiling = the normalized input's visible size. A tier matching it is
        // passed through (no GPU work); every other tier is a real downscale.
        // The input is owned by the caller — never closed here.
        const inW = input.displayWidth || input.codedWidth;
        const inH = input.displayHeight || input.codedHeight;
        try {
            // Cascade source: starts at the ceiling input, then each smaller
            // tier samples the previous larger tier's output.
            let src: CanvasImageSource = input;
            for (let i = topIdx; i >= 0; i--) {
                const { width, height } = layers[i];
                if (width === inW && height === inH) {
                    results[i] = input;
                    continue;
                }
                if (this.canvas.width !== width)
                    this.canvas.width = width;
                if (this.canvas.height !== height)
                    this.canvas.height = height;
                gl.bindTexture(gl.TEXTURE_2D, this.sourceTexture);
                gl.texImage2D(
                    gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE,
                    src as unknown as TexImageSource);
                gl.viewport(0, 0, width, height);
                gl.bindFramebuffer(gl.FRAMEBUFFER, null);
                gl.drawArrays(gl.TRIANGLES, 0, 6);
                // VideoFrame(canvas) snapshots the drawing buffer, so reusing
                // the canvas for the next (smaller) tier is safe.
                const out = new VideoFrame(this.canvas, {
                    timestamp: input.timestamp,
                    alpha: 'discard',
                });
                results[i] = out;
                src = out;
            }
            return results as VideoFrame[];
        } catch (e) {
            warnLog?.log('WebGlDownscaler.process failed — disabling WebGL downscale:', e);
            disableWebGlDownscale();
            for (const r of results)
                if (r && r !== input) try { r.close(); } catch { /* ignore */ }
            throw e;
        }
    }

    dispose(): void {
        if (this.disposed)
            return;
        this.disposed = true;
        const gl = this.gl;
        if (!gl)
            return;
        if (this.sourceTexture) gl.deleteTexture(this.sourceTexture);
        if (this.positionBuffer) gl.deleteBuffer(this.positionBuffer);
        if (this.program) gl.deleteProgram(this.program);
        gl.getExtension('WEBGL_lose_context')?.loseContext();
        this.gl = null;
        this.canvas.width = 0;
        this.canvas.height = 0;
    }

    private initGl(): void {
        const gl = this.gl!;
        this.program = createProgram(gl, VERTEX_SHADER, COPY_FRAGMENT);
        this.uTexture = gl.getUniformLocation(this.program, 'u_texture');
        this.positionBuffer = setupFullScreenQuad(gl);
        // Upload-time Y flip so the non-flipping VS reads frames upright.
        gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, 1);
        gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, 0);
        this.sourceTexture = createTexture(gl);
        gl.useProgram(this.program);
        gl.activeTexture(gl.TEXTURE0);
        gl.uniform1i(this.uTexture, 0);
    }
}
