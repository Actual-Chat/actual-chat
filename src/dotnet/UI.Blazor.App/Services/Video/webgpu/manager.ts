import { getLogs } from 'logging';

const { infoLog, warnLog, errorLog } = getLogs('VideoWebGPU');

export type DeviceLostListener = (info: GPUDeviceLostInfo) => void;

// Single shared GPUDevice + sampler across ONNX, tensor, blur, downscaler.
// Watches device.lost so consumers fail-fast instead of triggering Edge's
// per-frame OperationError storms against a dead device.
export class WebGPUManager {
    private static device: GPUDevice | null = null;
    private static sampler: GPUSampler | null = null;
    private static initPromise: Promise<GPUDevice> | null = null;
    private static lostListeners = new Set<DeviceLostListener>();
    private static lastLostInfo: GPUDeviceLostInfo | null = null;

    static async init(externalDevice?: GPUDevice): Promise<GPUDevice> {
        const existingDevice = this.device;
        if (existingDevice) {
            if (externalDevice && externalDevice !== existingDevice)
                warnLog?.log('Ignoring secondary device initialization request');
            return existingDevice;
        }

        if (externalDevice) {
            this.attachDevice(externalDevice);
            return externalDevice;
        }

        this.initPromise ??= (async () => {
            const navigatorRef = globalThis.navigator as Navigator | undefined;
            if (!navigatorRef?.gpu)
                throw new Error('WebGPU not available in this environment');

            const adapter = await navigatorRef.gpu.requestAdapter();
            if (!adapter)
                throw new Error('Failed to acquire WebGPU adapter');

            // bgra8unorm-storage lets the simulcast downscaler compute path
            // write directly into BGRA canvas textures (Chrome/Safari preferred
            // format). Downscaler falls back to render-pass when unsupported.
            const requiredFeatures: GPUFeatureName[] = [];
            if (adapter.features.has('bgra8unorm-storage'))
                requiredFeatures.push('bgra8unorm-storage');

            const createdDevice = await adapter.requestDevice({ requiredFeatures });
            this.attachDevice(createdDevice);
            return createdDevice;
        })().finally(() => {
            this.initPromise = null;
        });

        return this.initPromise;
    }

    static hasFeature(name: GPUFeatureName): boolean {
        return this.device?.features.has(name) ?? false;
    }

    static get(): GPUDevice {
        if (!this.device)
            throw new Error('WebGPUManager not initialized. Call WebGPUManager.init() first.');
        return this.device;
    }

    static getSampler(): GPUSampler {
        if (!this.sampler)
            throw new Error('WebGPUManager sampler not initialized.');
        return this.sampler;
    }

    static hasDevice(): boolean {
        return this.device !== null;
    }

    static getLastLostInfo(): GPUDeviceLostInfo | null {
        return this.lastLostInfo;
    }

    // Listener fires AFTER manager nulls device/sampler — consumers can
    // confirm via hasDevice(). Returns a disposer.
    static addLostListener(listener: DeviceLostListener): () => void {
        this.lostListeners.add(listener);
        return () => { this.lostListeners.delete(listener); };
    }

    // Private methods

    private static attachDevice(device: GPUDevice): void {
        this.device = device;
        this.sampler = device.createSampler({ magFilter: 'linear', minFilter: 'linear' });
        this.lastLostInfo = null;

        // D1: device baseline for freeze post-mortems.
        try {
            const features = Array.from(device.features).join(',');
            const limits = device.limits;
            infoLog?.log(
                `GPUDevice attached: features=[${features}] `
                + `maxBufferSize=${limits.maxBufferSize} `
                + `maxTextureDim2D=${limits.maxTextureDimension2D} `
                + `maxBindGroups=${limits.maxBindGroups}`);
        } catch (e) {
            warnLog?.log('attachDevice: feature/limit dump failed:', e);
        }

        // Surface async GPU validation errors — Chrome/Edge otherwise log them
        // to console with no JS-readable signal.
        try {
            device.addEventListener('uncapturederror', (event: Event) => {
                const err = (event as GPUUncapturedErrorEvent).error;
                errorLog?.log(`GPUDevice uncapturederror: ${err.constructor.name}: ${err.message}`);
            });
        } catch (e) {
            warnLog?.log('attachDevice: uncapturederror listener failed:', e);
        }

        // device.lost settles exactly once when the device is permanently dead;
        // any later submit surfaces OperationError.
        void device.lost.then((info: GPUDeviceLostInfo) => {
            this.onDeviceLost(device, info);
        }).catch((e: unknown) => {
            warnLog?.log('device.lost promise rejected unexpectedly:', e);
        });
    }

    private static onDeviceLost(device: GPUDevice, info: GPUDeviceLostInfo): void {
        // Ignore stale notifications from a replaced device.
        if (this.device !== device) {
            warnLog?.log(
                `GPUDevice lost (stale, already replaced): reason=${info.reason} `
                + `message=${info.message}`);
            return;
        }

        errorLog?.log(`GPUDevice lost: reason=${info.reason} message=${info.message}`);
        this.device = null;
        this.sampler = null;
        this.lastLostInfo = info;

        // Snapshot — handlers may self-dispose and mutate the set.
        const listeners = Array.from(this.lostListeners);
        for (const listener of listeners) {
            try {
                listener(info);
            } catch (e) {
                warnLog?.log('device-lost listener threw:', e);
            }
        }
    }
}
