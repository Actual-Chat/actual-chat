class AudioUpdate {
    public played: () => void;
    public chunk: Uint8Array;
    public offset: number;

    public constructor(chunk: Uint8Array, offsetSecs: number) {
        this.chunk = chunk;
        this.offset = offsetSecs;
    }
}

class AudioEnd {
}

export class AudioPlayer {
    private readonly _debugMode: boolean;
    private readonly _audio: HTMLAudioElement;
    private readonly _blazorRef: DotNet.DotNetObject;
    private _sourceBuffer: SourceBuffer;
    private _bufferQueue: (AudioUpdate | AudioEnd)[];
    private _removedBefore: number;
    private _previousReadyState: number;
    private _previousOffset: number;
    private readonly _mediaSource: MediaSource;
    private readonly _bufferCreated: Promise<SourceBuffer>;

    public constructor(blazorRef: DotNet.DotNetObject, debugMode: boolean) {
        this._debugMode = debugMode;
        this._audio = new Audio();
        this._audio.autoplay = true;
        this._blazorRef = blazorRef;
        this._sourceBuffer = null;
        this._bufferQueue = [];
        this._mediaSource = new MediaSource();
        this._previousReadyState = -1;
        this._previousOffset = -1;
        this._mediaSource.addEventListener('error', _ => {
            if (debugMode)
                console.log('source error: ' + this._mediaSource.readyState);
        });

        this._audio.addEventListener('ended', (e) => {
            let _ = this.invokeOnPlaybackEnded();
            if (debugMode) {
                console.log('Audio ended. ' + JSON.stringify(e));
            }
        });
        this._audio.addEventListener('error', (e) => {
            let err = this._audio.error;
            let _ = this.invokeOnPlaybackEnded(err.code, err.message);
            if (debugMode)
                console.error(`Error during append audio. Code: ${err.code}. Message: ${err.message}`);
        });
        this._audio.addEventListener('stalled', _ => {
            if (debugMode)
                console.log('Audio stalled. ');
        });
        this._audio.addEventListener('waiting', (e) => {
            if (debugMode)
                console.log('Audio is waiting. ');
                console.log(`Audio state: ${this.getReadyState()}`);
            let time = this._audio.currentTime;
            let readyState = this._audio.readyState;
            let _ = this.invokeOnDataWaiting(time, readyState);
        });
        this._audio.addEventListener('timeupdate', (e) => {
            let time = this._audio.currentTime;
            let _ = this.invokeOnPlaybackTimeChanged(time);
        });
        this._audio.addEventListener('canplay', (e) => {
        });

        this._bufferCreated = new Promise<SourceBuffer>(resolve => {
            this._mediaSource.addEventListener('sourceopen', _ => {
                URL.revokeObjectURL(this._audio.src);
                let mime = 'audio/webm; codecs=opus';
                this._sourceBuffer = this._mediaSource.addSourceBuffer(mime);
                this._sourceBuffer.addEventListener('updateend', _ => this.OnUpdateEnd());

                resolve(this._sourceBuffer);
            });
        });

        this._audio.src = URL.createObjectURL(this._mediaSource);
    }

    public static create(blazorRef: DotNet.DotNetObject, debugMode: boolean) {
        return new AudioPlayer(blazorRef, debugMode);
    }

    public async initialize(byteArray: Uint8Array): Promise<void> {
        if (this._debugMode)
            console.log('Audio player initialized.');

        try {
            if (this._sourceBuffer !== null) {
                this._sourceBuffer.appendBuffer(byteArray);
                if (this._debugMode)
                    console.log('Audio init header has been appended.');
            } else {
                if (this._debugMode)
                    console.log('Audio init: waiting for SourceBuffer.');
                let sourceBuffer = await this._bufferCreated;
                sourceBuffer.appendBuffer(byteArray);
                if (this._debugMode)
                    console.log('Audio init header has been appended with delay.');
            }
        } catch (e) {
            if (this._debugMode)
                console.error(e, e.stack);
        }
    }

    public dispose(): void {
        if (this._debugMode)
            console.log('Audio player dispose() call');
        this.stop(null);
    }

    public appendAudio(byteArray: Uint8Array, offset: number): number {
        if (this._audio.error !== null) {
            let e = this._audio.error;
            if (this._debugMode)
                console.error(`Error during append audio. Code: ${e.code}. Message: ${e.message}`);
            return;
        }

        try {
            if (this._audio.readyState !== this._previousReadyState) {
                console.log(`Audio state: ${this.getReadyState()}`);
            }
            this._previousReadyState = this._audio.readyState;

            if (this._sourceBuffer.updating) {
                this._bufferQueue.push(new AudioUpdate(byteArray, offset));
            } else {
                let queueItemAppended = this.OnUpdateEnd();
                if (!queueItemAppended) {
                    this._sourceBuffer.appendBuffer(byteArray);
                    if (this._previousOffset > offset) {
                        if (this._debugMode)
                            console.error(`Update offset is less than previously processed offset`);
                    }
                    this._previousOffset = offset;
                } else {
                    this._bufferQueue.push(new AudioUpdate(byteArray, offset));
                }
            }

            if (this._sourceBuffer.buffered.length > 0) {
                let bufferedUpTo = this._sourceBuffer.buffered.end(this._sourceBuffer.buffered.length - 1);
                return bufferedUpTo - this._audio.currentTime;
            }

            return 0;

        } catch (e) {
            if (this._debugMode)
                console.error(e, e.stack);
        }
    }

    public endOfStream(): void {
        if (this._debugMode)
            console.log('Audio player endOfStream() call');
        if (this._sourceBuffer.updating) {
            this._bufferQueue.push(new AudioEnd());
        } else {
            if (this._bufferQueue.length > 0) {
                this.OnUpdateEnd();
                this._bufferQueue.push(new AudioEnd());
            } else {
                this._mediaSource.endOfStream();
            }
        }
    }

    public stop(error: EndOfStreamError | null) {
        if (this._debugMode)
            console.log('Audio player stop() call');
        this._audio.pause();

        if (this._sourceBuffer.updating) {
            this._sourceBuffer.onupdateend = _ => {
                if (!this._sourceBuffer.updating)
                    this._mediaSource.endOfStream(error);
            }
        } else {
            this._mediaSource.endOfStream(error);
        }
    }

    // private methods

    private CleanupPlayedBuffer() {
        if (this._sourceBuffer.updating)
            return;

        let currentTime = this._audio.currentTime;
        let removedBefore = this._removedBefore;
        if (currentTime > removedBefore + 10) {
            this._sourceBuffer.remove(removedBefore, currentTime - 5);
            this._removedBefore = currentTime - 5;
        }
    }

    private OnUpdateEnd(): boolean {
        if (this._bufferQueue.length === 0 || this._sourceBuffer.updating)
            return false;

        try {
            this.CleanupPlayedBuffer();

            let update = this._bufferQueue.shift();
            if (update instanceof AudioUpdate) {
                let offset = update.offset;
                this._sourceBuffer.appendBuffer(update.chunk);
                if (this._previousOffset > offset) {
                    if (this._debugMode)
                        console.error(`Update offset is less than previously processed offset`);
                }
                this._previousOffset = offset;
            } else {
                this._mediaSource.endOfStream();
            }

            return true;
        } catch (e) {
            if (this._debugMode)
                console.error(e, e.stack);
        }
    }

    private async invokeOnPlaybackTimeChanged(time: number): Promise<void> {
        await this._blazorRef.invokeMethodAsync("OnPlaybackTimeChanged", time);
    }

    private invokeOnPlaybackEnded(code: number | null = null, message: string | null = null): Promise<void> {
        return this._blazorRef.invokeMethodAsync("OnPlaybackEnded", code, message);
    }

    private invokeOnDataWaiting(time: number, readyState: number): Promise<void> {
        return this._blazorRef.invokeMethodAsync("OnDataWaiting", time, readyState);
    }

    private getReadyState(): string {
        switch (this._audio.readyState) {
            case this._audio.HAVE_CURRENT_DATA:
                return 'HAVE_CURRENT_DATA';
            case this._audio.HAVE_ENOUGH_DATA:
                return 'HAVE_ENOUGH_DATA';
            case this._audio.HAVE_FUTURE_DATA:
                return 'HAVE_FUTURE_DATA';
            case this._audio.HAVE_METADATA:
                return 'HAVE_METADATA';
            case this._audio.HAVE_NOTHING:
                return 'HAVE_NOTHING';
            default:
                return 'UNKNOWN - ' + this._audio.readyState;
        }
    }
}
