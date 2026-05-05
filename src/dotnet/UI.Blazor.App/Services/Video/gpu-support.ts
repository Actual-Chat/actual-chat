import { getLogs } from 'logging';

const { debugLog } = getLogs('VideoSegmentation');

/**
 * GPU Backend Detection and Support
 * Detects available GPU backends for ONNX Runtime Web
 */

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

/**
 * Detect available GPU backends for ONNX Runtime Web
 * Tests actual ONNX Runtime backend availability, not just browser APIs
 */
export async function detectGPUBackends(): Promise<GPUBackendSupport> {
    const support: GPUBackendSupport = {
        webgpu: false,
        webgl: false,
        wasm: true, // WASM is always available as CPU fallback
        recommended: 'wasm',
        details: {}
    };

    // Test WebGPU support - check if browser supports it
    try {
        if ('gpu' in navigator) {
            const adapter = await navigator.gpu.requestAdapter();
            if (adapter) {
                // Check importExternalTexture support without allocating a device.
                // Creating a device here wastes GPU memory that iOS Safari needs
                // for the worker — causes OOM when ONNX Runtime requests its own device.
                const hasImportExternalTexture = typeof GPUDevice !== 'undefined'
                    && typeof GPUDevice.prototype.importExternalTexture === 'function';
                if (hasImportExternalTexture) {
                    support.webgpu = true;
                    const hasBgra8Storage = adapter.features.has('bgra8unorm-storage');
                    support.details.webgpu =
                        `Browser WebGPU available (importExternalTexture supported, bgra8unorm-storage=${hasBgra8Storage})`;
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

    // Test WebGL support
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

    // WASM is always available
    support.details.wasm = 'CPU fallback always available';

    // Determine recommended backend based on what's actually likely to work
    // Prefer WebGPU > WebGL > WASM for performance
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
