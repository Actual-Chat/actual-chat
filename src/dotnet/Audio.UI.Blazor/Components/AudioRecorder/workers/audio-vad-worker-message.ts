export interface VadMessage {
    type: 'init-port' | 'init-new-stream' | 'buffer';
    buffer?: ArrayBuffer;
}
