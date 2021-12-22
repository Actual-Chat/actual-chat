import RecordRTC, { MediaStreamRecorder, Options, State } from 'recordrtc';
import OpusMediaRecorder from 'opus-media-recorder';
import WebMOpusWasm from 'opus-media-recorder/WebMOpusEncoder.wasm';
import { AudioContextPool } from 'audio-context-pool';
import {
    DataRecordingEvent,
    IRecordingEventQueue,
    PauseRecordingEvent,
    RecordingEventQueue,
    ResumeRecordingEvent
} from './recording-event-queue';
import { VoiceActivityChanged } from './audio-vad';

const LogScope = 'AudioRecorder';
const sampleRate = 48000;

const opusWorkerOptions = {
    encoderWorkerFactory: _ => new Worker('/dist/encoderWorker.js'),
    WebMOpusEncoderWasmPath: WebMOpusWasm
};

const OpusMediaRecorderWrapper = Object.assign(function (stream: MediaStream, options?: MediaRecorderOptions) {
    return new OpusMediaRecorder(stream, options, opusWorkerOptions);
}, OpusMediaRecorder);

self["StandardMediaRecorder"] = self.MediaRecorder;
self["OpusMediaRecorder"] = OpusMediaRecorderWrapper;

self.MediaRecorder = OpusMediaRecorderWrapper;

export class AudioRecorder {
    protected readonly _debugMode: boolean;
    protected readonly _blazorRef: DotNet.DotNetObject;
    protected readonly isMicrophoneAvailable: boolean;
    protected recording: {
        recorder: RecordRTC,
        stream: MediaStream,
        context: AudioContext,
        vadWorkletNode: AudioWorkletNode,
        mediaStreamAudioSourceNode?: MediaStreamAudioSourceNode,
    };
    protected _queue: IRecordingEventQueue;

    public constructor(blazorRef: DotNet.DotNetObject, debugMode: boolean, queue: IRecordingEventQueue) {
        this._blazorRef = blazorRef;
        this._debugMode = debugMode;
        this.recording = null;
        this.isMicrophoneAvailable = false;
        this._queue = queue;

        if (blazorRef == null)
            console.error(`${LogScope}.constructor: blazorRef == null`);

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
        const queue: IRecordingEventQueue = new RecordingEventQueue({
            debugMode: debugMode && false,
            minChunkSize: 64,
            chunkSize: 1024,
            maxFillBufferTimeMs: 400,
            sendAsync: async (packet: Uint8Array): Promise<void> => {
                if (debugMode)
                    console.log(`AudioRecorder.queue.sendAsync: sending ${packet.length} bytes`);
                await blazorRef.invokeMethodAsync('OnAudioEventChunk', packet);
            },
        });
        return new AudioRecorder(blazorRef, debugMode, queue);
    }

    public static changeMediaRecorder(useStandardMediaRecorder: boolean) {
        self.MediaRecorder = useStandardMediaRecorder
            ? self["StandardMediaRecorder"]
            : self["OpusMediaRecorder"];
    }

    public static isStandardMediaRecorder(): boolean {
        return self.MediaRecorder === self["StandardMediaRecorder"];
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

        const channel = new MessageChannel();
        const worker = new Worker('/dist/vadWorker.js');
        worker.onmessage = (ev: MessageEvent<VoiceActivityChanged>) => {
            const vadEvent = ev.data;
            const recording = this.recording;

            if (recording !== null) {
                const state = recording.recorder.getState();
                if (this._debugMode)
                    console.log(`${LogScope}.startRecording: state = ${state}`);

                if (vadEvent.kind === 'end') {
                    this._queue.append(new PauseRecordingEvent());
                }
                else {
                    this._queue.append(new ResumeRecordingEvent());
                }
            }
            if (this._debugMode)
                console.log(`${LogScope}.startRecording: vadEvent =`, vadEvent);
        };
        worker.postMessage({ topic: 'init-port' }, [channel.port1]);

        if (this.recording === null) {
            let stream: MediaStream = await navigator.mediaDevices.getUserMedia({
                audio: {
                    channelCount: 1,
                    sampleRate: sampleRate,
                    sampleSize: 32,
                    // @ts-ignore
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
            const options: Options = {
                type: 'audio',
                // @ts-ignore
                mimeType: 'audio/webm;codecs=opus',
                recorderType: MediaStreamRecorder,
                disableLogs: false,
                timeSlice: 20,
                checkForInactiveTracks: true,
                sampleRate: sampleRate,
                desiredSampleRate: sampleRate,
                bufferSize: 16384,
                bitsPerSecond: 32000,
                audioBitsPerSecond: 32000,
                audioBitrateMode: "constant",
                numberOfAudioChannels: 1,


                // as soon as the stream is available
                ondataavailable: async (blob: Blob) => {
                    try {
                        let buffer = await blob.arrayBuffer();
                        let chunk = new Uint8Array(buffer);
                        this._queue.append(new DataRecordingEvent(chunk));
                    } catch (e) {
                        console.error(`${LogScope}.startRecording: error ${e}`, e.stack);
                    }
                }
            };
            let recorder: RecordRTC = new RecordRTC(stream, options);

            recorder["stopRecordingAsync"] = (): Promise<void> =>
                new Promise((resolve, _) => recorder.stopRecording(() => resolve()));

            const audioContext = await AudioContextPool.get("vad") as AudioContext;

            const audioWorkletOptions: AudioWorkletNodeOptions = {
                numberOfInputs: 1,
                numberOfOutputs: 1,
                channelCount: 1,
                channelInterpretation: 'speakers',
                channelCountMode: 'explicit',
            };
            const vadWorkletNode = new AudioWorkletNode(audioContext, 'audio-vad.worklet-processor', audioWorkletOptions);

            this.recording = {
                recorder: recorder,
                stream: stream,
                context: audioContext,
                vadWorkletNode: vadWorkletNode,
                mediaStreamAudioSourceNode: null,
            };
        }
        // TODO: refactor this after deleting recordrtc
        this.recording.mediaStreamAudioSourceNode = this.recording.context.createMediaStreamSource(this.recording.stream);
        this.recording.mediaStreamAudioSourceNode.connect(this.recording.vadWorkletNode);
        this.recording.vadWorkletNode.port.postMessage({ topic: 'init-port' }, [channel.port2]);
        this.recording.recorder.startRecording();
        await this._blazorRef.invokeMethodAsync('OnStartRecording');
    }

    public async stopRecording(): Promise<void> {
        if (!this.isRecording())
            return;
        if (this._debugMode)
            console.log(`${LogScope}.stopRecording: started`);

        const recording = this.recording;
        this.recording = null;

        if (recording !== null) {
            recording.mediaStreamAudioSourceNode.disconnect();
            recording.mediaStreamAudioSourceNode = null;
            recording.vadWorkletNode.disconnect();
            recording.stream.getAudioTracks().forEach(t => t.stop());
            recording.stream.getVideoTracks().forEach(t => t.stop());
            await recording.recorder["stopRecordingAsync"]();
        }
        await this._queue.flushAsync();
        await this._blazorRef.invokeMethodAsync('OnRecordingStopped');
        if (this._debugMode)
            console.log(`${LogScope}.stopRecording: completed`);
    }

    private isRecording() {
        return this.recording !== null && this.recording.recorder !== null && this.recording.recorder.getState() === 'recording';
    }
}
