// Off-thread bg backdrop via WebGL2 separable Gaussian blur. Algorithm
// and shader structure adapted from the gregblur project
// (https://github.com/gregce/gregblur, Apache License 2.0) — the
// separable-Gaussian pass shape and the GLSL kernel shape are borrowed;
// segmentation-mask plumbing is stripped because the receiver backdrop
// blurs the whole frame, no person matte needed.
//
// Lives in the player worker against the same transferred OffscreenCanvas
// the WebGPU and Canvas2D paths use; renders a 64-px-wide blurred bitmap
// that CSS object-cover scales to fill the tile.

import { frameHeight, frameWidth, type FrameSource } from 'web-codecs-compat/init';
import { BgBlurPerfTracker } from '../services/bg-blur-stats';
import { createFbo, createProgram, createTexture, setupFullScreenQuad } from './webgl-helpers';
import { getLogs } from 'logging';

const { warnLog } = getLogs('VideoWebGPU');

const BG_CANVAS_WIDTH = 64;
// Gaussian sigma (in target-pixel units). Picks the per-pass kernel
// spread; effective radius after all iterations ≈ sigma * sqrt(N).
const BLUR_RADIUS_PX = 12;
// Compile-time loop cap; the runtime branch inside the loop skips taps
// beyond ceil(radius). Matches gregblur's MAX_SAMPLES.
const MAX_SAMPLES = 16;
// Number of H+V passes to chain. Each iteration convolves another
// Gaussian, compounding to a wider effective radius without enlarging
// the per-pass kernel (cheap because the canvas is tiny).
// 2 iterations roughly matches the visual softness of the WebGPU
// dual-Kawase path on this 64×N target.
const BLUR_ITERATIONS = 2;

const VERTEX_SHADER = `#version 300 es
layout(location = 0) in vec2 a_position;
out vec2 v_uv;
void main() {
  v_uv = (a_position + 1.0) * 0.5;
  gl_Position = vec4(a_position, 0.0, 1.0);
}
`;

// First pass: copies VideoFrame → ping FBO. The source texture is uploaded
// at full source dims with LINEAR filtering; the FBO is the target canvas
// size, so this draw doubles as the downsample.
const COPY_FRAGMENT = `#version 300 es
precision mediump float;
in vec2 v_uv;
uniform sampler2D u_texture;
out vec4 fragColor;
void main() {
  fragColor = texture(u_texture, v_uv);
}
`;

// Separable Gaussian. Direction selected per-pass via u_direction = (1,0)
// for horizontal, (0,1) for vertical. Mask path from gregblur removed.
const BLUR_FRAGMENT = `#version 300 es
precision mediump float;
in vec2 v_uv;
uniform sampler2D u_texture;
uniform vec2 u_texelSize;
uniform vec2 u_direction;
uniform float u_radius;
out vec4 fragColor;

void main() {
  float sigma = u_radius;
  float twoSigmaSq = 2.0 * sigma * sigma;
  float totalWeight = 0.0;
  vec3 result = vec3(0.0);
  int radius = int(min(${MAX_SAMPLES}.0, ceil(u_radius)));
  for (int i = -${MAX_SAMPLES}; i <= ${MAX_SAMPLES}; ++i) {
    float offset = float(i);
    if (abs(offset) > float(radius)) continue;
    float w = exp(-(offset * offset) / twoSigmaSq);
    vec2 c = v_uv + u_direction * u_texelSize * offset;
    result += texture(u_texture, c).rgb * w;
    totalWeight += w;
  }
  fragColor = vec4(result / max(totalWeight, 0.001), 1.0);
}
`;

export class WebGlBgRenderer {
    private gl: WebGL2RenderingContext | null = null;
    private readonly perf = new BgBlurPerfTracker('webgl');
    private isDisposed = false;
    // Stored so dispose() can detach it before its own loseContext() — that call
    // dispatches the same event, and answering it would log an ordinary teardown
    // as a context loss.
    private readonly onContextLost = (): void => {
        warnLog?.log('WebGlBgRenderer: context lost');
        this.gl = null;
    };

    private copyProgram: WebGLProgram | null = null;
    private blurProgram: WebGLProgram | null = null;
    private positionBuffer: WebGLBuffer | null = null;

    private sourceTexture: WebGLTexture | null = null;
    private pingTexture: WebGLTexture | null = null;
    private pongTexture: WebGLTexture | null = null;
    private pingFbo: WebGLFramebuffer | null = null;
    private pongFbo: WebGLFramebuffer | null = null;
    private allocW = 0;
    private allocH = 0;

    private uCopyTexture: WebGLUniformLocation | null = null;
    private uBlurTexture: WebGLUniformLocation | null = null;
    private uBlurTexelSize: WebGLUniformLocation | null = null;
    private uBlurDirection: WebGLUniformLocation | null = null;
    private uBlurRadius: WebGLUniformLocation | null = null;

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
            warnLog?.log('WebGlBgRenderer: webgl2 context unavailable');
            return;
        }

        // Without this the renderer goes silently black on a lost context and the
        // logs give no way to tell that apart from an ordinary teardown.
        canvas.addEventListener('webglcontextlost', this.onContextLost);
        try {
            this.gl = gl;
            this.initGl();
        } catch (e) {
            warnLog?.log('WebGlBgRenderer: init failed:', e);
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
        if (this.pingTexture) gl.deleteTexture(this.pingTexture);
        if (this.pongTexture) gl.deleteTexture(this.pongTexture);
        if (this.pingFbo) gl.deleteFramebuffer(this.pingFbo);
        if (this.pongFbo) gl.deleteFramebuffer(this.pongFbo);
        if (this.positionBuffer) gl.deleteBuffer(this.positionBuffer);
        if (this.copyProgram) gl.deleteProgram(this.copyProgram);
        if (this.blurProgram) gl.deleteProgram(this.blurProgram);
        gl.getExtension('WEBGL_lose_context')?.loseContext();
        this.gl = null;
    }

    render(frame: FrameSource): boolean {
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
            if (this.allocW !== targetW || this.allocH !== targetH)
                this.allocPingPong(targetW, targetH);

            // Upload VideoFrame to source texture. UNPACK_FLIP_Y_WEBGL=1 puts
            // texture row 0 at the top of the frame, so a non-flipping VS reads
            // pixels in their natural orientation. Set in initGl().
            gl.bindTexture(gl.TEXTURE_2D, this.sourceTexture);
            gl.texImage2D(
                gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE,
                frame as unknown as TexImageSource);

            // Pass 1: source → ping (LINEAR downsample to target dims).
            gl.bindFramebuffer(gl.FRAMEBUFFER, this.pingFbo);
            gl.viewport(0, 0, targetW, targetH);
            gl.useProgram(this.copyProgram);
            gl.activeTexture(gl.TEXTURE0);
            gl.bindTexture(gl.TEXTURE_2D, this.sourceTexture);
            gl.uniform1i(this.uCopyTexture, 0);
            gl.drawArrays(gl.TRIANGLES, 0, 6);

            // BLUR_ITERATIONS rounds of H then V; ping/pong alternates and
            // the LAST vertical pass renders straight to the canvas swap
            // chain. Source for the first H is the ping FBO populated by
            // the copy pass.
            gl.useProgram(this.blurProgram);
            gl.uniform2f(this.uBlurTexelSize, 1 / targetW, 1 / targetH);
            gl.uniform1f(this.uBlurRadius, BLUR_RADIUS_PX);
            gl.activeTexture(gl.TEXTURE0);
            gl.uniform1i(this.uBlurTexture, 0);
            let srcTex = this.pingTexture;
            let srcFbo = this.pingFbo;
            let dstFbo = this.pongFbo;
            for (let i = 0; i < BLUR_ITERATIONS; i++) {
                const isLast = i === BLUR_ITERATIONS - 1;

                // Horizontal pass into the alternate FBO.
                gl.bindFramebuffer(gl.FRAMEBUFFER, dstFbo);
                gl.viewport(0, 0, targetW, targetH);
                gl.bindTexture(gl.TEXTURE_2D, srcTex);
                gl.uniform2f(this.uBlurDirection, 1, 0);
                gl.drawArrays(gl.TRIANGLES, 0, 6);

                // Vertical pass: into the (now free) other FBO, unless this
                // is the last iteration — then straight to the canvas.
                const hOutputTex = dstFbo === this.pongFbo ? this.pongTexture : this.pingTexture;
                if (isLast) {
                    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
                } else {
                    gl.bindFramebuffer(gl.FRAMEBUFFER, srcFbo);
                }
                gl.viewport(0, 0, targetW, targetH);
                gl.bindTexture(gl.TEXTURE_2D, hOutputTex);
                gl.uniform2f(this.uBlurDirection, 0, 1);
                gl.drawArrays(gl.TRIANGLES, 0, 6);

                // Swap ping/pong roles for the next iteration's H pass.
                if (!isLast) {
                    const tmpFbo = srcFbo;
                    srcFbo = dstFbo;
                    dstFbo = tmpFbo;
                    srcTex = srcFbo === this.pingFbo ? this.pingTexture : this.pongTexture;
                }
            }

            this.perf.sample(performance.now() - t0);
            return true;
        } catch (e) {
            warnLog?.log('WebGlBgRenderer render failed:', e);
            return false;
        }
    }

    // Private methods

    private initGl(): void {
        const gl = this.gl!;
        this.copyProgram = createProgram(gl, VERTEX_SHADER, COPY_FRAGMENT);
        this.blurProgram = createProgram(gl, VERTEX_SHADER, BLUR_FRAGMENT);
        this.uCopyTexture = gl.getUniformLocation(this.copyProgram, 'u_texture');
        this.uBlurTexture = gl.getUniformLocation(this.blurProgram, 'u_texture');
        this.uBlurTexelSize = gl.getUniformLocation(this.blurProgram, 'u_texelSize');
        this.uBlurDirection = gl.getUniformLocation(this.blurProgram, 'u_direction');
        this.uBlurRadius = gl.getUniformLocation(this.blurProgram, 'u_radius');

        this.positionBuffer = setupFullScreenQuad(gl);

        // Upload-time Y flip so a non-flipping VS reads frames right-way-up.
        gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, 1);
        gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, 0);

        this.sourceTexture = createTexture(gl);
        this.pingTexture = createTexture(gl);
        this.pongTexture = createTexture(gl);
        // FBOs are allocated empty here and attached to their pyramid textures
        // in `allocPingPong` after the textures get a level-0 image — see the
        // comment there for why.
        this.pingFbo = createFbo(gl);
        this.pongFbo = createFbo(gl);
    }

    private allocPingPong(w: number, h: number): void {
        const gl = this.gl!;
        // (1) Allocate storage with a *sized* internal format (RGBA8). Unsized
        //     `gl.RGBA` isn't color-renderable on WebGL2; attaching such a
        //     texture leaves the FBO incomplete.
        // (2) Re-attach the texture to the FBO *after* storage exists. An
        //     earlier attachment made when the texture had no level-0 image
        //     registers as MISSING_ATTACHMENT on Chromium, and calling
        //     texImage2D later does not retroactively populate the slot.
        //     Rebinding here makes the FBO complete on first draw.
        const pairs: [WebGLTexture | null, WebGLFramebuffer | null][] = [
            [this.pingTexture, this.pingFbo],
            [this.pongTexture, this.pongFbo],
        ];
        for (const [tex, fbo] of pairs) {
            gl.bindTexture(gl.TEXTURE_2D, tex);
            gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, w, h, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
            gl.bindFramebuffer(gl.FRAMEBUFFER, fbo);
            gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, tex, 0);
        }
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
        this.allocW = w;
        this.allocH = h;
    }
}

