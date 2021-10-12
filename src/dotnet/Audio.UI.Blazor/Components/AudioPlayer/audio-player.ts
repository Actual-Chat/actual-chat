class AudioUpdate
{
    public chunk: Uint8Array;
    public offsetSecs: number;

    public constructor(init: Partial<AudioUpdate>) {
        Object.assign(this,  init);
    }
}

export class AudioPlayer {
    private _audio: HTMLAudioElement;
    private _blazorRef: DotNet.DotNetObject;
    private _sourceBuffer: SourceBuffer;
    private _bufferQueue: AudioUpdate[];
    private _removedBefore: number;
    private readonly _mediaSource: MediaSource;
    private readonly _bufferCreated: Promise<SourceBuffer>;


    public static create(blazorRef: DotNet.DotNetObject) {
        return new AudioPlayer(blazorRef);
    }

    public constructor(blazorRef: DotNet.DotNetObject) {
        this._audio = new Audio();
        this._audio.autoplay = true;
        this._blazorRef = blazorRef;
        this._sourceBuffer = null;
        this._bufferQueue = [];
        this._mediaSource = new MediaSource();
        this._mediaSource.addEventListener('sourceopen', _ => { console.log('sourceopen: ' + this._mediaSource.readyState); });
        this._mediaSource.addEventListener('sourceended', _ => { console.log('sourceended: ' + this._mediaSource.readyState); });
        this._mediaSource.addEventListener('sourceclose', _ => { console.log('sourceclose: ' + this._mediaSource.readyState); });
        this._mediaSource.addEventListener('error', _ => { console.log('source error: ' + this._mediaSource.readyState); });


        this._audio.addEventListener('ended', (e) => {
            console.log('Audio ended. ' + JSON.stringify(e));
        });
        this._audio.addEventListener('error', (e) => {
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

        this._bufferCreated = new Promise<SourceBuffer>(resolve => {
            this._mediaSource.addEventListener('sourceopen', _ => {
                URL.revokeObjectURL(this._audio.src);

                this._audio.play().then(_ => {
                });

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
                            this._sourceBuffer.appendBuffer(update.chunk);
                        }
                    }
                });

                resolve(this._sourceBuffer);
            });
        });

        this._audio.src = URL.createObjectURL(this._mediaSource);
    }

    public dispose(): void {
        console.log('Audio player dispose() call');
        this.stop(null);
        this._audio.pause();
    }

    public async initialize(byteArray: Uint8Array): Promise<void> {
        console.log('Audio player initialized.');
        if (this._sourceBuffer !== null) {
            this._sourceBuffer.appendBuffer(byteArray);
            console.log('Audio init header has been appended.');
        }
        else {
            console.log('Audio init: waiting for SourceBuffer.');
            let sourceBuffer = await this._bufferCreated;
            sourceBuffer.appendBuffer(byteArray);
            console.log('Audio init header has been appended with delay.');
        }
    }

    public appendAudio(byteArray: Uint8Array, offsetSecs: number): void {
        try {
            if (this._audio.error !== null) {
                let e = this._audio.error;
                console.error(`Error during append audio. Code: ${e.code}. Message: ${e.message}`);
            }
            else {
                console.log(`Audio state: ${this._audio.readyState}`);
                console.log(`MediaSource state: ${this._mediaSource.readyState}`);
                if (this._sourceBuffer.updating) {
                    this._bufferQueue.push(new AudioUpdate({chunk: byteArray, offsetSecs: offsetSecs}));
                } else {
                    let currentTime = this._audio.currentTime;
                    let removedBefore = this._removedBefore;
                    if (currentTime > removedBefore + 5) {
                        this._sourceBuffer.remove(removedBefore, currentTime - 2);
                        this._removedBefore = currentTime - 2;
                        this._bufferQueue.push(new AudioUpdate({chunk: byteArray, offsetSecs: offsetSecs}));
                    }

                    if (this._bufferQueue.length > 0) {
                        this._bufferQueue.push(new AudioUpdate({chunk: byteArray, offsetSecs: offsetSecs}));
                        let updateWithProperOrder = this._bufferQueue.shift();
                        this._sourceBuffer.appendBuffer(updateWithProperOrder.chunk);
                    }
                    else {
                        this._sourceBuffer.appendBuffer(byteArray);
                    }
                }
            }
        }
        catch(err) {
            console.error('Error appending audio. ' + JSON.stringify(err));
        }
    }

    public stop(error: EndOfStreamError | null) {
        console.log('Audio player stop() call');
        if (this._sourceBuffer.updating) {
            this._sourceBuffer.onupdateend = _ => {
                if (!this._sourceBuffer.updating)
                    this._mediaSource.endOfStream();
            }
        }
        else {
            this._mediaSource.endOfStream();
        }
        this._audio.pause();
    }
}
