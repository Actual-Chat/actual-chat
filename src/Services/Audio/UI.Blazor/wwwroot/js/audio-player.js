let mediaSource = null;
let sourceBuffer = null;
let audio = null;

async function base64ToBlob(base64) {
    let dataUrl = "data:application/octet-binary;base64," + base64;
    let res = await fetch(dataUrl);
    let buffer = await res.arrayBuffer();
    return new Uint8Array(buffer);
}

export async function initialize() {
    audio = document.getElementById('player');
    mediaSource = new MediaSource();
    
    mediaSource.addEventListener('sourceopen', e => {
        URL.revokeObjectURL(audio.src);
        
        let mime = 'audio/webm; codecs=opus';
        sourceBuffer = mediaSource.addSourceBuffer(mime);
    });
    audio.src = URL.createObjectURL(mediaSource);
}

export async function appendAudio(base64){
    if (mediaSource === null)
        return;
    
    let blob = await base64ToBlob(base64);
    sourceBuffer.appendBuffer(blob);
}