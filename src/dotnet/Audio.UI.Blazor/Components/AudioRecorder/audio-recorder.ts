import { OpusMediaRecorder } from './opus-media-recorder';
import { AudioContextPool } from 'audio-context-pool';
import {
    DataRecordingEvent,
    IRecordingEventQueue,
    PauseRecordingEvent,
    RecordingEventQueue,
    ResumeRecordingEvent
} from './recording-event-queue';
import { VoiceActivityChanged } from './audio-vad';
import { toHexString } from "./to-hex-string";

const LogScope = 'AudioRecorder';
const SampleRate = 48000;

self["StandardMediaRecorder"] = self.MediaRecorder;
self["OpusMediaRecorder"] = OpusMediaRecorder;

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
        streamNode: MediaStreamAudioSourceNode,
    };

    public constructor(blazorRef: DotNet.DotNetObject, debugMode: boolean, queue: IRecordingEventQueue) {
        this.blazorRef = blazorRef;
        this.debugMode = debugMode;
        this.recording = null;
        this.isMicrophoneAvailable = false;
        this.queue = queue;

        const options: MediaRecorderOptions = {
            // @ts-ignore
            mimeType: 'audio/webm;codecs=opus',
            bitsPerSecond: 32000,
            audioBitsPerSecond: 32000,
        };
        this.recorder = new OpusMediaRecorder(options);
        this.recorder.ondataavailable = async (be: BlobEvent) => {
            try {
                const blob = be.data;
                let buffer = await blob.arrayBuffer();
                let chunk = new Uint8Array(buffer);
                this.queue.append(new DataRecordingEvent(chunk));
            } catch (e) {
                console.error(`${LogScope}.startRecording: error ${e}`, e.stack);
            }
        };
        this.recorder.onerror = async (errorEvent: MediaRecorderErrorEvent) => {
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
                    console.log(`AudioRecorder.queue.sendAsync: sending ${packet.length} bytes - ${toHexString(packet.slice(0, 10))}`);
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

        if (this.context == null) {
            const recorderContext = await AudioContextPool.get("recorder") as AudioContext;
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
            }
        }

        if (this.recording == null) {
            const stream: MediaStream = await navigator.mediaDevices.getUserMedia({
                audio: {
                    channelCount: 1,
                    sampleRate: 48000,
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

            // TODO: refactor this after deleting recordrtc
            this.vadWorker.postMessage({ topic: 'init-new-stream' });
            const streamNode = this.context.recorderContext.createMediaStreamSource(stream);
            streamNode.connect(this.context.vadWorkletNode);

            this.recording = {
                stream: stream,
                streamNode: streamNode,
            };
        }


        this.queue.append(new ResumeRecordingEvent(Date.now(), 0));
        await this.recorder.startAsync(this.recording.streamNode, 40);
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
            recording.streamNode.disconnect();
            recording.streamNode = null;
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
