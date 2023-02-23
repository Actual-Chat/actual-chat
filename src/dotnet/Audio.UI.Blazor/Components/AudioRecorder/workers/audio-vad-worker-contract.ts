export interface AudioVadWorker {
    create(artifactVersions: Map<string, string>): Promise<void>;
    init(workletPort: MessagePort, encoderWorkerPort: MessagePort): Promise<void>;
    reset(): Promise<void>;

    append(buffer: ArrayBuffer): Promise<void>;
}
