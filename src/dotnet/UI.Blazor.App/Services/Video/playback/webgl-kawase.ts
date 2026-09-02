// Off-thread bg backdrop via WebGL2 dual-Kawase pyramid blur — a direct
// port of the WebGPU `applyFullFrameBlur` pipeline in
// `../webgpu/blur.ts`, with the segmentation-mask path stripped (the
// receiver backdrop blurs the whole frame, no person matte needed). Lives
// next to `webgl-bg.ts` (separable Gaussian) so the two algorithms can be
// compared by flipping `?bgBlur=webgl` vs `?bgBlur=webgl-kawase`.
//
// Pipeline mirrors the WebGPU side:
//   • Pyramid sized at the *frame* dimensions, levels 0..N-1 where each
//     level halves its parent's dimensions.
//   • Downsample chain: source frame → pyramid[1] → pyramid[2] → … via a
//     5-tap Kawase (centre + 4 diagonals) at every step.
//   • Upsample chain: pyramid[N-1] → pyramid[N-2] → … → pyramid[0] via the
//     9-tap dual-Kawase tent (centre + 4 diagonals + 4 axis).
//   • Final pass: copy pyramid[0] to the swap-chain canvas at the BG
//     canvas's natural size (64 × N). CSS object-cover handles the upscale
//     to fill the tile.
//
// The mask-less downsample/upsample collapse to a uniform Kawase blur —
// same fixed weights as the WebGPU collapse used by `getZeroMaskTexture()`.

import { frameHeight, frameWidth, type FrameSource } from 'web-codecs-compat/init';
import { BgBlurPerfTracker } from '../services/bg-blur-stats';
import { createFbo, createProgram, createTexture, setupFullScreenQuad } from './webgl-helpers';
import { getLogs } from 'logging';

const { warnLog } = getLogs('VideoWebGPU');

// Matches WebGPU's `cachedLevels = 4` for blurStrength ≥ 20.
const PYRAMID_LEVELS = 4;
// Output canvas width — same target as the Gaussian path so both paths
// post-process the same pixel budget. CSS object-cover scales to fill.
const BG_CANVAS_WIDTH = 64;
// WebGPU passes `blurStrength * 0.5` as the offset to `getOffsetBuffer`,
// which writes `offset = strength / targetSize`. We reproduce that here.
const OFFSET_MULTIPLIER = 0.5;

const VERTEX_SHADER = `#version 300 es
layout(location = 0) in vec2 a_position;
out vec2 v_uv;
void main() {
  v_uv = (a_position + 1.0) * 0.5;
  gl_Position = vec4(a_position, 0.0, 1.0);
}
`;

// 5-tap Kawase downsample: centre + 4 diagonals at ±offset, weighted
// (4, 1, 1, 1, 1) / 8. Matches the WebGPU mask-less collapse where the
// segmentation-aware weights reduce to these constants.
const DOWNSAMPLE_FRAGMENT = `#version 300 es
precision mediump float;
in vec2 v_uv;
uniform sampler2D u_texture;
uniform vec2 u_offset;
out vec4 fragColor;
void main() {
  vec2 d = u_offset;
  vec4 c0 = texture(u_texture, v_uv);
  vec4 c1 = texture(u_texture, v_uv + vec2(-d.x, -d.y));
  vec4 c2 = texture(u_texture, v_uv + vec2( d.x, -d.y));
  vec4 c3 = texture(u_texture, v_uv + vec2(-d.x,  d.y));
  vec4 c4 = texture(u_texture, v_uv + vec2( d.x,  d.y));
  fragColor = (c0 * 4.0 + c1 + c2 + c3 + c4) / 8.0;
}
`;

// 9-tap dual-Kawase upsample (tent): centre × 4, 4 diagonals × 2, 4 axis × 1.
// Weights sum to 16 → divide by 16. Axis taps sit at 2 × offset.
const UPSAMPLE_FRAGMENT = `#version 300 es
precision mediump float;
in vec2 v_uv;
uniform sampler2D u_texture;
uniform vec2 u_offset;
out vec4 fragColor;
void main() {
  vec2 d = u_offset;
  vec2 a = d * 2.0;
  vec4 c0  = texture(u_texture, v_uv);
  vec4 cd0 = texture(u_texture, v_uv + vec2(-d.x, -d.y));
  vec4 cd1 = texture(u_texture, v_uv + vec2( d.x, -d.y));
  vec4 cd2 = texture(u_texture, v_uv + vec2(-d.x,  d.y));
  vec4 cd3 = texture(u_texture, v_uv + vec2( d.x,  d.y));
  vec4 ca0 = texture(u_texture, v_uv + vec2(-a.x, 0.0));
  vec4 ca1 = texture(u_texture, v_uv + vec2( a.x, 0.0));
  vec4 ca2 = texture(u_texture, v_uv + vec2(0.0, -a.y));
  vec4 ca3 = texture(u_texture, v_uv + vec2(0.0,  a.y));
  vec4 sum = c0 * 4.0
           + (cd0 + cd1 + cd2 + cd3) * 2.0
           + (ca0 + ca1 + ca2 + ca3);
  fragColor = sum / 16.0;
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

export class WebGlKawaseBgRenderer {
    private gl: WebGL2RenderingContext | null = null;
    private readonly perf = new BgBlurPerfTracker('webgl-kawase');
    private isDisposed = false;
    // Stored so dispose() can detach it before its own loseContext() — that call
    // dispatches the same event, and answering it would log an ordinary teardown
    // as a context loss.
    private readonly onContextLost = (): void => {
        warnLog?.log('WebGlKawaseBgRenderer: context lost');
        this.gl = null;
    };

    private downsampleProgram: WebGLProgram | null = null;
    private upsampleProgram: WebGLProgram | null = null;
    private copyProgram: WebGLProgram | null = null;
    private positionBuffer: WebGLBuffer | null = null;

    private sourceTexture: WebGLTexture | null = null;
    private readonly pyramidTex: WebGLTexture[] = [];
    private readonly pyramidFbo: WebGLFramebuffer[] = [];
    private allocFrameW = 0;
    private allocFrameH = 0;

    private uDownsampleTex: WebGLUniformLocation | null = null;
    private uDownsampleOffset: WebGLUniformLocation | null = null;
    private uUpsampleTex: WebGLUniformLocation | null = null;
    private uUpsampleOffset: WebGLUniformLocation | null = null;
    private uCopyTex: WebGLUniformLocation | null = null;

    constructor(private readonly canvas: OffscreenCanvas) {
        const gl = canvas.getContext('webgl2', {
            alpha: false,
            antialias: false,
            depth: false,
            stencil: false,
            premultipliedAlpha: false,
            preserveDrawingBuffer: false,
        });
        if (!gl) {
            warnLog?.log('WebGlKawaseBgRenderer: webgl2 context unavailable');
            return;
        }

        // Without this the renderer goes silently black on a lost context and the
        // logs give no way to tell that apart from an ordinary teardown.
        canvas.addEventListener('webglcontextlost', this.onContextLost);
        try {
            this.gl = gl;
            this.initGl();
        } catch (e) {
            warnLog?.log('WebGlKawaseBgRenderer: init failed:', e);
            this.gl = null;
        }
    }

    dispose(): void {
        if (this.isDisposed)
            return;

        this.isDisposed = true;
        this.canvas.removeEventListener('webglcontextlost', this.onContextLost);
        const gl = this.gl;
        if (!gl)
            return;

        if (this.sourceTexture) gl.deleteTexture(this.sourceTexture);
        for (const t of this.pyramidTex) gl.deleteTexture(t);
        for (const f of this.pyramidFbo) gl.deleteFramebuffer(f);
        this.pyramidTex.length = 0;
        this.pyramidFbo.length = 0;
        if (this.positionBuffer) gl.deleteBuffer(this.positionBuffer);
        if (this.downsampleProgram) gl.deleteProgram(this.downsampleProgram);
        if (this.upsampleProgram) gl.deleteProgram(this.upsampleProgram);
        if (this.copyProgram) gl.deleteProgram(this.copyProgram);
        gl.getExtension('WEBGL_lose_context')?.loseContext();
        this.gl = null;
    }

    render(frame: FrameSource, blurStrength = 20): boolean {
        if (this.isDisposed)
            return false;

        const gl = this.gl;
        if (!gl)
            return false;

        const fw = frameWidth(frame);
        const fh = frameHeight(frame);
        if (fw <= 0 || fh <= 0)
            return false;

        const targetW = BG_CANVAS_WIDTH;
        const targetH = Math.max(1, Math.round(targetW * fh / fw));
        try {
            const t0 = performance.now();
            if (this.canvas.width !== targetW || this.canvas.height !== targetH) {
                this.canvas.width = targetW;
                this.canvas.height = targetH;
            }
            if (fw !== this.allocFrameW || fh !== this.allocFrameH)
                this.allocPyramid(fw, fh);

            // Upload VideoFrame to source texture (matches Gaussian path's
            // UNPACK_FLIP_Y_WEBGL=1 from initGl so a non-flipping VS reads
            // pixels right-way-up).
            gl.bindTexture(gl.TEXTURE_2D, this.sourceTexture);
            gl.texImage2D(
                gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE,
                frame as unknown as TexImageSource);

            // Downsample chain. Level 1's source is the uploaded frame;
            // every deeper level reads its parent's pyramid texture.
            gl.useProgram(this.downsampleProgram);
            gl.activeTexture(gl.TEXTURE0);
            gl.uniform1i(this.uDownsampleTex, 0);
            for (let level = 1; level < PYRAMID_LEVELS; level++) {
                const dstW = Math.max(1, fw >> level);
                const dstH = Math.max(1, fh >> level);
                const srcTex = level === 1 ? this.sourceTexture : this.pyramidTex[level - 1];
                gl.bindFramebuffer(gl.FRAMEBUFFER, this.pyramidFbo[level]);
                gl.viewport(0, 0, dstW, dstH);
                gl.bindTexture(gl.TEXTURE_2D, srcTex);
                gl.uniform2f(
                    this.uDownsampleOffset,
                    (blurStrength * OFFSET_MULTIPLIER) / dstW,
                    (blurStrength * OFFSET_MULTIPLIER) / dstH,
                );
                gl.drawArrays(gl.TRIANGLES, 0, 6);
            }

            // Upsample chain — deepest mip back to pyramid[0]. Offset is in
            // the destination's UV scale (matches WebGPU `frameW >> (level-1)`).
            gl.useProgram(this.upsampleProgram);
            gl.uniform1i(this.uUpsampleTex, 0);
            for (let level = PYRAMID_LEVELS - 1; level > 0; level--) {
                const dstW = Math.max(1, fw >> (level - 1));
                const dstH = Math.max(1, fh >> (level - 1));
                gl.bindFramebuffer(gl.FRAMEBUFFER, this.pyramidFbo[level - 1]);
                gl.viewport(0, 0, dstW, dstH);
                gl.bindTexture(gl.TEXTURE_2D, this.pyramidTex[level]);
                gl.uniform2f(
                    this.uUpsampleOffset,
                    (blurStrength * OFFSET_MULTIPLIER) / dstW,
                    (blurStrength * OFFSET_MULTIPLIER) / dstH,
                );
                gl.drawArrays(gl.TRIANGLES, 0, 6);
            }

            // Composite-equivalent: copy pyramid[0] (frame-sized blurred) to
            // the swap-chain canvas. WebGPU's composite pass also reads the
            // original frame, but the mask-less path collapses to `mix(blur,
            // orig, 0) = blur`, so a straight copy is equivalent.
            gl.useProgram(this.copyProgram);
            gl.uniform1i(this.uCopyTex, 0);
            gl.bindFramebuffer(gl.FRAMEBUFFER, null);
            gl.viewport(0, 0, targetW, targetH);
            gl.bindTexture(gl.TEXTURE_2D, this.pyramidTex[0]);
            gl.drawArrays(gl.TRIANGLES, 0, 6);

            this.perf.sample(performance.now() - t0);
            return true;
        } catch (e) {
            warnLog?.log('WebGlKawaseBgRenderer render failed:', e);
            return false;
        }
    }

    // Private methods

    private initGl(): void {
        const gl = this.gl!;
        this.downsampleProgram = createProgram(gl, VERTEX_SHADER, DOWNSAMPLE_FRAGMENT);
        this.upsampleProgram = createProgram(gl, VERTEX_SHADER, UPSAMPLE_FRAGMENT);
        this.copyProgram = createProgram(gl, VERTEX_SHADER, COPY_FRAGMENT);
        this.uDownsampleTex = gl.getUniformLocation(this.downsampleProgram, 'u_texture');
        this.uDownsampleOffset = gl.getUniformLocation(this.downsampleProgram, 'u_offset');
        this.uUpsampleTex = gl.getUniformLocation(this.upsampleProgram, 'u_texture');
        this.uUpsampleOffset = gl.getUniformLocation(this.upsampleProgram, 'u_offset');
        this.uCopyTex = gl.getUniformLocation(this.copyProgram, 'u_texture');

        this.positionBuffer = setupFullScreenQuad(gl);

        gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, 1);
        gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, 0);

        this.sourceTexture = createTexture(gl);
        for (let i = 0; i < PYRAMID_LEVELS; i++) {
            this.pyramidTex.push(createTexture(gl));
            this.pyramidFbo.push(createFbo(gl));
        }
    }

    private allocPyramid(fw: number, fh: number): void {
        const gl = this.gl!;
        // Same trap as the Gaussian path: allocate storage with a *sized*
        // internal format (RGBA8 — RGBA without size isn't color-renderable
        // on WebGL2), AND re-attach the texture to the FBO *after* the
        // level-0 image exists. Attachments registered against an unsized
        // texture come back as MISSING_ATTACHMENT and storage allocated
        // later does not retroactively populate the slot.
        for (let level = 0; level < PYRAMID_LEVELS; level++) {
            const w = Math.max(1, fw >> level);
            const h = Math.max(1, fh >> level);
            const tex = this.pyramidTex[level];
            const fbo = this.pyramidFbo[level];
            gl.bindTexture(gl.TEXTURE_2D, tex);
            gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, w, h, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
            gl.bindFramebuffer(gl.FRAMEBUFFER, fbo);
            gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, tex, 0);
        }
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
        this.allocFrameW = fw;
        this.allocFrameH = fh;
    }
}
