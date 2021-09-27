let mediaSource = null;
let sourceBuffer = null;
let audio = null;

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

export async function appendAudio(blob){
    if (mediaSource === null)
        return;
    
    sourceBuffer.appendBuffer(blob);
}