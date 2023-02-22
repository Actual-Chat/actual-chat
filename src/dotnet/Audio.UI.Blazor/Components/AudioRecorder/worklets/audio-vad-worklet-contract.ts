export interface AudioVadWorklet {
    init(workerPort: MessagePort): Promise<void>;
    append(buffer: ArrayBuffer): Promise<void>;
}
