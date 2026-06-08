// Ambient types for the WebRTC Encoded Transform API (Baseline 2025).
// TypeScript's lib.dom does not yet ship these on all toolchains, and we
// rely on `RTCEncodedVideoFrame.getMetadata()`, `RTCRtpScriptTransform`,
// `RTCRtpScriptTransformer.generateKeyFrame()` and `RTCRtpSender.transform`.

interface RTCRtpCodecCapability {
    mimeType: string;
    clockRate: number;
    channels?: number;
    sdpFmtpLine?: string;
}

interface RTCEncodedVideoFrameMetadata {
    frameId?: number;
    dependencies?: number[];
    width?: number;
    height?: number;
    spatialIndex?: number;
    temporalIndex?: number;
    synchronizationSource?: number;
    payloadType?: number;
    contributingSources?: number[];
    rtpTimestamp?: number;
    rid?: string;
}

interface RTCEncodedVideoFrame {
    readonly type: 'key' | 'delta' | 'empty';
    readonly timestamp: number;
    data: ArrayBuffer;
    getMetadata(): RTCEncodedVideoFrameMetadata;
}

interface RTCRtpScriptTransformer {
    readonly readable: ReadableStream<RTCEncodedVideoFrame>;
    readonly writable: WritableStream<RTCEncodedVideoFrame>;
    readonly options: unknown;
    generateKeyFrame(rid?: string): Promise<number>;
    sendKeyFrameRequest(): Promise<void>;
}

interface RTCTransformEvent extends Event {
    readonly transformer: RTCRtpScriptTransformer;
}

interface RTCRtpScriptTransform {
    readonly __brand: 'RTCRtpScriptTransform';
}

type RTCRtpScriptTransformCtor =
    new (worker: Worker, options?: unknown, transfer?: Transferable[]) => RTCRtpScriptTransform;

declare const RTCRtpScriptTransform: RTCRtpScriptTransformCtor;

interface RTCRtpSender {
    transform?: RTCRtpScriptTransform | null;
}

interface RTCRtpReceiver {
    transform?: RTCRtpScriptTransform | null;
}

interface DedicatedWorkerGlobalScope {
    onrtctransform: ((this: DedicatedWorkerGlobalScope, ev: RTCTransformEvent) => void) | null;
}
