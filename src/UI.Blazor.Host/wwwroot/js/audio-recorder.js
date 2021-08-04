let recorder = null;

const sampleRate = 16000;

export function isRecording() {
    return recorder !== null && recorder.getState() === 'recording';
}

export async function initialize() {
    if (recorder !== null) return;
    // Temporarily
    if(typeof navigator.mediaDevices === 'undefined' || !navigator.mediaDevices.getUserMedia) {
        alert('Please allow to use microphone.');

        if(!!navigator.getUserMedia) {
            alert('This browser seems supporting deprecated getUserMedia API.');
        }
    }
    let stream = await navigator.mediaDevices.getUserMedia({
        audio: {
            channelCount: 1,
            sampleRate: sampleRate,
            autoGainControl: {
                ideal: true
            },
            echoCancellation: {
                ideal: true
            },
            noiseSuppression: {
                ideal: true
            }
        },
        video: false
    });
    recorder = RecordRTC(stream, {
        type: 'audio',
        mimeType: 'audio/webm; codecs=opus',
        sampleRate: sampleRate,
        desiredSampleRate: sampleRate,
        bufferSize: 4096,
        numberOfAudioChannels: 1,
        timeSlice: 500,
        disableLogs: false,
        // as soon as the stream is available
        ondataavailable(blob) {
            console.log("audio blob is ready, Blob: %s", blob);
            // if(!me.eventService.getIsPlaying()) {
            //     me.ioService.sendBinaryStream(blob);
            //     me.waveform.visualize();
            // }
        }
    });
}

export async function startRecording() {
    if (isRecording())
        return null;
    
    await initialize();
    
    recorder.startRecording();
}

export async function stopRecording() {
    if (!isRecording())
        return null;

    recorder.stopRecording();
    recorder = null;
}