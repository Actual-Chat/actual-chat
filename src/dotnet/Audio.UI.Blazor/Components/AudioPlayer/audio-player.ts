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
    private _lastReadyState: number;
    private _lastOffset: number;
    private readonly _mediaSource: MediaSource;
    private readonly _bufferCreated: Promise<SourceBuffer>;

    public constructor(blazorRef: DotNet.DotNetObject, debugMode: boolean) {
        this._debugMode = debugMode;
        this._audio = new Audio();
        this._blazorRef = blazorRef;
        this._sourceBuffer = null;
        this._bufferQueue = [];
        this._mediaSource = new MediaSource();
        this._lastReadyState = -1;
        this._lastOffset = -1;
        this._mediaSource.addEventListener('error', _ => {
            this.logError(`mediaSource.error: ${this._mediaSource.readyState}`);
        });

        this._audio.addEventListener('ended', e => {
            let _ = this.invokeOnPlaybackEnded();
            if (debugMode)
                this.log(`_audio.ended.`);
        });
        this._audio.addEventListener('error', e => {
            let err = this._audio.error;
            let _ = this.invokeOnPlaybackEnded(err.code, err.message);
            this.logError(`_audio.error: code: ${err.code}, message: ${err.message}`);
        });
        this._audio.addEventListener('stalled', _ => {
            this.logWarn(`_audio.stalled.`);
        });
        this._audio.addEventListener('waiting', e => {
            this.logWarn(`_audio.waiting, _audio.readyState = ${this.getReadyState()}`);
            let time = this._audio.currentTime;
            let readyState = this._audio.readyState;
            let _ = this.invokeOnReadyToBufferMore(time, readyState);
        });
        this._audio.addEventListener('timeupdate', e => {
            let time = this._audio.currentTime;
            if (this._audio.readyState !== this._lastReadyState) {
                if (this._debugMode)
                    this.log(`timeupdate: new _audio.readyState = ${this.getReadyState()}`);
            }
            this._lastReadyState = this._audio.readyState;

            let _ = this.invokeOnPlaybackTimeChanged(time);
        });
        this._audio.addEventListener('canplay', _ => {
        });
        this._audio.addEventListener('loadeddata', async _ => {
            let audio = this._audio;
            if (audio.readyState >= 3) {
                await audio.play();
            }
        });

        this._bufferCreated = new Promise<SourceBuffer>(resolve => {
            this._mediaSource.addEventListener('sourceopen', _ => {
                URL.revokeObjectURL(this._audio.src);

                if (this._mediaSource.sourceBuffers.length == 0) {
                    let mime = 'audio/webm; codecs=opus';
                    this._sourceBuffer = this._mediaSource.addSourceBuffer(mime);
                    this._sourceBuffer.addEventListener('updateend', _ => this.onUpdateEnd());

                    resolve(this._sourceBuffer);
                }
            });
        });

        this._audio.src = URL.createObjectURL(this._mediaSource);
    }

    public static create(blazorRef: DotNet.DotNetObject, debugMode: boolean) {
        return new AudioPlayer(blazorRef, debugMode);
    }

    public async initialize(byteArray: Uint8Array): Promise<void> {
        if (this._debugMode)
            this.log(`initialize()`);

        try {
            if (this._sourceBuffer !== null) {
                this._sourceBuffer.appendBuffer(byteArray);
                if (this._debugMode)
                    this.log(`initialize: header has been appended`);
            } else {
                if (this._debugMode)
                    this.log(`initialize: awaiting this._bufferCreated`);
                let sourceBuffer = await this._bufferCreated;
                sourceBuffer.appendBuffer(byteArray);
                if (this._debugMode)
                    this.log(`initialize: header has been appended with a delay`);
            }
        } catch (e) {
            this.logError(`initialize: error ${e} ${e.stack}`);
        }
    }

    public dispose(): void {
        if (this._debugMode)
            this.log(`dispose()`);
        this.stop(null);
    }

    public appendAudio(byteArray: Uint8Array, offset: number): number {
        if (this._debugMode)
            this.log(`.appendAudio(size: ${byteArray.length}, offset: ${offset})`);
        if (this._audio.error !== null) {
            let e = this._audio.error;
            this.logError(`appendAudio: error, code: ${e.code}, message: ${e.message}`);
            return 0;
        }

        try {
            if (this._audio.readyState !== this._lastReadyState) {
                if (this._debugMode)
                    this.log(`appendAudio: new _audio.readyState = ${this.getReadyState()}`);
            }
            this._lastReadyState = this._audio.readyState;

            if (this._sourceBuffer.updating) {
                this._bufferQueue.push(new AudioUpdate(byteArray, offset));
            } else {
                let queueItemAppended = this.onUpdateEnd();
                if (!queueItemAppended) {
                    this._sourceBuffer.appendBuffer(byteArray);
                    if (offset < this._lastOffset)
                        this.logError(`appendAudio: offset < _lastOffset!`);
                    this._lastOffset = offset;
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
            this.logError(`appendAudio: error ${e} ${e.stack}`);
        }
    }

    public endOfStream(): void {
        if (this._debugMode)
            this.log(`endOfStream()`);
        if (this._sourceBuffer.updating) {
            this._bufferQueue.push(new AudioEnd());
        } else {
            if (this._bufferQueue.length > 0) {
                this.onUpdateEnd();
                this._bufferQueue.push(new AudioEnd());
            } else {
                if (this._mediaSource.readyState === "open") {
                    this._mediaSource.endOfStream();
                }
            }
        }
    }

    public stop(error: EndOfStreamError | null) {
        if (this._debugMode)
            this.log(`stop()`);
        this._audio.pause();

        if (this._sourceBuffer.updating) {
            this._sourceBuffer.onupdateend = _ => {
                if (!this._sourceBuffer.updating)
                    this._mediaSource.endOfStream(error);
            };
        } else {
            if (this._mediaSource.readyState === "open") {
                this._mediaSource.endOfStream(error);
            }
        }
    }

    // private methods

    private cleanupSourceBuffer() {
        if (this._sourceBuffer.updating)
            return;

        let currentTime = this._audio.currentTime;
        let removedBefore = this._removedBefore;
        if (currentTime > removedBefore + 10) {
            this._sourceBuffer.remove(removedBefore, currentTime - 5);
            this._removedBefore = currentTime - 5;
        }
    }

    private onUpdateEnd(): boolean {
        if (this._bufferQueue.length === 0 || this._sourceBuffer.updating)
            return false;

        try {
            this.cleanupSourceBuffer();

            let update = this._bufferQueue.shift();
            if (update instanceof AudioUpdate) {
                let offset = update.offset;
                this._sourceBuffer.appendBuffer(update.chunk);
                if (offset < this._lastOffset)
                    this.logError(`onUpdateEnd: offset < _lastOffset!`);
                this._lastOffset = offset;
            } else {
                if (this._mediaSource.readyState === "open") {
                    this._mediaSource.endOfStream();
                }
            }

            return true;
        } catch (e) {
            this.logError(`onUpdateEnd: error ${e} ${e.stack}`);
        }
    }

    private async invokeOnPlaybackTimeChanged(time: number): Promise<void> {
        await this._blazorRef.invokeMethodAsync("OnPlaybackTimeChanged", time);
    }

    private invokeOnPlaybackEnded(code: number | null = null, message: string | null = null): Promise<void> {
        return this._blazorRef.invokeMethodAsync("OnPlaybackEnded", code, message);
    }

    private invokeOnReadyToBufferMore(time: number, readyState: number): Promise<void> {
        return this._blazorRef.invokeMethodAsync("OnReadyToBufferMore", time, readyState);
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

    private log(message: string) {
        console.debug(`[${new Date(Date.now()).toISOString()}] AudioPlayer: ${message}`);
    }

    private logWarn(message: string) {
        console.warn(`[${new Date(Date.now()).toISOString()}] AudioPlayer: ${message}`);
    }

    private logError(message: string) {
        console.error(`[${new Date(Date.now()).toISOString()}] AudioPlayer: ${message}`);
    }
}
