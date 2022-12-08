/* eslint-disable @typescript-eslint/ban-types */
import { completeRpc, RpcResultMessage, rpc } from 'rpc';
import { audioContextLazy } from 'audio-context-lazy';

import { ProcessorOptions } from './worklets/opus-encoder-worklet-processor';
import { EncoderWorkletMessage } from './worklets/opus-encoder-worklet-message';
import { VadMessage } from './workers/audio-vad-worker-message';
import { VadWorkletMessage } from './worklets/audio-vad-worklet-message';
import { CreateEncoderMessage, EndMessage, InitEncoderMessage } from './workers/opus-encoder-worker-message';
import { Log, LogLevel } from 'logging';

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
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

const AUDIO_BITS_PER_SECOND = 32000;

export class OpusMediaRecorder {
    public static origin: string = new URL('opus-media-recorder.ts', import.meta.url).origin;
    private readonly worker: Worker;
    private readonly vadWorker: Worker;
    private readonly channelCount: number = 1;
    private readonly encoderWorkerChannel: MessageChannel;
    private readonly vadWorkerChannel: MessageChannel;
    private readonly whenLoaded: Promise<void>;

    private context: AudioContext = null;
    private encoderWorklet: AudioWorkletNode = null;
    private vadWorklet: AudioWorkletNode = null;

    public source?: MediaStreamAudioSourceNode = null;
    public stream?: MediaStream;

    // TODO: clearer states
    public state: RecordingState = 'inactive';

    constructor() {
        this.encoderWorkerChannel = new MessageChannel();
        this.worker = new Worker('/dist/opusEncoderWorker.js');
        this.worker.onmessage = this.onWorkerMessage;
        this.worker.onerror = this.onWorkerError;

        this.vadWorkerChannel = new MessageChannel();
        this.vadWorker = new Worker('/dist/vadWorker.js');

        this.whenLoaded = this.load();
    }

    public async start(sessionId: string, chatId: string): Promise<void> {
        warnLog?.assert(sessionId != '', `start: sessionId is unspecified`);
        warnLog?.assert(chatId != '', `start: chatId is unspecified`);

        await this.init();

        if (this.source)
            this.source.disconnect();
        this.stream = await OpusMediaRecorder.GetMicrophoneStream();
        this.source = this.context.createMediaStreamSource(this.stream);
        this.state = 'recording';

        await rpc((rpcResult) => {
            const initMessage: InitEncoderMessage = {
                type: 'init',
                sessionId: sessionId,
                chatId: chatId,
                rpcResultId: rpcResult.id,
            };
            // Initialize the worker
            this.worker.postMessage(initMessage);
            // Initialize new stream at the VAD worker
            const vadInitMessage: VadMessage = { type: 'reset', };
            this.vadWorker.postMessage(vadInitMessage);

            // Start streaming
            this.source.connect(this.vadWorklet);
            this.source.connect(this.encoderWorklet);
        });
    }

    public async stop(): Promise<void> {
        await rpc((rpcResult) => {
            warnLog?.assert(this.state !== 'inactive', `stop: state == 'inactive'`);

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

            const msg: EndMessage = {
                type: 'end',
                rpcResultId: rpcResult.id
            };
            // Tell encoder finalize the job and destroy itself.
            this.worker.postMessage(msg);
        });
        this.state = 'inactive';
    }

    // Private methods

    private async load(): Promise<void> {
        const audioHubUrl = new URL('/api/hub/audio', OpusMediaRecorder.origin).toString();

        await rpc((rpcResult) => {
            const msg: CreateEncoderMessage = {
                type: 'create',
                audioHubUrl: audioHubUrl,
                rpcResultId: rpcResult.id,
            };

            const crossWorkerChannel = new MessageChannel();
            this.worker.postMessage(
                msg,
                [this.encoderWorkerChannel.port1, crossWorkerChannel.port1]);

            const msgVad: VadMessage = { type: 'create', };
            this.vadWorker.postMessage(msgVad, [this.vadWorkerChannel.port1, crossWorkerChannel.port2]);
        });
    }

    private async init(): Promise<void> {
        const context = await audioContextLazy.get();
        if (context.sampleRate !== 48000)
            throw new Error(`AudioContext sampleRate should be 48000, but sampleRate=${this.context.sampleRate}`);

        this.context = context;
        if (this.context['initialized'])
            return;

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
        this.context['initialized'] = true;
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

    private onWorkerMessage = (ev: MessageEvent<RpcResultMessage>) => {
        void completeRpc(ev.data);
        return;
    }

    private onWorkerError = (error: ErrorEvent) => {
        // Stop stream first
        if (this.source)
            this.source.disconnect();
        if (this.encoderWorklet)
            this.encoderWorklet.disconnect();
        if (this.vadWorklet)
            this.vadWorklet.disconnect();

        errorLog?.log(`${LogScope}.onWorkerError: unhandled error:`, error);
    };
}
