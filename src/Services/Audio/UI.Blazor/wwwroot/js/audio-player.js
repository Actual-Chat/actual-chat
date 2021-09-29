export function create(elementRef, backendRef) {
    return new AudioPlayer(elementRef, backendRef);
}

export class AudioPlayer {
    constructor(elementRef, backendRef) {
        this.elementRef = elementRef;
        this.backendRef = backendRef;
        this.sourceBuffer = null;
        this.bufferQueue = [];
        this.mediaSource = new MediaSource();

        this.mediaSource.addEventListener('sourceopen', e => {
            URL.revokeObjectURL(this.elementRef.src);

            let mime = 'audio/webm; codecs=opus';
            this.sourceBuffer = this.mediaSource.addSourceBuffer(mime);
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
    
    async appendAudio(base64) {
        let blob = await base64ToBlob(base64);
        if (this.sourceBuffer.updating) {
            this.bufferQueue.push(blob);
        }
        else {
            this.sourceBuffer.appendBuffer(blob);
        }
    }
    
    stop(error) {
        this.mediaSource.endOfStream(error);
    }
}

// let mediaSource = null;
// let sourceBuffer = null;
// let audio = null;

async function base64ToBlob(base64) {
    let dataUrl = "data:application/octet-binary;base64," + base64;
    let res = await fetch(dataUrl);
    let buffer = await res.arrayBuffer();
    return new Uint8Array(buffer);
}
