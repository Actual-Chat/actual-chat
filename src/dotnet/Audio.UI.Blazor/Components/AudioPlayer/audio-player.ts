export class AudioPlayer {
    private _elementRef: HTMLAudioElement;
    private _blazorRef: DotNet.DotNetObject;
    private _sourceBuffer: SourceBuffer;
    private _bufferQueue: Uint8Array[];
    private readonly _mediaSource: MediaSource;


    public static create(blazorRef: DotNet.DotNetObject) {
        return new AudioPlayer(blazorRef);
    }

    public constructor(blazorRef: DotNet.DotNetObject) {
        this._elementRef = new Audio();
        this._elementRef.autoplay = true;
        this._blazorRef = blazorRef;
        this._sourceBuffer = null;
        this._bufferQueue = [];
        this._mediaSource = new MediaSource();
        this._elementRef.addEventListener('ended', (e) => {
            console.log(JSON.stringify(e));
        });
        this._elementRef.addEventListener('error', (e) => {
            let err = this._elementRef.error;
            console.log(e.message);
            console.error(`Error during append audio. Code: ${err.code}. Message: ${err.message}`);
        });
        this._elementRef.addEventListener('stalled', (e) => {
            console.log(JSON.stringify(e));
        });
        this._elementRef.addEventListener('waiting', (e) => {
            console.log(JSON.stringify(e));
        });


        this._mediaSource.addEventListener('sourceopen', _ => {
            URL.revokeObjectURL(this._elementRef.src);

            let mime = 'audio/webm; codecs=opus';
            this._sourceBuffer = this._mediaSource.addSourceBuffer(mime);
            this._sourceBuffer.mode = 'segments';
            this._sourceBuffer.addEventListener('updateend', _ => {
                if (this._bufferQueue.length) {
                    this._sourceBuffer.appendBuffer(this._bufferQueue.shift());
                }
            });
        });

        this._elementRef.src = URL.createObjectURL(this._mediaSource);
    }

    public dispose(): void {
        this._mediaSource.endOfStream();
    }

    public initialize(byteArray: Uint8Array): void {
        console.log(this._elementRef.readyState);
        this._sourceBuffer.appendBuffer(byteArray);
    }

    public appendAudio(byteArray: Uint8Array): void {
        try {
            if (this._elementRef.error !== null) {
                let e = this._elementRef.error;
                console.error(`Error during append audio. Code: ${e.code}. Message: ${e.message}`);
            }
            else {
                console.log(this._elementRef.readyState);
                if (this._sourceBuffer.updating) {
                    this._bufferQueue.push(byteArray);
                } else {
                    this._sourceBuffer.appendBuffer(byteArray);
                }
            }
        }
        catch(err) {
            console.error(err);
        }
    }

    public stop(error: EndOfStreamError | null) {
        this._mediaSource.endOfStream(error);
    }
}
