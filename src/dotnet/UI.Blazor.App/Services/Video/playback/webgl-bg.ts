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

import { BgBlurPerfTracker } from '../services/bg-blur-stats';
import { getLogs } from 'logging';

const { warnLog } = getLogs('VideoWebGPU');

const BG_CANVAS_WIDTH = 64;
// Gaussian sigma (in target-pixel units). 8 ≈ ctx.filter='blur(3px)' on
// the same 64×N canvas perceptually; tunable without recompile.
const BLUR_RADIUS_PX = 8;
// Compile-time loop cap; the runtime branch inside the loop skips taps
// beyond ceil(radius). Matches gregblur's MAX_SAMPLES.
const MAX_SAMPLES = 16;

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
    private disposed = false;

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
        try {
            this.gl = gl;
            this.initGl();
        } catch (e) {
            warnLog?.log('WebGlBgRenderer: init failed:', e);
            this.gl = null;
        }
    }

    render(frame: VideoFrame): boolean {
        if (this.disposed) return false;
        const gl = this.gl;
        if (!gl) return false;
        const fw = frame.displayWidth;
        const fh = frame.displayHeight;
        if (fw <= 0 || fh <= 0) return false;
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

            // Pass 2: ping → pong, horizontal Gaussian.
            gl.bindFramebuffer(gl.FRAMEBUFFER, this.pongFbo);
            gl.viewport(0, 0, targetW, targetH);
            gl.useProgram(this.blurProgram);
            gl.activeTexture(gl.TEXTURE0);
            gl.bindTexture(gl.TEXTURE_2D, this.pingTexture);
            gl.uniform1i(this.uBlurTexture, 0);
            gl.uniform2f(this.uBlurTexelSize, 1 / targetW, 1 / targetH);
            gl.uniform2f(this.uBlurDirection, 1, 0);
            gl.uniform1f(this.uBlurRadius, BLUR_RADIUS_PX);
            gl.drawArrays(gl.TRIANGLES, 0, 6);

            // Pass 3: pong → canvas swap chain, vertical Gaussian.
            gl.bindFramebuffer(gl.FRAMEBUFFER, null);
            gl.viewport(0, 0, targetW, targetH);
            gl.bindTexture(gl.TEXTURE_2D, this.pongTexture);
            gl.uniform1i(this.uBlurTexture, 0);
            gl.uniform2f(this.uBlurDirection, 0, 1);
            gl.drawArrays(gl.TRIANGLES, 0, 6);

            this.perf.sample(performance.now() - t0);
            return true;
        } catch (e) {
            warnLog?.log('WebGlBgRenderer render failed:', e);
            return false;
        }
    }

    dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        const gl = this.gl;
        if (!gl) return;
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

    private initGl(): void {
        const gl = this.gl!;
        this.copyProgram = createProgram(gl, VERTEX_SHADER, COPY_FRAGMENT);
        this.blurProgram = createProgram(gl, VERTEX_SHADER, BLUR_FRAGMENT);
        this.uCopyTexture = gl.getUniformLocation(this.copyProgram, 'u_texture');
        this.uBlurTexture = gl.getUniformLocation(this.blurProgram, 'u_texture');
        this.uBlurTexelSize = gl.getUniformLocation(this.blurProgram, 'u_texelSize');
        this.uBlurDirection = gl.getUniformLocation(this.blurProgram, 'u_direction');
        this.uBlurRadius = gl.getUniformLocation(this.blurProgram, 'u_radius');

        this.positionBuffer = requireGlObject(gl.createBuffer(), 'position buffer');
        gl.bindBuffer(gl.ARRAY_BUFFER, this.positionBuffer);
        gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([
            -1, -1, 1, -1, -1, 1,
            -1, 1, 1, -1, 1, 1,
        ]), gl.STATIC_DRAW);
        // Both programs declare `a_position` at layout(location=0); enable the
        // attribute once and reuse across draws.
        gl.enableVertexAttribArray(0);
        gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0);

        // Upload-time Y flip so a non-flipping VS reads frames right-way-up.
        gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, 1);
        gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, 0);

        this.sourceTexture = createTexture(gl);
        this.pingTexture = createTexture(gl);
        this.pongTexture = createTexture(gl);
        this.pingFbo = createFbo(gl, this.pingTexture);
        this.pongFbo = createFbo(gl, this.pongTexture);
    }

    private allocPingPong(w: number, h: number): void {
        const gl = this.gl!;
        for (const tex of [this.pingTexture, this.pongTexture]) {
            gl.bindTexture(gl.TEXTURE_2D, tex);
            gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, w, h, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
        }
        this.allocW = w;
        this.allocH = h;
    }
}

// WebGL create*() return `T | null` per spec on OOM / context-lost. The
// runtime can produce null, but lint propagates the conservative non-null
// narrowing of getContext through to these calls. Funnel through a helper
// so the explicit null check sits at one boundary instead of three.
function requireGlObject<T>(value: T | null, name: string): T {
    if (value === null)
        throw new Error(`WebGlBgRenderer: ${name} unavailable`);
    return value;
}

function createProgram(gl: WebGL2RenderingContext, vs: string, fs: string): WebGLProgram {
    let vsObj: WebGLShader | null = null;
    let fsObj: WebGLShader | null = null;
    let prog: WebGLProgram | null = null;
    try {
        vsObj = compileShader(gl, gl.VERTEX_SHADER, vs);
        fsObj = compileShader(gl, gl.FRAGMENT_SHADER, fs);
        prog = requireGlObject(gl.createProgram(), 'program');
        gl.attachShader(prog, vsObj);
        gl.attachShader(prog, fsObj);
        gl.linkProgram(prog);
        if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) {
            const log = gl.getProgramInfoLog(prog) ?? 'unknown link error';
            throw new Error(`program link failed: ${log}`);
        }
        return prog;
    } catch (e) {
        if (prog) gl.deleteProgram(prog);
        throw e;
    } finally {
        if (vsObj) gl.deleteShader(vsObj);
        if (fsObj) gl.deleteShader(fsObj);
    }
}

function compileShader(gl: WebGL2RenderingContext, type: number, src: string): WebGLShader {
    const sh = requireGlObject(gl.createShader(type), 'shader');
    gl.shaderSource(sh, src);
    gl.compileShader(sh);
    if (!gl.getShaderParameter(sh, gl.COMPILE_STATUS)) {
        const log = gl.getShaderInfoLog(sh) ?? 'unknown compile error';
        gl.deleteShader(sh);
        const stage = type === gl.VERTEX_SHADER ? 'vertex' : 'fragment';
        throw new Error(`${stage} shader compile failed: ${log}`);
    }
    return sh;
}

function createTexture(gl: WebGL2RenderingContext): WebGLTexture {
    const tex = requireGlObject(gl.createTexture(), 'texture');
    gl.bindTexture(gl.TEXTURE_2D, tex);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    return tex;
}

function createFbo(gl: WebGL2RenderingContext, tex: WebGLTexture): WebGLFramebuffer {
    const fbo = requireGlObject(gl.createFramebuffer(), 'framebuffer');
    gl.bindFramebuffer(gl.FRAMEBUFFER, fbo);
    gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, tex, 0);
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    return fbo;
}
