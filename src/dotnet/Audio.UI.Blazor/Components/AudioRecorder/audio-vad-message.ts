export interface VadMessage {
    topic: 'init-port' | 'init-new-stream' | 'buffer';
    buffer?: ArrayBuffer;
}
