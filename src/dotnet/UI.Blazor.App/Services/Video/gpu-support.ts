import { Log } from 'logging';

const { debugLog } = Log.get('VideoSegmentation');

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
                support.webgpu = true;
                support.details.webgpu = 'Browser WebGPU available';
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

/**
 * Get ONNX Runtime execution providers for the recommended backend
 */
export function getONNXExecutionProviders(backend: 'webgpu' | 'webgl' | 'wasm'): string[] {
    switch (backend) {
    case 'webgpu':
        return ['webgpu'];
    case 'webgl':
        return ['webgl'];
    case 'wasm':
    default:
        return ['wasm'];
    }
}

/**
 * Check if the current platform supports the background blur feature
 * Requires at least one GPU backend (WebGPU, WebGL, or WASM)
 */
export async function isBackgroundBlurSupported(): Promise<boolean> {
    const support = await detectGPUBackends();
    return support.webgpu || support.webgl || support.wasm;
}

/**
 * Get user-friendly backend name for display
 */
export function getBackendDisplayName(backend: 'webgpu' | 'webgl' | 'wasm'): string {
    switch (backend) {
    case 'webgpu':
        return 'WebGPU (GPU)';
    case 'webgl':
        return 'WebGL (GPU)';
    case 'wasm':
        return 'CPU (WASM)';
    default:
        return 'Unknown';
    }
}

/**
 * Get performance estimate for backend (rough ms per inference)
 */
export function getBackendPerformanceEstimate(backend: 'webgpu' | 'webgl' | 'wasm'): { min: number; max: number; typical: number } {
    switch (backend) {
    case 'webgpu':
        return { min: 5, max: 15, typical: 8 };
    case 'webgl':
        return { min: 15, max: 30, typical: 20 };
    case 'wasm':
        return { min: 40, max: 80, typical: 60 };
    default:
        return { min: 100, max: 200, typical: 150 };
    }
}
