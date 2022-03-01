import { ObjectPool } from 'object-pool';
import { AudioContextPool } from 'audio-context-pool';
import { OpusMediaRecorder } from './opus-media-recorder';

const LogScope = 'AudioRecorder';

export class AudioRecorder {
    private static recorderPool = new ObjectPool<OpusMediaRecorder>(() => {
        const options: MediaRecorderOptions = {
            mimeType: 'audio/webm;codecs=opus',
            bitsPerSecond: 32000,
            audioBitsPerSecond: 32000,
        };
        return new OpusMediaRecorder(options);
    });

    private readonly debugMode: boolean;
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly isMicrophoneAvailable: boolean;
    private recorder: OpusMediaRecorder;
    private readonly sessionId: string;
    private readonly chatId: string;

    private context: {
        recorderContext: AudioContext,
    };

    private recording: {
        stream: MediaStream,
        source: MediaStreamAudioSourceNode,
    };

    public constructor(blazorRef: DotNet.DotNetObject, sessionId: string, chatId: string, debugMode: boolean) {
        this.blazorRef = blazorRef;
        this.debugMode = debugMode;
        this.sessionId = sessionId;
        this.chatId = chatId;
        this.recording = null;
        this.isMicrophoneAvailable = false;

        if (blazorRef == null)
            console.error(`${LogScope}.constructor: blazorRef == null`);

        // Temporarily
        if (typeof navigator.mediaDevices === 'undefined' || !navigator.mediaDevices.getUserMedia) {
            alert('Please allow to use microphone.');

            if (navigator['getUserMedia'] !== undefined) {
                alert('This browser seems supporting deprecated getUserMedia API.');
            }
        } else {
            this.isMicrophoneAvailable = true;
        }
    }

    public static create(blazorRef: DotNet.DotNetObject, sessionId: string, chatId: string, debugMode: boolean) {
        return new AudioRecorder(blazorRef, sessionId, chatId, debugMode);
    }

    public static async initRecorderPool(): Promise<void> {
        const recorder = await this.recorderPool.get();
        await this.recorderPool.release(recorder);
    }

    public dispose() {
        this.recording = null;
    }

    public async startRecording(): Promise<void> {
        if (this.isRecording())
            return;

        if (!this.isMicrophoneAvailable) {
            console.error(`${LogScope}.startRecording: microphone is unavailable.`);
            return;
        }

        this.recorder = await AudioRecorder.recorderPool.get();
        this.recorder.onerror = (errorEvent: MediaRecorderErrorEvent) => {
            // eslint-disable-next-line @typescript-eslint/restrict-template-expressions
            console.error(`${LogScope}.onerror: ${errorEvent['message']}`);
        };

        if (this.context == null) {
            const recorderContext = await AudioContextPool.get('main') as AudioContext;
            this.context = {
                recorderContext: recorderContext,
            };
        }

        if (this.recording == null) {
            /**
             * [Chromium]{@link https://github.com/chromium/chromium/blob/main/third_party/blink/renderer/modules/mediastream/media_constraints_impl.cc#L98-L116}
             * [Chromium]{@link https://github.com/chromium/chromium/blob/main/third_party/blink/renderer/platform/mediastream/media_constraints.cc#L358-L372}
             */
            const constraints: MediaStreamConstraints & any = {
                audio: {
                    channelCount: 1,
                    sampleRate: 48000,
                    sampleSize: 32,
                    autoGainControl: true,
                    echoCancellation: true,
                    noiseSuppression: true,
                    googEchoCancellation: true,
                    googEchoCancellation2: true,
                    latency: 0,
                    advanced: [
                        { autoGainControl: { exact: true } },
                        { echoCancellation: { exact: true } },
                        { noiseSuppression: { exact: true } },
                        { googEchoCancellation: { ideal: true } },
                        { googEchoCancellation2: { ideal: true } },
                        { googAutoGainControl: { ideal: true } },
                        { googNoiseSuppression: { ideal: true } },
                        { googNoiseSuppression2: { ideal: true } },
                        { googExperimentalAutoGainControl: { ideal: true } },
                        { googExperimentalEchoCancellation: { ideal: true } },
                        { googExperimentalNoiseSuppression: { ideal: true } },
                        { googHighpassFilter: { ideal: true } },
                        { googTypingNoiseDetection: { ideal: true } },
                        { googAudioMirroring: { exact: false } },
                    ],
                },
                video: false,
            };
            const stream: MediaStream = await navigator.mediaDevices.getUserMedia(constraints as MediaStreamConstraints);
            const source = this.context.recorderContext.createMediaStreamSource(stream);

            this.recording = {
                stream: stream,
                source: source,
            };
        }

        const { blazorRef, sessionId, chatId, recording, debugMode } = this;
        await this.recorder.startAsync(recording.source, 20, sessionId, chatId, debugMode);
        await blazorRef.invokeMethodAsync('OnStartRecording');
    }

    public async stopRecording(): Promise<void> {
        if (!this.isRecording())
            return;
        if (this.debugMode)
            console.log(`${LogScope}.stopRecording: started`);

        const recording = this.recording;
        this.recording = null;

        if (recording !== null) {
            recording.source.disconnect();
            recording.source = null;
            recording.stream.getAudioTracks().forEach(t => t.stop());
            recording.stream.getVideoTracks().forEach(t => t.stop());
            await this.recorder.stopAsync();
            await AudioRecorder.recorderPool.release(this.recorder);
        }
        await this.blazorRef.invokeMethodAsync('OnRecordingStopped');
        if (this.debugMode)
            console.log(`${LogScope}.stopRecording: completed`);
    }

    private isRecording() {
        return this.recording !== null && this.recording.stream !== null;
    }
}
