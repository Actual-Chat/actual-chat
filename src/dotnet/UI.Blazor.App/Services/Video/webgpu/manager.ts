import { getLogs } from 'logging';

const { infoLog, warnLog, errorLog } = getLogs('VideoWebGPU');

export type DeviceLostListener = (info: GPUDeviceLostInfo) => void;

// Centralized WebGPU device ownership. Single shared GPUDevice + sampler so
// blur and any future GPU consumers don't race on adapter/device creation.
// Watches device.lost so consumers can fail-fast instead of submitting work
// to a dead device (which surfaces as per-frame OperationError storms).
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

            const createdDevice = await adapter.requestDevice();
            this.attachDevice(createdDevice);
            return createdDevice;
        })().finally(() => {
            this.initPromise = null;
        });

        return this.initPromise;
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

    // Subscribe to device-lost notifications. Listener is invoked synchronously
    // after the device.lost promise settles and AFTER the manager nulls out
    // its device/sampler refs — consumers can read hasDevice() to confirm.
    // Returns a disposer that unregisters the listener.
    static addLostListener(listener: DeviceLostListener): () => void {
        this.lostListeners.add(listener);
        return () => { this.lostListeners.delete(listener); };
    }

    private static attachDevice(device: GPUDevice): void {
        this.device = device;
        this.sampler = device.createSampler({ magFilter: 'linear', minFilter: 'linear' });
        this.lastLostInfo = null;

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

        try {
            device.addEventListener('uncapturederror', (event: Event) => {
                const err = (event as GPUUncapturedErrorEvent).error;
                errorLog?.log(`GPUDevice uncapturederror: ${err.constructor.name}: ${err.message}`);
            });
        } catch (e) {
            warnLog?.log('attachDevice: uncapturederror listener failed:', e);
        }

        // device.lost is a Promise<GPUDeviceLostInfo> — settles exactly once
        // when the device is permanently dead. Reasons: 'destroyed' (we called
        // destroy()), 'unknown' (driver crash, GPU process killed, OS reset).
        // After this, any work submitted surfaces OperationError.
        void device.lost.then((info: GPUDeviceLostInfo) => {
            this.onDeviceLost(device, info);
        }).catch((e: unknown) => {
            warnLog?.log('device.lost promise rejected unexpectedly:', e);
        });
    }

    private static onDeviceLost(device: GPUDevice, info: GPUDeviceLostInfo): void {
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
