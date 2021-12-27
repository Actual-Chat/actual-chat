export interface IAudioPlayer {
    onStartPlaying?: () => void;
    onInitialized?: () => void;
    dispose(): void;
    appendAudio(byteArray: Uint8Array, offset: number): Promise<void>;
    endOfStream(): void;
    stop(error: EndOfStreamError | null): void;
}
