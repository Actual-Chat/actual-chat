export interface OpusEncoderWorklet {
    init(workerPort: MessagePort): Promise<void>;
    append(buffer: ArrayBuffer): Promise<void>;
}
