export interface IAudioPlayer {
    onStartPlaying?: () => void;
    onInitialized?: () => void;
    init(byteArray: Uint8Array): Promise<void>;
    appendAudio(byteArray: Uint8Array, offset: number): Promise<void>;
    endOfStream(): void;
    stop(error: EndOfStreamError | null): void;
    dispose(): void;
}
