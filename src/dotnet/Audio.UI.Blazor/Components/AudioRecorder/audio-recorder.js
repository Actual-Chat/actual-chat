const sampleRate = 16000;

export class AudioRecorder {

    static create(backendRef, debugMode) {
        return new AudioRecorder(backendRef, debugMode);
    }

    constructor(backendRef, debugMode) {
        this.backendRef = backendRef;
        this.debugMode = debugMode;
        this.recording = null;
        this.isMicrophoneAvailable = false;

        if (backendRef === undefined || backendRef === null) {
            if (this.debugMode) {
                console.error("Audio Recorder backend is undefined");
            }
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
            if (this.debugMode) {
                console.error("Microphone is unavailable");
            }
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
                    if (this.debugMode) {
                        console.log("audio blob is ready, Blob size: %d", blob.size);
                    }
                    try {
                        let buffer = await blob.arrayBuffer();
                        let chunk = new Uint8Array(buffer);

                        await this.backendRef.invokeMethodAsync('OnAudioData', chunk);
                    } catch (err) {
                        if (this.debugMode) {
                            console.error(err);
                        }
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

