import { DataEvent, OpusMediaRecorder, OpusMediaRecorderOptions } from './opus-media-recorder';
import { AudioContextPool } from 'audio-context-pool';
import {
    DataRecordingEvent,
    IRecordingEventQueue,
    PauseRecordingEvent,
    RecordingEventQueue,
    ResumeRecordingEvent
} from './recording-event-queue';
import { VoiceActivityChanged } from './audio-vad';
import { toHexString } from './to-hex-string';

const LogScope = 'AudioRecorder';

export class AudioRecorder {
    private readonly debugMode: boolean;
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly isMicrophoneAvailable: boolean;
    protected readonly queue: IRecordingEventQueue;
    private readonly recorder: OpusMediaRecorder;
    private readonly vadWorker: Worker;
    private readonly vadChannel: MessageChannel;

    private context: {
        recorderContext: AudioContext,
        vadWorkletNode: AudioWorkletNode,
    };

    private recording: {
        stream: MediaStream,
        source: MediaStreamAudioSourceNode,
    };

    public constructor(blazorRef: DotNet.DotNetObject, debugMode: boolean, queue: IRecordingEventQueue) {
        this.blazorRef = blazorRef;
        this.debugMode = debugMode;
        this.recording = null;
        this.isMicrophoneAvailable = false;
        this.queue = queue;

        const options: OpusMediaRecorderOptions = {
            mimeType: 'audio/webm;codecs=opus',
            bitsPerSecond: 32000,
            audioBitsPerSecond: 32000,
        };
        this.recorder = new OpusMediaRecorder(options);
        // this.recorder.ondatarecorded = async (de: DataEvent) => {
        //     try {
        //         this.queue.append(new DataRecordingEvent(de.data));
        //     } catch (e) {
        //         console.error(`${LogScope}.startRecording: error ${e}`, e.stack);
        //     }
        // };
        this.recorder.onerror = (errorEvent: MediaRecorderErrorEvent) => {
            console.error(`${LogScope}.onerror: ${errorEvent}`);
        };

        this.vadWorker = new Worker('/dist/vadWorker.js');
        this.vadChannel = new MessageChannel();
        this.vadWorker.onmessage = (ev: MessageEvent<VoiceActivityChanged>) => {
            const vadEvent = ev.data;
            if (this.debugMode)
                console.log(`${LogScope}.startRecording: vadEvent =`, vadEvent);

            if (this.isRecording()) {
                if (vadEvent.kind === 'end') {
                    this.queue.append(new PauseRecordingEvent(Date.now(), vadEvent.offset));
                }
                else {
                    this.queue.append(new ResumeRecordingEvent(Date.now(), vadEvent.offset));
                }
            }
        };
        this.vadWorker.postMessage({ topic: 'init-port' }, [this.vadChannel.port1]);

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

    public static create(blazorRef: DotNet.DotNetObject, debugMode: boolean) {
        const queue: IRecordingEventQueue = new RecordingEventQueue({
            debugMode: debugMode && false,
            minChunkSize: 64,
            chunkSize: 1024,
            maxFillBufferTimeMs: 400,
            sendAsync: async (packet: Uint8Array): Promise<void> => {
                if (debugMode)
                    console.log(`AudioRecorder.queue.sendAsync: sending ${packet.length} bytes - ${toHexString(packet.slice(0, 10))}`);
                await blazorRef.invokeMethodAsync('OnAudioEventChunk', packet);
            },
        });
        return new AudioRecorder(blazorRef, debugMode, queue);
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

        if (this.context == null) {
            const recorderContext = await AudioContextPool.get('main') as AudioContext;
            const audioWorkletOptions: AudioWorkletNodeOptions = {
                numberOfInputs: 1,
                numberOfOutputs: 1,
                channelCount: 1,
                channelInterpretation: 'speakers',
                channelCountMode: 'explicit',
            };
            const vadWorkletNode = new AudioWorkletNode(recorderContext, 'audio-vad-worklet-processor', audioWorkletOptions);
            vadWorkletNode.port.postMessage({ topic: 'init-port' }, [this.vadChannel.port2]);

            this.context = {
                recorderContext: recorderContext,
                vadWorkletNode: vadWorkletNode,
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
                video: false
            };
            const stream: MediaStream = await navigator.mediaDevices.getUserMedia(constraints as MediaStreamConstraints);
            this.vadWorker.postMessage({ topic: 'init-new-stream' });
            const source = this.context.recorderContext.createMediaStreamSource(stream);
            source.connect(this.context.vadWorkletNode);

            this.recording = {
                stream: stream,
                source: source,
            };
        }

        this.queue.append(new ResumeRecordingEvent(Date.now(), 0));
        await this.recorder.startAsync(this.recording.source, 40);
        await this.blazorRef.invokeMethodAsync('OnStartRecording');
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
            this.context.vadWorkletNode.disconnect();
            recording.stream.getAudioTracks().forEach(t => t.stop());
            recording.stream.getVideoTracks().forEach(t => t.stop());
            await this.recorder.stopAsync();
        }
        await this.queue.flushAsync();
        await this.blazorRef.invokeMethodAsync('OnRecordingStopped');
        if (this.debugMode)
            console.log(`${LogScope}.stopRecording: completed`);
    }

    private isRecording() {
        return this.recording !== null && this.recording.stream !== null;
    }
}
