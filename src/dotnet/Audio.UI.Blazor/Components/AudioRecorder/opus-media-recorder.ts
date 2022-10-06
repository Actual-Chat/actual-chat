/* eslint-disable @typescript-eslint/ban-types */
import { CallbackRegistry } from 'callback-registry';
import { audioContextLazy } from 'audio-context-lazy';
import { ResolveCallbackMessage } from 'resolve-callback-message';

import { ProcessorOptions } from './worklets/opus-encoder-worklet-processor';
import { EncoderWorkletMessage } from './worklets/opus-encoder-worklet-message';
import { EndMessage, InitEncoderMessage, CreateEncoderMessage } from './workers/opus-encoder-worker-message';
import { VadMessage } from './workers/audio-vad-worker-message';
import { VadWorkletMessage } from './worklets/audio-vad-worklet-message';

/*
┌─────────────────────────────────┐  ┌──────────────────────┐
│                                 │  │            web worker│◄────────┐
│ ┌───┐    ┌────────────┐    ┌────┼──►VAD worker            │         │
│ │MIC├─┬─►│VAD worklet ├────┘    │  └──────────┬───────────┘         │
│ └───┘ │  └────────────┘         │             │isVoiceFound         │
│       │                         │ ┌───────────▼────────────┐        │
│       │ ┌───────────────┐       │ │              web worker│        │    ┌───────┐
│       └─►Encoder worklet├───────┼─►                        ├────────┼───►│SignalR│
│         └───────────────┘       │ │ Encoder worker         │        │    └───────┘
│               Audio thread      │ └────────────────────────┘        │
└─────────────────────────────────┘              ▲                    │
             ▲                                   │                    │
             │                                   │                    │
             │                                   │                    │
             │                                   │                    │
             └────────────────────────────┬──────┴────────────────────┘
                                          │
                                          │
                                   ┌──────┴──────┐
                                   │ Main thread │ <- You are here (OpusMediaRecorder)
                                   └─────────────┘
 */

const LogScope = 'OpusMediaRecorder';
const AUDIO_BITS_PER_SECOND = 32000;

export class OpusMediaRecorder {
    public static origin: string = new URL('opus-media-recorder.ts', import.meta.url).origin;
    private readonly debug: boolean;
    private readonly worker: Worker;
    private readonly vadWorker: Worker;
    private readonly channelCount: number = 1;
    private readonly encoderWorkerChannel: MessageChannel;
    private readonly vadWorkerChannel: MessageChannel;
    private readonly whenLoaded: Promise<void>;

    private context: AudioContext = null;
    private encoderWorklet: AudioWorkletNode = null;
    private vadWorklet: AudioWorkletNode = null;
    private callbacks = new CallbackRegistry<void>();

    public source?: MediaStreamAudioSourceNode = null;
    public stream?: MediaStream;

    // TODO: clearer states
    public state: RecordingState = 'inactive';
    public onerror: ((ev: MediaRecorderErrorEvent) => void) | null;

    constructor(debug: boolean) {
        this.debug = debug;

        this.encoderWorkerChannel = new MessageChannel();
        this.worker = new Worker('/dist/opusEncoderWorker.js');
        this.worker.onmessage = this.onWorkerMessage;
        this.worker.onerror = this.onWorkerError;

        this.vadWorkerChannel = new MessageChannel();
        this.vadWorker = new Worker('/dist/vadWorker.js');

        this.whenLoaded = this.load();
    }

    public pause(): void {
        console.assert(
            this.state !== 'inactive',
            `${LogScope}.pause: Recorder isn't initialized but got an pause() call`);
        console.assert(
            this.source != null,
            `${LogScope}.pause: Recorder: pause() call is invalid when source is null`);

        // Stop stream first
        this.source.disconnect();
        this.encoderWorklet.disconnect();
        this.vadWorklet.disconnect();
        this.state = 'paused';
    }

    public resume(): void {
        console.assert(
            this.state !== 'inactive',
            `${LogScope}.resume: Recorder isn't initialized but got an resume() call`);
        console.assert(
            this.source != null,
            `${LogScope}.resume: Recorder: resume() call is invalid when source is null`);

        // Restart streaming data
        this.source.connect(this.encoderWorklet);
        this.source.connect(this.vadWorklet);
        this.state = 'recording';
    }

    public async start(sessionId: string, chatId: string): Promise<void> {
        console.assert(
            sessionId != '' && chatId != '',
            `${LogScope}.start: sessionId and chatId both should have value specified`);

        await this.init();

        if (this.source)
            this.source.disconnect();
        this.stream = await OpusMediaRecorder.GetMicrophoneStream();
        this.source = this.context.createMediaStreamSource(this.stream);
        this.state = 'recording';

        await new Promise<void>(resolve => {
            const callbackId = this.callbacks.register(resolve);

            const { channelCount } = this;
            const initMessage: InitEncoderMessage = {
                type: 'init',
                channelCount: channelCount,
                bitsPerSecond: AUDIO_BITS_PER_SECOND,
                sessionId: sessionId,
                chatId: chatId,
                callbackId: callbackId,
            };
            // Initialize the worker
            // Expected 'initCompleted' event from the worker.
            this.worker.postMessage(initMessage);

            // Initialize new stream at the VAD worker
            const vadInitMessage: VadMessage = { type: 'reset', };
            this.vadWorker.postMessage(vadInitMessage);
        });

        // Start streaming
        this.source.connect(this.encoderWorklet);
        // It's OK to not wait for VAD worker init-new-stream message to be processed
        this.source.connect(this.vadWorklet);
    }

    public async stop(): Promise<void> {
        await new Promise<void>(resolve => {
            console.assert(
                this.state !== 'inactive',
                `${LogScope}.stop: Recorder isn't initialized but got an stop command`);
            const callbackId = this.callbacks.register(resolve);

            // Stop stream first
            if (this.source)
                this.source.disconnect();
            if (this.encoderWorklet)
                this.encoderWorklet.disconnect();
            if (this.vadWorklet)
                this.vadWorklet.disconnect();

            if (this.stream) {
                this.stream.getAudioTracks().forEach(t => t.stop());
                this.stream.getVideoTracks().forEach(t => t.stop());
            }
            this.stream = null;
            this.source = null;

            // Stop event will be triggered at _onmessageFromWorker(),
            const msg: EndMessage = {
                type: 'end',
                callbackId
            };
            // Tell encoder finalize the job and destroy itself.
            // Expects callback event from the worker.
            this.worker.postMessage(msg);
        });
        this.state = 'inactive';
    }

    // Private methods

    private load(): Promise<void> {
        const audioHubUrl = new URL('/api/hub/audio', OpusMediaRecorder.origin).toString();

        return new Promise<void>(resolve => {
            const callbackId = this.callbacks.register(resolve);

            const msg: CreateEncoderMessage = {
                type: 'create',
                audioHubUrl: audioHubUrl,
                callbackId: callbackId,
                debug: this.debug,
            };

            const crossWorkerChannel = new MessageChannel();
            // Expected 'whenLoaded' event from the worker.
            this.worker.postMessage(msg, [this.encoderWorkerChannel.port1, crossWorkerChannel.port1]);

            const msgVad: VadMessage = { type: 'create', };
            this.vadWorker.postMessage(msgVad, [this.vadWorkerChannel.port1, crossWorkerChannel.port2]);
        });
    }

    private async init(): Promise<void> {
        if (this.context != null)
            return;

        const context = await audioContextLazy.get();
        if (context.sampleRate !== 48000)
            throw new Error(`AudioContext sampleRate should be 48000, but sampleRate=${this.context.sampleRate}`);
        this.context = context; // We want to assign it only when it's a proper AudioContext
        await this.whenLoaded;

        // Encoder worklet init
        const encoderWorkletOptions: AudioWorkletNodeOptions = {
            numberOfInputs: 1,
            numberOfOutputs: 1,
            channelCount: 1,
            channelInterpretation: 'speakers',
            channelCountMode: 'explicit',
            processorOptions: {
                timeSlice: 20, // hard-coded 20ms at the codec level
            } as ProcessorOptions,
        };
        this.encoderWorklet = new AudioWorkletNode(
            this.context,
            'opus-encoder-worklet-processor',
            encoderWorkletOptions);
        const initPortMessage: EncoderWorkletMessage = { type: 'init' };
        this.encoderWorklet.port.postMessage(initPortMessage, [this.encoderWorkerChannel.port2]);

        // VAD worklet init
        const vadWorkletOptions: AudioWorkletNodeOptions = {
            numberOfInputs: 1,
            numberOfOutputs: 1,
            channelCount: 1,
            channelInterpretation: 'speakers',
            channelCountMode: 'explicit',
        };
        this.vadWorklet = new AudioWorkletNode(this.context, 'audio-vad-worklet-processor', vadWorkletOptions);
        const vadInitPortMessage: VadWorkletMessage = { type: 'init' };
        this.vadWorklet.port.postMessage(vadInitPortMessage, [this.vadWorkerChannel.port2]);
    }

    private static async GetMicrophoneStream(): Promise<MediaStream> {
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
        let mediaStream = await navigator.mediaDevices.getUserMedia(constraints as MediaStreamConstraints);
        const tracks = mediaStream.getAudioTracks();
        if (!tracks[0]) {
            throw new Error('DOMException: UnknownError, media track not found.');
        }
        return mediaStream;
    }

    private onWorkerMessage = (ev: MessageEvent<ResolveCallbackMessage>) => {
        const { callbackId } = ev.data;
        this.callbacks.invoke(callbackId);
    };

    private onWorkerError = (error: ErrorEvent) => {
        // Stop stream first
        if (this.source)
            this.source.disconnect();
        if (this.encoderWorklet)
            this.encoderWorklet.disconnect();
        if (this.vadWorklet)
            this.vadWorklet.disconnect();

        console.error(`${LogScope}.onWorkerError: FileName: ${error.filename} Line: ${error.lineno} Message: ${error.message}`);
    };
}
