// Sender-side WebGL2 downscaler that outputs NV12 VideoFrames per tier — a
// readback-based alternative to webgpu/downscaler.ts. Motivation: the WebGPU
// NV12 path skips the encoder's libyuv ConvertAndScale, but its Dawn `mapAsync`
// readback periodically collides with the HW encoder on the mobile GPU
// (Downscale time mean 8 ms / max 382 ms), dropping fps. The plain WebGL
// downscaler is smooth (1.8 / 30 ms) because it reads back nothing (canvas-
// backed frames). This path tests whether a WebGL2 `readPixels` readback avoids
// that contention while still producing NV12.
//
// Per tier: render RGB→Y packed 4-per-RGBA-texel into a (W/4)×H framebuffer and
// RGB→UV (U0,V0,U1,V1 per texel) into a (W/4)×(H/2) framebuffer (BT.709 limited),
// readPixels both, assemble a 256-aligned NV12 buffer (rows reversed — readPixels
// is bottom-up), and wrap as a VideoFrame. Orientation/upload mirror
// webgl/downscaler.ts (UNPACK_FLIP_Y + non-flipping VS, verified upright).
//
// Self-falls-back to MetadataDownscaler on context loss / failure.

import { getLogs } from 'logging';
import { createProgram, createTexture, createFbo, setupFullScreenQuad } from '../playback/webgl-helpers';
import { MetadataDownscaler } from '../metadata/downscaler';
import type { DownscalerLike, LayerSpec } from '../operators/downscale';

const { warnLog, infoLog } = getLogs('VideoPipeline');

const VERTEX_SHADER = `#version 300 es
layout(location = 0) in vec2 a_position;
out vec2 v_uv;
void main() {
  v_uv = (a_position + 1.0) * 0.5;
  gl_Position = vec4(a_position, 0.0, 1.0);
}
`;

// RGB→Y (BT.709 limited), 4 horizontal luma pixels packed into one RGBA texel.
// FBO is (W/4)×H; u_outW is the tier width. Bilinear source sampling downscales;
// at ratio 1 it reads exact pixel centers (no softening).
const Y_FRAGMENT = `#version 300 es
precision highp float;
in vec2 v_uv;
uniform sampler2D u_src;
uniform float u_outW;
out vec4 fragColor;
float Y(vec3 c) { return (16.0 + 46.5594*c.r + 156.6294*c.g + 15.8112*c.b) / 255.0; }
void main() {
  float px0 = floor(v_uv.x * u_outW * 0.25) * 4.0;
  float v = v_uv.y;
  float y0 = Y(texture(u_src, vec2((px0 + 0.5) / u_outW, v)).rgb);
  float y1 = Y(texture(u_src, vec2((px0 + 1.5) / u_outW, v)).rgb);
  float y2 = Y(texture(u_src, vec2((px0 + 2.5) / u_outW, v)).rgb);
  float y3 = Y(texture(u_src, vec2((px0 + 3.5) / u_outW, v)).rgb);
  fragColor = vec4(y0, y1, y2, y3);
}
`;

// RGB→UV (BT.709 limited), 2 chroma pixels (U0,V0,U1,V1) per RGBA texel.
// FBO is (W/4)×(H/2); vertical bilinear averages the 2×2 luma block.
const UV_FRAGMENT = `#version 300 es
precision highp float;
in vec2 v_uv;
uniform sampler2D u_src;
uniform float u_outW;
out vec4 fragColor;
void main() {
  float cx0 = floor(v_uv.x * u_outW * 0.25) * 2.0;
  float v = v_uv.y;
  vec3 a = texture(u_src, vec2((2.0*cx0 + 1.0) / u_outW, v)).rgb;
  vec3 b = texture(u_src, vec2((2.0*(cx0 + 1.0) + 1.0) / u_outW, v)).rgb;
  float u0 = (128.0 - 25.6642*a.r - 86.3358*a.g + 112.0*a.b) / 255.0;
  float w0 = (128.0 + 112.0*a.r - 101.7303*a.g - 10.2697*a.b) / 255.0;
  float u1 = (128.0 - 25.6642*b.r - 86.3358*b.g + 112.0*b.b) / 255.0;
  float w1 = (128.0 + 112.0*b.r - 101.7303*b.g - 10.2697*b.b) / 255.0;
  fragColor = vec4(u0, w0, u1, w1);
}
`;

const NV12_COLORSPACE: VideoColorSpaceInit = {
    primaries: 'bt709',
    transfer: 'bt709',
    matrix: 'bt709',
    fullRange: false,
};

function align256(n: number): number {
    return (n + 255) & ~255;
}

// Max clientWaitSync polls before giving up and reading anyway (~ms each).
const MAX_FENCE_POLLS = 200;

interface Tier {
    w: number;
    h: number;
    yTex: WebGLTexture;
    yFbo: WebGLFramebuffer;
    uvTex: WebGLTexture;
    uvFbo: WebGLFramebuffer;
    yPbo: WebGLBuffer;  // PIXEL_PACK_BUFFER, W×H
    uvPbo: WebGLBuffer; // PIXEL_PACK_BUFFER, W×(H/2)
    yTmp: Uint8Array;   // W×H, bottom-up
    uvTmp: Uint8Array;  // W×(H/2), bottom-up
}

export class WebGlNv12Downscaler implements DownscalerLike {
    private readonly canvas: OffscreenCanvas;
    private gl: WebGL2RenderingContext | null = null;
    private yProgram: WebGLProgram | null = null;
    private uvProgram: WebGLProgram | null = null;
    private yOutW: WebGLUniformLocation | null = null;
    private uvOutW: WebGLUniformLocation | null = null;
    private positionBuffer: WebGLBuffer | null = null;
    private sourceTexture: WebGLTexture | null = null;
    private readonly tiers = new Map<string, Tier>();
    private fallback: MetadataDownscaler | null = null;
    private failed = false;
    private disposed = false;

    constructor() {
        this.canvas = new OffscreenCanvas(0, 0);
        const gl = this.canvas.getContext('webgl2', {
            alpha: false, antialias: false, depth: false, stencil: false,
            premultipliedAlpha: false, preserveDrawingBuffer: false,
        });
        if (!gl)
            throw new Error('WebGlNv12Downscaler: webgl2 context unavailable');
        this.gl = gl;
        this.initGl();
        infoLog?.log('WebGlNv12Downscaler: ready (async PBO NV12)');
    }

    async process(input: VideoFrame, layers: readonly LayerSpec[]): Promise<VideoFrame[]> {
        if (this.disposed || this.failed || !this.gl)
            return this.useFallback().process(input, layers);
        try {
            return await this.glProcess(this.gl, input, layers);
        } catch (e) {
            warnLog?.log('WebGlNv12Downscaler.process failed — falling back to metadata:', e);
            this.failed = true;
            return this.useFallback().process(input, layers);
        }
    }

    private async glProcess(gl: WebGL2RenderingContext, input: VideoFrame, layers: readonly LayerSpec[]): Promise<VideoFrame[]> {
        const results = new Array<VideoFrame | null>(layers.length).fill(null);
        // Upload the source once; every tier downscales directly from it (bilinear).
        gl.bindTexture(gl.TEXTURE_2D, this.sourceTexture);
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE,
            input as unknown as TexImageSource);

        // Issue every tier's draws + readPixels-into-PBO (non-blocking), then one
        // fence — so the GPU does all reads while the worker yields, instead of
        // blocking ~18 ms/frame in a synchronous readPixels.
        const tiers: Tier[] = [];
        gl.activeTexture(gl.TEXTURE0);
        gl.bindTexture(gl.TEXTURE_2D, this.sourceTexture);
        for (const { width: w, height: h } of layers) {
            if ((w & 3) !== 0 || (h & 1) !== 0)
                throw new Error(`WebGlNv12Downscaler: tier ${w}x${h} not packable (W%4||H%2)`);
            const tier = this.getTier(gl, w, h);
            this.issueTier(gl, tier);
            tiers.push(tier);
        }
        await this.awaitFence(gl);
        for (const [i, tier] of tiers.entries())
            results[i] = this.buildTier(gl, tier, input.timestamp);
        return results as VideoFrame[];
    }

    // Render Y + UV and start the async readback into this tier's PBOs.
    private issueTier(gl: WebGL2RenderingContext, tier: Tier): void {
        const { w, h } = tier;
        gl.useProgram(this.yProgram);
        gl.uniform1f(this.yOutW, w);
        gl.bindFramebuffer(gl.FRAMEBUFFER, tier.yFbo);
        gl.viewport(0, 0, w / 4, h);
        gl.drawArrays(gl.TRIANGLES, 0, 6);
        gl.bindBuffer(gl.PIXEL_PACK_BUFFER, tier.yPbo);
        gl.readPixels(0, 0, w / 4, h, gl.RGBA, gl.UNSIGNED_BYTE, 0);

        gl.useProgram(this.uvProgram);
        gl.uniform1f(this.uvOutW, w);
        gl.bindFramebuffer(gl.FRAMEBUFFER, tier.uvFbo);
        gl.viewport(0, 0, w / 4, h / 2);
        gl.drawArrays(gl.TRIANGLES, 0, 6);
        gl.bindBuffer(gl.PIXEL_PACK_BUFFER, tier.uvPbo);
        gl.readPixels(0, 0, w / 4, h / 2, gl.RGBA, gl.UNSIGNED_BYTE, 0);
        gl.bindBuffer(gl.PIXEL_PACK_BUFFER, null);
    }

    // Yield the worker until the GPU readback completes, instead of blocking.
    private async awaitFence(gl: WebGL2RenderingContext): Promise<void> {
        const sync = gl.fenceSync(gl.SYNC_GPU_COMMANDS_COMPLETE, 0);
        if (!sync) return; // unsupported → getBufferSubData below blocks instead
        gl.flush();
        try {
            for (let i = 0; i < MAX_FENCE_POLLS; i++) {
                const s = gl.clientWaitSync(sync, 0, 0);
                if (s === gl.ALREADY_SIGNALED || s === gl.CONDITION_SATISFIED)
                    return;
                await new Promise<void>(r => setTimeout(r, 1));
            }
        } finally {
            gl.deleteSync(sync);
        }
    }

    // Pull the readback out of the PBOs and assemble the NV12 VideoFrame.
    private buildTier(gl: WebGL2RenderingContext, tier: Tier, timestamp: number): VideoFrame {
        const { w, h } = tier;
        gl.bindBuffer(gl.PIXEL_PACK_BUFFER, tier.yPbo);
        gl.getBufferSubData(gl.PIXEL_PACK_BUFFER, 0, tier.yTmp);
        gl.bindBuffer(gl.PIXEL_PACK_BUFFER, tier.uvPbo);
        gl.getBufferSubData(gl.PIXEL_PACK_BUFFER, 0, tier.uvTmp);
        gl.bindBuffer(gl.PIXEL_PACK_BUFFER, null);

        // Assemble NV12 (256-aligned stride), reversing rows: readPixels is
        // bottom-up, NV12 is top-down.
        const stride = align256(w);
        const uvBase = stride * h;
        const nv12 = new Uint8Array(new ArrayBuffer(uvBase + stride * (h / 2)));
        for (let r = 0; r < h; r++)
            nv12.set(tier.yTmp.subarray((h - 1 - r) * w, (h - 1 - r) * w + w), r * stride);
        const uvRows = h / 2;
        for (let r = 0; r < uvRows; r++)
            nv12.set(tier.uvTmp.subarray((uvRows - 1 - r) * w, (uvRows - 1 - r) * w + w), uvBase + r * stride);

        return new VideoFrame(nv12, {
            format: 'NV12',
            codedWidth: w,
            codedHeight: h,
            timestamp,
            colorSpace: NV12_COLORSPACE,
            layout: [{ offset: 0, stride }, { offset: uvBase, stride }],
        });
    }

    private getTier(gl: WebGL2RenderingContext, w: number, h: number): Tier {
        const key = `${w}x${h}`;
        const existing = this.tiers.get(key);
        if (existing) return existing;

        const yTex = this.fboTexture(gl, w / 4, h);
        const yFbo = this.attachFbo(gl, yTex);
        const uvTex = this.fboTexture(gl, w / 4, h / 2);
        const uvFbo = this.attachFbo(gl, uvTex);
        const tier: Tier = {
            w, h, yTex, yFbo, uvTex, uvFbo,
            yPbo: this.packBuffer(gl, w * h),
            uvPbo: this.packBuffer(gl, w * (h / 2)),
            yTmp: new Uint8Array(w * h),
            uvTmp: new Uint8Array(w * (h / 2)),
        };
        this.tiers.set(key, tier);
        return tier;
    }

    private packBuffer(gl: WebGL2RenderingContext, size: number): WebGLBuffer {
        const buf = gl.createBuffer();
        gl.bindBuffer(gl.PIXEL_PACK_BUFFER, buf);
        gl.bufferData(gl.PIXEL_PACK_BUFFER, size, gl.STREAM_READ);
        gl.bindBuffer(gl.PIXEL_PACK_BUFFER, null);
        return buf;
    }

    private fboTexture(gl: WebGL2RenderingContext, w: number, h: number): WebGLTexture {
        const tex = createTexture(gl);
        gl.bindTexture(gl.TEXTURE_2D, tex);
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, w, h, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
        return tex;
    }

    private attachFbo(gl: WebGL2RenderingContext, tex: WebGLTexture): WebGLFramebuffer {
        const fbo = createFbo(gl);
        gl.bindFramebuffer(gl.FRAMEBUFFER, fbo);
        gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, tex, 0);
        if (gl.checkFramebufferStatus(gl.FRAMEBUFFER) !== gl.FRAMEBUFFER_COMPLETE)
            throw new Error('WebGlNv12Downscaler: incomplete framebuffer');
        return fbo;
    }

    private initGl(): void {
        const gl = this.gl!;
        this.yProgram = createProgram(gl, VERTEX_SHADER, Y_FRAGMENT);
        this.uvProgram = createProgram(gl, VERTEX_SHADER, UV_FRAGMENT);
        this.yOutW = gl.getUniformLocation(this.yProgram, 'u_outW');
        this.uvOutW = gl.getUniformLocation(this.uvProgram, 'u_outW');
        this.positionBuffer = setupFullScreenQuad(gl);
        gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, 1);
        gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, 0);
        this.sourceTexture = createTexture(gl);
        gl.useProgram(this.yProgram);
        gl.uniform1i(gl.getUniformLocation(this.yProgram, 'u_src'), 0);
        gl.useProgram(this.uvProgram);
        gl.uniform1i(gl.getUniformLocation(this.uvProgram, 'u_src'), 0);
    }

    private useFallback(): MetadataDownscaler {
        return (this.fallback ??= new MetadataDownscaler());
    }

    dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        const gl = this.gl;
        if (gl) {
            for (const t of this.tiers.values()) {
                gl.deleteTexture(t.yTex);
                gl.deleteFramebuffer(t.yFbo);
                gl.deleteTexture(t.uvTex);
                gl.deleteFramebuffer(t.uvFbo);
                gl.deleteBuffer(t.yPbo);
                gl.deleteBuffer(t.uvPbo);
            }
            if (this.sourceTexture) gl.deleteTexture(this.sourceTexture);
            if (this.positionBuffer) gl.deleteBuffer(this.positionBuffer);
            if (this.yProgram) gl.deleteProgram(this.yProgram);
            if (this.uvProgram) gl.deleteProgram(this.uvProgram);
            gl.getExtension('WEBGL_lose_context')?.loseContext();
        }
        this.tiers.clear();
        this.gl = null;
        this.canvas.width = 0;
        this.canvas.height = 0;
        this.fallback?.dispose();
        this.fallback = null;
    }
}
