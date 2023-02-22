export interface AudioVadWorker {
    create(): Promise<void>;
    init(workletPort: MessagePort, encoderWorkerPort: MessagePort): Promise<void>;
    reset(): Promise<void>;

    append(buffer: ArrayBuffer): Promise<void>;
}
