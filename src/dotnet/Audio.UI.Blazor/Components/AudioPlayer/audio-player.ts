class AudioUpdate {
    public played: () => void;
    public chunk: Uint8Array;
    public offsetSecs: number;

    public constructor(played: () => void, chunk: Uint8Array, offsetSecs: number) {
        this.played = played;
        this.chunk = chunk;
        this.offsetSecs = offsetSecs;
    }
}

class AudioEnd {
}

export class AudioPlayer {
    private readonly _audio: HTMLAudioElement;
    private readonly _blazorRef: DotNet.DotNetObject;
    private _sourceBuffer: SourceBuffer;
    private _bufferQueue: (AudioUpdate | AudioEnd)[];
    private _playingQueue: AudioUpdate[];
    private _removedBefore: number;
    private _audioEnded?: () => void;
    private _startOffset?: number;
    private _previousReadyState: number;
    private readonly _mediaSource: MediaSource;
    private readonly _bufferCreated: Promise<SourceBuffer>;

    public constructor(blazorRef: DotNet.DotNetObject) {
        this._audio = new Audio();
        this._audio.autoplay = true;
        this._blazorRef = blazorRef;
        this._sourceBuffer = null;
        this._bufferQueue = [];
        this._playingQueue = [];
        this._mediaSource = new MediaSource();
        this._previousReadyState = -1;
        this._mediaSource.addEventListener('sourceopen', _ => {
            console.log('sourceopen: ' + this._mediaSource.readyState);
        });
        this._mediaSource.addEventListener('sourceended', _ => {
            console.log('sourceended: ' + this._mediaSource.readyState);
        });
        this._mediaSource.addEventListener('sourceclose', _ => {
            console.log('sourceclose: ' + this._mediaSource.readyState);
        });
        this._mediaSource.addEventListener('error', _ => {
            console.log('source error: ' + this._mediaSource.readyState);
        });

        this._audio.addEventListener('ended', (e) => {
            while (this._playingQueue.length > 0) {
                let playingFrame = this._playingQueue.shift();
                playingFrame.played();
            }

            if (this._audioEnded)
                this._audioEnded();

            console.log('Audio ended. ' + JSON.stringify(e));
        });
        this._audio.addEventListener('error', (e) => {
            while (this._playingQueue.length > 0) {
                let playingFrame = this._playingQueue.shift();
                playingFrame.played();
            }

            if (this._audioEnded)
                this._audioEnded();

            let err = this._audio.error;
            console.log(e.message);
            console.error(`Error during append audio. Code: ${err.code}. Message: ${err.message}`);
        });
        this._audio.addEventListener('stalled', _ => {
            console.log('Audio stalled. ');
        });
        this._audio.addEventListener('waiting', (e) => {
            console.log('Audio is waiting. ');
            console.log(`Audio state: ${this.getReadyState()}`);
        });
        this._audio.addEventListener('timeupdate', (e) => {
            let time = this._audio.currentTime;
            while (this._playingQueue.length > 0) {
                let playingFrame = this._playingQueue.shift();
                let frameTime = playingFrame.offsetSecs;
                if (time > frameTime - 5) {
                    playingFrame.played();
                } else {
                    this._playingQueue.unshift(playingFrame);
                    break;
                }
            }

            this._blazorRef.invokeMethodAsync("SetCurrentPlaybackTime", time);
        });
        this._audio.addEventListener('canplay', (e) => {
            if (this._startOffset) {
                this._audio.currentTime = this._startOffset;
                this._startOffset = null;
            }
        });

        this._bufferCreated = new Promise<SourceBuffer>(resolve => {
            this._mediaSource.addEventListener('sourceopen', _ => {
                URL.revokeObjectURL(this._audio.src);

                let mime = 'audio/webm; codecs=opus';
                this._sourceBuffer = this._mediaSource.addSourceBuffer(mime);
                // this._sourceBuffer.mode = 'segments';
                this._sourceBuffer.addEventListener('updateend', _ => {
                    if (this._bufferQueue.length > 0 && !this._sourceBuffer.updating) {
                        let update = this._bufferQueue.shift();
                        let currentTime = this._audio.currentTime;
                        let removedBefore = this._removedBefore;
                        if (currentTime > removedBefore + 5) {
                            this._sourceBuffer.remove(removedBefore, currentTime - 2);
                            this._removedBefore = currentTime - 2;
                            this._bufferQueue.unshift(update);
                        } else {
                            if (update instanceof AudioUpdate) {
                                this._sourceBuffer.appendBuffer(update.chunk);
                                if (this._audio.readyState === this._audio.HAVE_ENOUGH_DATA) {
                                    if (this._audio.currentTime + 5 <= update.offsetSecs) {
                                        this._playingQueue.push(update);
                                    } else {
                                        update.played();
                                    }
                                } else {
                                    update.played();
                                }
                            } else {
                                this._mediaSource.endOfStream();
                            }
                        }
                    }
                });

                resolve(this._sourceBuffer);
            });
        });

        this._audio.src = URL.createObjectURL(this._mediaSource);
    }

    public static create(blazorRef: DotNet.DotNetObject) {
        return new AudioPlayer(blazorRef);
    }

    public async initialize(byteArray: Uint8Array, offset: number): Promise<void> {
        console.log('Audio player initialized.');
        this._startOffset = offset;

        if (this._sourceBuffer !== null) {
            this._sourceBuffer.appendBuffer(byteArray);
            console.log('Audio init header has been appended.');
        } else {
            console.log('Audio init: waiting for SourceBuffer.');
            let sourceBuffer = await this._bufferCreated;
            sourceBuffer.appendBuffer(byteArray);
            console.log('Audio init header has been appended with delay.');
        }
    }

    public dispose(): void {
        console.log('Audio player dispose() call');
        this.stop(null);
    }

    public appendAudio(byteArray: Uint8Array, offsetSecs: number): Promise<void> {
        return new Promise<void>(resolve => {
            try {
                if (this._audio.error !== null) {
                    let e = this._audio.error;
                    console.error(`Error during append audio. Code: ${e.code}. Message: ${e.message}`);
                } else {
                    if (this._audio.readyState !== this._previousReadyState) {
                        console.log(`Audio state: ${this.getReadyState()}`);
                    }
                    this._previousReadyState = this._audio.readyState;
                    if (this._sourceBuffer.updating) {
                        this._bufferQueue.push(new AudioUpdate(resolve, byteArray, offsetSecs));
                    } else {
                        let currentTime = this._audio.currentTime;
                        let removedBefore = this._removedBefore;
                        if (currentTime > removedBefore + 5) {
                            this._sourceBuffer.remove(removedBefore, currentTime - 2);
                            this._removedBefore = currentTime - 2;
                            this._bufferQueue.push(new AudioUpdate(resolve, byteArray, offsetSecs));
                        }

                        let newUpdate = new AudioUpdate(resolve, byteArray, offsetSecs)
                        if (this._bufferQueue.length > 0) {
                            this._bufferQueue.push(newUpdate);
                            let updateWithProperOrder = this._bufferQueue.shift();
                            if (updateWithProperOrder instanceof AudioUpdate) {
                                this._sourceBuffer.appendBuffer(updateWithProperOrder.chunk);
                                if (this._audio.readyState === this._audio.HAVE_ENOUGH_DATA) {
                                    if (this._audio.currentTime + 5 <= updateWithProperOrder.offsetSecs) {
                                        this._playingQueue.push(updateWithProperOrder);
                                    } else {
                                        updateWithProperOrder.played();
                                    }
                                } else {
                                    updateWithProperOrder.played();
                                }
                            } else
                                this._mediaSource.endOfStream();
                        } else {
                            this._sourceBuffer.appendBuffer(byteArray);
                            if (this._audio.readyState === this._audio.HAVE_ENOUGH_DATA) {
                                if (this._audio.currentTime + 5 <= newUpdate.offsetSecs) {
                                    this._playingQueue.push(newUpdate);
                                } else {
                                    newUpdate.played();
                                }
                            } else {
                                newUpdate.played();
                            }
                        }
                    }
                }
            } catch (err) {
                console.error('Error appending audio. ' + JSON.stringify(err));
            }
        });
    }

    public async endOfStream(): Promise<void> {
        console.log('Audio player endOfStream() call');
        return new Promise<void>(resolve => {
            if (this._sourceBuffer.updating) {
                this._bufferQueue.push(new AudioEnd());
            } else {
                if (this._bufferQueue.length > 0) {
                    this._bufferQueue.push(new AudioEnd());
                } else {
                    this._mediaSource.endOfStream();
                }
            }
            this._audioEnded = resolve;
        });
    }

    public stop(error: EndOfStreamError | null) {
        console.log('Audio player stop() call');
        this._audio.pause();

        if (this._sourceBuffer.updating) {
            this._sourceBuffer.onupdateend = _ => {
                if (!this._sourceBuffer.updating)
                    this._mediaSource.endOfStream();
            }
        } else {
            this._mediaSource.endOfStream();
        }
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
