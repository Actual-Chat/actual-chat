import { getLogs } from 'logging';

const { debugLog } = getLogs('VideoSegmentation');

// Detects available GPU backends for ONNX Runtime Web.

export interface GPUBackendSupport {
  webgpu: boolean;
  webgl: boolean;
  wasm: boolean;
  recommended: 'webgpu' | 'webgl' | 'wasm';
  details: {
    webgpu?: string;
    webgl?: string;
    wasm?: string;
  };
}

export async function detectGPUBackends(): Promise<GPUBackendSupport> {
    const support: GPUBackendSupport = {
        webgpu: false,
        webgl: false,
        wasm: true,
        recommended: 'wasm',
        details: {}
    };

    try {
        if ('gpu' in navigator) {
            const adapter = await navigator.gpu.requestAdapter();
            if (adapter) {
                // Probe via prototype only — allocating a device here OOMs iOS
                // Safari when ONNX Runtime later requests its own.
                const hasImportExternalTexture = typeof GPUDevice !== 'undefined'
                    && typeof GPUDevice.prototype.importExternalTexture === 'function';
                if (hasImportExternalTexture) {
                    support.webgpu = true;
                    support.details.webgpu = 'Browser WebGPU available (importExternalTexture supported)';
                } else {
                    support.details.webgpu = 'WebGPU adapter available but importExternalTexture not supported';
                }
            } else {
                support.details.webgpu = 'No WebGPU adapter available';
            }
        } else {
            support.details.webgpu = 'WebGPU API not available in browser';
        }
    } catch (error) {
        support.details.webgpu = `WebGPU error: ${error instanceof Error ? error.message : 'Unknown error'}`;
    }

    try {
        const canvas = document.createElement('canvas');
        const gl = canvas.getContext('webgl2') ?? canvas.getContext('webgl');
        if (gl) {
            support.webgl = true;
            const debugInfo = gl.getExtension('WEBGL_debug_renderer_info');
            if (debugInfo) {
                const renderer = gl.getParameter(debugInfo.UNMASKED_RENDERER_WEBGL) as string;
                const vendor = gl.getParameter(debugInfo.UNMASKED_VENDOR_WEBGL) as string;
                support.details.webgl = `Renderer: ${renderer}, Vendor: ${vendor}`;
            } else {
                support.details.webgl = 'WebGL context available';
            }
        } else {
            support.details.webgl = 'WebGL context not available';
        }
    } catch (error) {
        support.details.webgl = `WebGL error: ${error instanceof Error ? error.message : 'Unknown error'}`;
    }

    support.details.wasm = 'CPU fallback always available';

    if (support.webgpu) {
        support.recommended = 'webgpu';
    } else if (support.webgl) {
        support.recommended = 'webgl';
    } else {
        support.recommended = 'wasm';
    }

    debugLog?.log('Detected backends:', support);
    return support;
}

// Cached probe used by the render-backends to decide whether to transfer
// the bg-blur OffscreenCanvas to the player worker or stay on the WebGL
// main-thread painter. transferControlToOffscreen is one-shot, so we
// must commit to a single answer per canvas; caching the first probe
// result here keeps that decision stable for the session.
let webGpuSupportedPromise: Promise<boolean> | null = null;

export function isWebGpuSupported(): Promise<boolean> {
    webGpuSupportedPromise ??= (async () => {
        const support = await detectGPUBackends();
        return support.webgpu;
    })();
    return webGpuSupportedPromise;
}

// Synchronous, conservative probe — needed at VideoPlayer construction time
// when the bg-canvas transfer decision must be made before any frame paints.
// Checks only the API surface (navigator.gpu + importExternalTexture on the
// prototype) without requesting an adapter. False negatives are acceptable
// (we just keep the WebGL fallback painter); false positives are paid by
// BgBlurRenderer's `initFailed` path, which simply stops painting — never
// blocks the main thread.
export function isWebGpuLikelySupported(): boolean {
    try {
        const nav = globalThis.navigator as Navigator | undefined;
        if (!nav || !('gpu' in nav))
            return false;
        return typeof GPUDevice !== 'undefined'
            && typeof GPUDevice.prototype.importExternalTexture === 'function';
    } catch {
        return false;
    }
}
