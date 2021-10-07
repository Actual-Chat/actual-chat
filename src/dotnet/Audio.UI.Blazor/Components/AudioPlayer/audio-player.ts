export class AudioPlayer {
    private _elementRef: HTMLAudioElement;
    private _blazorRef: DotNet.DotNetObject;
    private _sourceBuffer: SourceBuffer;
    private _bufferQueue: Uint8Array[];
    private readonly _mediaSource: MediaSource;


    public static create(elementRef: HTMLAudioElement, blazorRef: DotNet.DotNetObject) {
        return new AudioPlayer(elementRef, blazorRef);
    }

    public constructor(elementRef: HTMLAudioElement, blazorRef: DotNet.DotNetObject) {
        this._elementRef = elementRef;
        this._blazorRef = blazorRef;
        this._sourceBuffer = null;
        this._bufferQueue = [];
        this._mediaSource = new MediaSource();
        this._elementRef.addEventListener('ended', (e) => {
            console.log(JSON.stringify(e));
        });
        this._elementRef.addEventListener('error', (e) => {
            console.log(JSON.stringify(e));
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

    public async appendAudio(byteArray: Uint8Array): Promise<void> {
        console.log(this._elementRef.readyState);
        if (this._sourceBuffer.updating) {
            this._bufferQueue.push(byteArray);
        }
        else {
            this._sourceBuffer.appendBuffer(byteArray);
        }
    }

    public stop(error: EndOfStreamError) {
        this._mediaSource.endOfStream(error);
    }
}
