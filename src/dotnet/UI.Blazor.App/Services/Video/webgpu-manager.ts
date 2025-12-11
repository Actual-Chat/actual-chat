/**
 * Centralized WebGPU device ownership shared between ONNX Runtime, tensor utilities,
 * and blur pipelines. Ensures a single GPUDevice and shared sampler instance.
 */
export class WebGPUManager {
    private static device: GPUDevice | null = null;
    private static sampler: GPUSampler | null = null;
    private static initPromise: Promise<GPUDevice> | null = null;

    /**
     * Initialize the shared GPUDevice. ONNX Runtime should pass its device here to
     * eliminate duplicate device creation. Falls back to requesting a device if needed.
     */
    static async init(externalDevice?: GPUDevice): Promise<GPUDevice> {
        const existingDevice = this.device;
        if (existingDevice) {
            if (externalDevice && externalDevice !== existingDevice) {
                console.warn('[WebGPUManager] Ignoring secondary device initialization request.');
            }
            return existingDevice;
        }

        if (externalDevice) {
            this.attachDevice(externalDevice);
            return externalDevice;
        }

        if (!this.initPromise) {
            this.initPromise = (async () => {
                const navigatorRef = globalThis.navigator as Navigator | undefined;
                if (!navigatorRef?.gpu) {
                    throw new Error('WebGPU not available in this environment');
                }

                const adapter = await navigatorRef.gpu.requestAdapter();
                if (!adapter) {
                    throw new Error('Failed to acquire WebGPU adapter');
                }

                const createdDevice = await adapter.requestDevice();
                this.attachDevice(createdDevice);
                return createdDevice;
            })().finally(() => {
                this.initPromise = null;
            });
        }

        const pendingInit = this.initPromise;
        if (!pendingInit) {
            throw new Error('WebGPU initialization did not start correctly');
        }

        return pendingInit;
    }

    /**
     * Returns the shared GPUDevice. Throws if init() has not been called yet.
     */
    static get(): GPUDevice {
        if (!this.device) {
            throw new Error('WebGPUManager not initialized. Call WebGPUManager.init() first.');
        }
        return this.device;
    }

    /**
     * Exposes the default linear sampler created alongside the shared GPUDevice.
     */
    static getSampler(): GPUSampler {
        if (!this.sampler) {
            throw new Error('WebGPUManager sampler not initialized.');
        }
        return this.sampler;
    }

    static hasDevice(): boolean {
        return this.device !== null;
    }

    private static attachDevice(device: GPUDevice): void {
        this.device = device;
        this.sampler = device.createSampler({ magFilter: 'linear', minFilter: 'linear' });
    }
}
