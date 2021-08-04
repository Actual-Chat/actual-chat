let recording = null;
let lastBackend = null;

const sampleRate = 16000;

async function blobToBase64(blob) {
    return new Promise((resolve, _) => {
        const reader = new FileReader();
        reader.onloadend = () => resolve(reader.result);
        reader.readAsDataURL(blob);
    });
}

export function isRecording() {
    return recording !== null && recording.recorder !== null && recording.recorder.getState() === 'recording';
}

export async function initialize(backend) {
    if (recording !== null) return;
    if (backend === undefined || backend === null) {
        console.error("Audio Recorder backend is undefined");
    }
 
    lastBackend = backend;
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
    let recorder = RecordRTC(stream, {
        type: 'audio',
        mimeType: 'audio/webm; codecs=opus',
        sampleRate: sampleRate,
        desiredSampleRate: sampleRate,
        bufferSize: 4096,
        numberOfAudioChannels: 1,
        timeSlice: 320,
        disableLogs: false,
        // as soon as the stream is available
        ondataavailable: (blob) => {
            let base64 = blobToBase64(blob);
            console.log("audio blob is ready, Blob length: %d", base64.length);

            backend.invokeMethodAsync('AudioDataAvailable', base64);
        }
    });
    
    recorder.stopRecordingAsync = () => {
        return new Promise((resolve,_) => {
            recorder.stopRecording(resolve);
        }); 
    };

    recording = {
        backend: backend,
        recorder: recorder
    };
}

export async function startRecording() {
    if (isRecording())
        return null;
    
    if (recording === null) {
        await initialize(lastBackend);
    }
    
    recording.recorder.startRecording();
    await recording.backend.invokeMethodAsync('RecordingStarted');
}

export async function stopRecording() {
    if (!isRecording())
        return null;
    
    let r = recording;
    recording = null;
    await r.recorder.stopRecordingAsync();
    await r.backend.invokeMethodAsync('RecordingStopped');
}