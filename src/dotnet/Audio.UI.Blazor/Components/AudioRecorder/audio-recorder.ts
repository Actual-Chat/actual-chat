import type * as RecordRTC from 'recordrtc';

const LogScope = 'AudioRecorder';
const sampleRate = 24000;

export class AudioRecorder {
    private readonly _debugMode: boolean;
    private readonly isMicrophoneAvailable: boolean;
    private readonly _blazorRef: DotNet.DotNetObject;
    private recording: { recorder: RecordRTC, stream: MediaStream; };

    public constructor(blazorRef: DotNet.DotNetObject, debugMode: boolean) {
        this._blazorRef = blazorRef;
        this._debugMode = debugMode;
        this.recording = null;
        this.isMicrophoneAvailable = false;

        if (blazorRef === undefined || blazorRef === null) {
            console.error(`${LogScope}.constructor.error: blazorRef is undefined`);
        }

        // Temporarily
        if (typeof navigator.mediaDevices === 'undefined' || !navigator.mediaDevices.getUserMedia) {
            alert('Please allow to use microphone.');

            if (navigator["getUserMedia"] !== undefined) {
                alert('This browser seems supporting deprecated getUserMedia API.');
            }
        } else {
            this.isMicrophoneAvailable = true;
        }
    }

    public static create(blazorRef: DotNet.DotNetObject, debugMode: boolean) {
        return new AudioRecorder(blazorRef, debugMode);
    }

    public dispose() {
        this.recording = null;
    }

    private isRecording() {
        return this.recording !== null && this.recording.recorder !== null && this.recording.recorder.getState() === 'recording';
    }

    public async startRecording(): Promise<any> {
        if (this.isRecording())
            return null;
        if (!this.isMicrophoneAvailable) {
            console.error(`${LogScope}.startRecording: microphone is unavailable.`);
            return null;
        }

        if (this.recording === null) {
            let stream: MediaStream = await navigator.mediaDevices.getUserMedia({
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
            const options: RecordRTC.Options = {
                type: 'audio',
                //@ts-expect-error
                mimeType: 'audio/webm; codecs=opus',
                //@ts-expect-error
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
                ondataavailable: async (blob: Blob) => {
                    if (this._debugMode) {
                        console.log(`${LogScope}.startRecording: awaiting blob.arrayBuffer(), blob.size = ${blob.size}`);
                    }
                    try {
                        let buffer = await blob.arrayBuffer();
                        let chunk = new Uint8Array(buffer);

                        await this._blazorRef.invokeMethodAsync('OnAudioData', chunk);
                    } catch (e) {
                        console.error(`${LogScope}.startRecording: error ${e}`, e.stack);
                    }
                }
            };
            // @ts-expect-error
            let recorder: RecordRTC = RecordRTC(stream, options);

            recorder["stopRecordingAsync"] = (): Promise<void> =>
                new Promise((resolve, _) => recorder.stopRecording(() => resolve));

            this.recording = {
                recorder: recorder,
                stream: stream
            };
        }

        let _ = this.recording.recorder.startRecording();
        await this._blazorRef.invokeMethodAsync('OnStartRecording');
    }

    public async stopRecording() {
        if (!this.isRecording())
            return null;

        let r = this.recording;
        this.recording = null;
        r.stream.getAudioTracks().forEach(t => t.stop());
        r.stream.getVideoTracks().forEach(t => t.stop());
        await r.recorder["stopRecordingAsync"]();
        await this._blazorRef.invokeMethodAsync('OnStopRecording');
    }
}

