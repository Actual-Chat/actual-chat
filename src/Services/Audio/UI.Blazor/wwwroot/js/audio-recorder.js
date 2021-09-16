let recording = null;
let currentBackend = null;
let isMicrophoneAvailable = false;

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
 
    currentBackend = backend;
    // Temporarily
    if(typeof navigator.mediaDevices === 'undefined' || !navigator.mediaDevices.getUserMedia) {
        alert('Please allow to use microphone.');

        if(!!navigator.getUserMedia) {
            alert('This browser seems supporting deprecated getUserMedia API.');
        }
    }
    else {
        isMicrophoneAvailable = true;
    }
}

export async function startRecording() {
    if (isRecording())
        return null;
    if (currentBackend === null) {
        console.error("Audio Recorder backend is undefined. Call 'initialize' first");
        return null;
    }
    if (!isMicrophoneAvailable)
    {
        console.error("Microphone is unavailable");
        return null;
    }
    
    let backend = currentBackend;
    if (recording === null) {
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
            audioBitsPerSecond: 32 * 1024,
            checkForInactiveTracks: true,
            audioBitrateMode: "variable",
            bufferSize: 4096,
            numberOfAudioChannels: 1,
            timeSlice: 320,
            disableLogs: false,
            // as soon as the stream is available
            ondataavailable: async (blob) => {
                let typePrefix = `data:${blob.type};base64,`;
                let base64Typed = await blobToBase64(blob);
                let base64 = base64Typed.substr(typePrefix.length);
                console.log("audio blob is ready, Blob length: %d", base64.length);

                await backend.invokeMethodAsync('OnAudioData', base64);
            }
        });

        recorder.stopRecordingAsync = () => {
            return new Promise((resolve,_) => {
                recorder.stopRecording(resolve);
            });
        };

        recording = {
            backend: backend,
            recorder: recorder,
            stream: stream
        };
    }
    
    recording.recorder.startRecording();
    await recording.backend.invokeMethodAsync('OnStartRecording');
}

export async function stopRecording() {
    if (!isRecording())
        return null;
    
    let r = recording;
    recording = null;
    r.stream.getAudioTracks().forEach(t => t.stop());
    r.stream.getVideoTracks().forEach(t => t.stop());
    await r.recorder.stopRecordingAsync();
    await r.backend.invokeMethodAsync('OnStopRecording');
}
