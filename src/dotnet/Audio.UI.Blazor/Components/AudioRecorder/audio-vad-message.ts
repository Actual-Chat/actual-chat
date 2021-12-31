export interface VadMessage {
    topic: "init-port" | "buffer";
    buffer: ArrayBuffer;
}
