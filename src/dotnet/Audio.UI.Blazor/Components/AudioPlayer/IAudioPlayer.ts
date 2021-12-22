export interface IAudioPlayer {
    onStartPlaying?: () => void;
    onInitialized?: () => void;
    initialize(byteArray: Uint8Array): Promise<void>;
    dispose(): void;
    appendAudioAsync(byteArray: Uint8Array, offset: number): Promise<void>;
    endOfStream(): void;
    stop(error: EndOfStreamError | null): void;
}
