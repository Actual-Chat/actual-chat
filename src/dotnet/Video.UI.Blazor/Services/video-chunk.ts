export interface VideoChunk {
    readonly data: Uint8Array,
    readonly duration: number | null;
    readonly timestamp: number;
    readonly isKey: boolean;
}
