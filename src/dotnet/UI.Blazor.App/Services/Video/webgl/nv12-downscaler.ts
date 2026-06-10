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
// readPixels both, assemble an NV12 buffer (rows reversed — readPixels is
// bottom-up; row stride 256-aligned on Chromium, tight on WebKit which ignores
// the custom layout stride), and wrap as a VideoFrame. Orientation/upload mirror
// webgl/downscaler.ts (UNPACK_FLIP_Y + non-flipping VS, verified upright).
//
// Self-falls-back to MetadataDownscaler on context loss / failure.

import { getLogs } from 'logging';
import { DeviceInfo } from 'device-info';
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

// Chromium's HW encoder wants a 256-aligned NV12 row stride to take its fast
// path; it honors the padded `stride` we pass in `layout`. WebKit ignores the
// custom stride and reads NV12 tightly packed, so the padding shears each row
// (green diagonal bands) and the UV offset lands in the wrong bytes (green
// blocks). Tight-pack (stride = width) for WebKit so its assumption matches.
function nv12Stride(w: number): number {
    return DeviceInfo.isWebKit ? w : (w + 255) & ~255;
}

interface Tier {
    yTex: WebGLTexture;
    yFbo: WebGLFramebuffer;
    uvTex: WebGLTexture;
    uvFbo: WebGLFramebuffer;
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
        infoLog?.log('WebGlNv12Downscaler: ready (readback NV12)');
    }

    async process(input: VideoFrame, layers: readonly LayerSpec[]): Promise<VideoFrame[]> {
        if (this.disposed || this.failed || !this.gl)
            return this.useFallback().process(input, layers);
        try {
            return this.glProcess(this.gl, input, layers);
        } catch (e) {
            warnLog?.log('WebGlNv12Downscaler.process failed — falling back to metadata:', e);
            this.failed = true;
            return this.useFallback().process(input, layers);
        }
    }

    private glProcess(gl: WebGL2RenderingContext, input: VideoFrame, layers: readonly LayerSpec[]): VideoFrame[] {
        const results = new Array<VideoFrame | null>(layers.length).fill(null);
        // Upload the source once; every tier downscales directly from it (bilinear).
        gl.bindTexture(gl.TEXTURE_2D, this.sourceTexture);
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE,
            input as unknown as TexImageSource);

        for (let i = 0; i < layers.length; i++) {
            const { width: w, height: h } = layers[i];
            if ((w & 3) !== 0 || (h & 1) !== 0)
                throw new Error(`WebGlNv12Downscaler: tier ${w}x${h} not packable (W%4||H%2)`);
            results[i] = this.renderTier(gl, input.timestamp, w, h);
        }
        return results as VideoFrame[];
    }

    private renderTier(gl: WebGL2RenderingContext, timestamp: number, w: number, h: number): VideoFrame {
        const tier = this.getTier(gl, w, h);
        gl.activeTexture(gl.TEXTURE0);
        gl.bindTexture(gl.TEXTURE_2D, this.sourceTexture);

        // Y pass → (W/4)×H, read back W×H bytes (bottom-up).
        gl.useProgram(this.yProgram);
        gl.uniform1f(this.yOutW, w);
        gl.bindFramebuffer(gl.FRAMEBUFFER, tier.yFbo);
        gl.viewport(0, 0, w / 4, h);
        gl.drawArrays(gl.TRIANGLES, 0, 6);
        gl.readPixels(0, 0, w / 4, h, gl.RGBA, gl.UNSIGNED_BYTE, tier.yTmp);

        // UV pass → (W/4)×(H/2), read back W×(H/2) bytes (bottom-up).
        gl.useProgram(this.uvProgram);
        gl.uniform1f(this.uvOutW, w);
        gl.bindFramebuffer(gl.FRAMEBUFFER, tier.uvFbo);
        gl.viewport(0, 0, w / 4, h / 2);
        gl.drawArrays(gl.TRIANGLES, 0, 6);
        gl.readPixels(0, 0, w / 4, h / 2, gl.RGBA, gl.UNSIGNED_BYTE, tier.uvTmp);

        // Assemble NV12, reversing rows: readPixels is bottom-up, NV12 is
        // top-down. Stride is 256-aligned on Chromium, tight on WebKit.
        const stride = nv12Stride(w);
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
            yTex, yFbo, uvTex, uvFbo,
            yTmp: new Uint8Array(w * h),
            uvTmp: new Uint8Array(w * (h / 2)),
        };
        this.tiers.set(key, tier);
        return tier;
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
