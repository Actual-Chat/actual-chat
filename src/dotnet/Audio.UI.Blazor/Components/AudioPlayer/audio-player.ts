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
    private readonly _mediaSource: MediaSource;
    private readonly _bufferCreated: Promise<SourceBuffer>;

    public constructor(blazorRef: DotNet.DotNetObject) {
        this._audio = new Audio();
        // this._audio.autoplay = true;
        this._blazorRef = blazorRef;
        this._sourceBuffer = null;
        this._bufferQueue = [];
        this._playingQueue = [];
        this._mediaSource = new MediaSource();
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
        this._audio.addEventListener('stalled', (e) => {
            console.log('Audio stalled. ' + JSON.stringify(e));
        });
        this._audio.addEventListener('waiting', (e) => {
            console.log('Audio is waiting. ' + JSON.stringify(e));
        });
        this._audio.addEventListener('timeupdate', (e) => {
            while (this._playingQueue.length > 0) {
                let time = this._audio.currentTime;
                let playingFrame = this._playingQueue.shift();
                let frameTime = playingFrame.offsetSecs;
                // if (time > frameTime) {
                playingFrame.played();
                // } else {
                //     this._playingQueue.unshift(playingFrame);
                //     break;
                // }
            }
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

                // this._audio.play().then(_ => {
                // });

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
                                if (this._audio.readyState === this._audio.HAVE_ENOUGH_DATA)
                                    this._playingQueue.push(update);
                                else
                                    update.played();
                            } else
                                this._mediaSource.endOfStream();
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
                    console.log(`Audio state: ${this._audio.readyState}`);
                    console.log(`MediaSource state: ${this._mediaSource.readyState}`);
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
                                if (this._audio.readyState === this._audio.HAVE_ENOUGH_DATA)
                                    this._playingQueue.push(updateWithProperOrder);
                                else
                                    updateWithProperOrder.played();
                            } else
                                this._mediaSource.endOfStream();
                        } else {
                            this._sourceBuffer.appendBuffer(byteArray);
                            if (this._audio.readyState === this._audio.HAVE_ENOUGH_DATA)
                                this._playingQueue.push(newUpdate);
                            else
                                newUpdate.played();
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
}
