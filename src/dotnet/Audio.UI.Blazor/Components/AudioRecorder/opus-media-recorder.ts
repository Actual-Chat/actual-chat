/* eslint-disable @typescript-eslint/ban-types */
import { completeRpc, RpcResultMessage, rpc } from 'rpc';
import { audioContextSource } from 'audio-context-source';

import { ProcessorOptions } from './worklets/opus-encoder-worklet-processor';
import { EncoderWorkletMessage } from './worklets/opus-encoder-worklet-message';
import { VadMessage } from './workers/audio-vad-worker-message';
import { VadWorkletMessage } from './worklets/audio-vad-worklet-message';
import {
    CreateEncoderMessage,
    EndMessage,
    InitEncoderMessage,
    StartMessage,
} from './workers/opus-encoder-worker-message';
import { AudioContextRef } from 'audio-context-ref';
import { Log, LogLevel, LogScope } from 'logging';

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

const LogScope: LogScope = 'OpusMediaRecorder';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export class OpusMediaRecorder {
    public static origin: string = new URL('opus-media-recorder.ts', import.meta.url).origin;
    private readonly worker: Worker;
    private readonly vadWorker: Worker;
    private readonly channelCount: number = 1;
    private readonly whenLoaded: Promise<void>;

    private contextRef: AudioContextRef | null = null;
    private encoderWorklet: AudioWorkletNode | null = null;
    private vadWorklet: AudioWorkletNode | null = null;

    public source?: MediaStreamAudioSourceNode = null;
    public stream?: MediaStream;

    // TODO: clearer states
    public state: RecordingState = 'inactive';

    constructor() {
        this.worker = new Worker('/dist/opusEncoderWorker.js');
        this.worker.onmessage = this.onWorkerMessage;
        this.worker.onerror = this.onWorkerError;

        this.vadWorker = new Worker('/dist/vadWorker.js');

        this.whenLoaded = this.load();
    }

    public async start(sessionId: string, chatId: string): Promise<void> {
        warnLog?.assert(sessionId != '', `start: sessionId is unspecified`);
        warnLog?.assert(chatId != '', `start: chatId is unspecified`);
        warnLog?.assert(this.contextRef != null, `start: chatId is unspecified`);

        this.contextRef = await audioContextSource.getRef();

        await this.init(this.contextRef.context);
        this.contextRef.whenContextChanged().then(context => {
            if (context && this.state === 'recording') {
                this.contextRef?.dispose();
                this.contextRef = null;
                void this.start(sessionId, chatId); // This call is recursive!
            }
        });

        if (this.source)
            this.source.disconnect();
        this.stream = await OpusMediaRecorder.getMicrophoneStream();
        this.source = this.contextRef.context.createMediaStreamSource(this.stream);
        this.state = 'recording';

        await rpc((rpcResult) => {
            const initMessage: StartMessage = {
                type: 'start',
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
        this.contextRef?.dispose();
        this.contextRef = null;
        this.state = 'inactive';
    }

    public async dispose(): Promise<void> {
        debugLog?.log( `dispose()`);
        if (this.state !== 'inactive')
            await this.stop();
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
            this.worker.postMessage(msg);

            // it's OK to not wait for vadWorker create
            const msgVad: VadMessage = { type: 'create', };
            this.vadWorker.postMessage(msgVad);
        });
    }

    private async init(context: AudioContext): Promise<void> {
        if (context['initialized'])
            return;

        await this.whenLoaded;

        const encoderWorkerChannel = new MessageChannel();
        const vadWorkerChannel = new MessageChannel();

        // Workers init
        await rpc((rpcResult) => {
            const msg: InitEncoderMessage = {
                type: 'init',
                rpcResultId: rpcResult.id,
            };

            const crossWorkerChannel = new MessageChannel();
            this.worker.postMessage(
                msg,
                [encoderWorkerChannel.port1, crossWorkerChannel.port1]);

            // it's OK to not wait for vadWorker init
            const msgVad: VadMessage = { type: 'init', };
            this.vadWorker.postMessage(msgVad, [vadWorkerChannel.port1, crossWorkerChannel.port2]);
        });

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
            context,
            'opus-encoder-worklet-processor',
            encoderWorkletOptions);
        const initPortMessage: EncoderWorkletMessage = { type: 'init' };
        this.encoderWorklet.port.postMessage(initPortMessage, [encoderWorkerChannel.port2]);

        // VAD worklet init
        const vadWorkletOptions: AudioWorkletNodeOptions = {
            numberOfInputs: 1,
            numberOfOutputs: 1,
            channelCount: 1,
            channelInterpretation: 'speakers',
            channelCountMode: 'explicit',
        };
        this.vadWorklet = new AudioWorkletNode(context, 'audio-vad-worklet-processor', vadWorkletOptions);
        const vadInitPortMessage: VadWorkletMessage = { type: 'init' };
        this.vadWorklet.port.postMessage(vadInitPortMessage, [vadWorkerChannel.port2]);

        context['initialized'] = true;
    }

    private static async getMicrophoneStream(): Promise<MediaStream> {
        /**
         * [Chromium]{@link https://github.com/chromium/chromium/blob/main/third_party/blink/renderer/modules/mediastream/media_constraints_impl.cc#L98-L116}
         * [Chromium]{@link https://github.com/chromium/chromium/blob/main/third_party/blink/renderer/platform/mediastream/media_constraints.cc#L358-L372}
         */
        try {
            debugLog?.log('-> getMicrophoneStream');
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
                throw new Error('UnknownError, media track not found.');
            }

            debugLog?.log('<- getMicrophoneStream. mediaStream.active =', mediaStream.active);
            return mediaStream;
        }
        catch (e) {
            errorLog?.log('Error getting microphone stream', e);
            throw e;
        }
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

        errorLog?.log(`onWorkerError: unhandled error:`, error);
    };
}
