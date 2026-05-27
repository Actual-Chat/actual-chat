// Shared WebGL2 plumbing for the off-thread bg-blur renderers
// (`webgl-bg.ts` separable-Gaussian, `webgl-kawase.ts` dual-Kawase).
// Both renderers share the same setup pattern — full-screen triangle pair,
// LINEAR-filtered CLAMP_TO_EDGE textures, color FBOs — so extract once.

// WebGL `create*()` returns `T | null` per spec on OOM / context-lost. The
// runtime can produce null even though TS narrows it away because
// `getContext` is typed as non-nullable. Funnel through a single helper so
// the explicit null check sits at one boundary.
export function requireGlObject<T>(value: T | null, name: string): T {
    if (value === null)
        throw new Error(`WebGL: ${name} unavailable`);
    return value;
}

export function compileShader(gl: WebGL2RenderingContext, type: number, src: string): WebGLShader {
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

export function createProgram(gl: WebGL2RenderingContext, vs: string, fs: string): WebGLProgram {
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

export function createTexture(gl: WebGL2RenderingContext): WebGLTexture {
    const tex = requireGlObject(gl.createTexture(), 'texture');
    gl.bindTexture(gl.TEXTURE_2D, tex);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    return tex;
}

// Allocates an empty FBO. Caller MUST attach a level-0-allocated texture
// via `framebufferTexture2D` after `texImage2D` storage exists; attaching
// here, before storage, leaves the FBO in MISSING_ATTACHMENT on Chromium
// and the deferred storage allocation does not retroactively populate the
// attachment slot.
export function createFbo(gl: WebGL2RenderingContext): WebGLFramebuffer {
    return requireGlObject(gl.createFramebuffer(), 'framebuffer');
}

// Standard full-screen triangle-pair VBO (two CCW triangles, NDC). Both
// blur paths declare `a_position` at layout(location=0) and use the same
// 6-vertex draw; this helper sets up the buffer + attribute pointer once.
export function setupFullScreenQuad(gl: WebGL2RenderingContext): WebGLBuffer {
    const buf = requireGlObject(gl.createBuffer(), 'position buffer');
    gl.bindBuffer(gl.ARRAY_BUFFER, buf);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([
        -1, -1, 1, -1, -1, 1,
        -1, 1, 1, -1, 1, 1,
    ]), gl.STATIC_DRAW);
    gl.enableVertexAttribArray(0);
    gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0);
    return buf;
}
