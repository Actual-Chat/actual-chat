const LogScope = 'AudioRecorder'
const sampleRate = 24000;

export class AudioRecorder {

    constructor(backendRef, debugMode) {
        this.backendRef = backendRef;
        this.debugMode = debugMode;
        this.recording = null;
        this.isMicrophoneAvailable = false;

        if (backendRef === undefined || backendRef === null) {
            console.error(`${LogScope}.constructor.error: backendRef undefined`);
        }

        // Temporarily
        if (typeof navigator.mediaDevices === 'undefined' || !navigator.mediaDevices.getUserMedia) {
            alert('Please allow to use microphone.');

            if (!!navigator.getUserMedia) {
                alert('This browser seems supporting deprecated getUserMedia API.');
            }
        } else {
            this.isMicrophoneAvailable = true;
        }
    }

    static create(backendRef, debugMode) {
        return new AudioRecorder(backendRef, debugMode);
    }

    dispose() {
        this.recording = null;
    }

    isRecording() {
        return this.recording !== null && this.recording.recorder !== null && this.recording.recorder.getState() === 'recording';
    }

    async startRecording() {
        if (this.isRecording())
            return null;
        if (!this.isMicrophoneAvailable) {
            console.error(`${LogScope}.startRecording: microphone is unavailable.`);
            return null;
        }

        if (this.recording === null) {
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
                recorderType: MediaStreamRecorder,
                disableLogs: false,
                timeSlice: 80,
                checkForInactiveTracks: true,
                bitsPerSecond: 24000,
                audioBitsPerSecond: 24000,
                sampleRate: sampleRate,
                desiredSampleRate: sampleRate,
                bufferSize: 16384,
                audioBitrateMode: "constant",
                numberOfAudioChannels: 1,


                // as soon as the stream is available
                ondataavailable: async (blob) => {
                    if (this.debugMode) {
                        console.log(`${LogScope}.startRecording: awaiting blob.arrayBuffer(), blob.size = ${blob.size}`);
                    }
                    try {
                        let buffer = await blob.arrayBuffer();
                        let chunk = new Uint8Array(buffer);

                        await this.backendRef.invokeMethodAsync('OnAudioData', chunk);
                    } catch (e) {
                        console.error(`${LogScope}.startRecording: error ${e}`, e.stack);
                    }
                }
            });

            recorder.stopRecordingAsync = () => {
                return new Promise((resolve, _) => {
                    recorder.stopRecording(resolve);
                });
            };

            this.recording = {
                recorder: recorder,
                stream: stream
            };
        }

        let _ = this.recording.recorder.startRecording();
        await this.backendRef.invokeMethodAsync('OnStartRecording');
    }

    async stopRecording() {
        if (!this.isRecording())
            return null;

        let r = this.recording;
        this.recording = null;
        r.stream.getAudioTracks().forEach(t => t.stop());
        r.stream.getVideoTracks().forEach(t => t.stop());
        await r.recorder.stopRecordingAsync();
        await this.backendRef.invokeMethodAsync('OnStopRecording');
    }
}

