export class AudioPlayer {
    static create(elementRef, backendRef) {
        return new AudioPlayer(elementRef, backendRef);
    }

    constructor(elementRef, backendRef) {
        this.elementRef = elementRef;
        this.backendRef = backendRef;
        this.sourceBuffer = null;
        this.bufferQueue = [];
        this.mediaSource = new MediaSource();
        this.elementRef.addEventListener('ended', (e) => {
            console.log(JSON.stringify(e));
        });
        this.elementRef.addEventListener('error', (e) => {
            console.log(JSON.stringify(e));
        });
        this.elementRef.addEventListener('stalled', (e) => {
            console.log(JSON.stringify(e));
        });
        this.elementRef.addEventListener('waiting', (e) => {
            console.log(JSON.stringify(e));
        });


        this.mediaSource.addEventListener('sourceopen', e => {
            URL.revokeObjectURL(this.elementRef.src);

            let mime = 'audio/webm; codecs=opus';
            this.sourceBuffer = this.mediaSource.addSourceBuffer(mime);
            this.sourceBuffer.mode = 'segments';
            this.sourceBuffer.addEventListener('updateend', sbe => {
                if (this.bufferQueue.length) {
                    this.sourceBuffer.appendBuffer(this.bufferQueue.shift());
                }
            });
        });

        this.elementRef.src = URL.createObjectURL(this.mediaSource);
    }

    dispose() {
        this.mediaSource.endOfStream();
    }

    async appendAudio(byteArray) {
        console.log(this.elementRef.readyState);
        if (this.sourceBuffer.updating) {
            this.bufferQueue.push(byteArray);
        }
        else {
            this.sourceBuffer.appendBuffer(byteArray);
        }
    }

    stop(error) {
        this.mediaSource.endOfStream(error);
    }
}
