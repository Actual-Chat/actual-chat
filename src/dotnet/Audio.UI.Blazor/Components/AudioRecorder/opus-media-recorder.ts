/* eslint-disable @typescript-eslint/ban-types */
import { audioContextSource } from 'audio-context-source';
import { rpcClient } from 'rpc';
import { Log, LogLevel, LogScope } from 'logging';
import { PromiseSource } from 'promises';

import { ProcessorOptions } from './worklets/opus-encoder-worklet-processor';
import { AudioContextRef } from 'audio-context-ref';
import { OpusEncoderWorker } from './workers/opus-encoder-worker-contract';
import { AudioVadWorker } from './workers/audio-vad-worker-contract';
import { Disposable } from '../../../../nodejs/src/disposable';
import { OpusEncoderWorklet } from './worklets/opus-encoder-worklet-contract';
import { AudioVadWorklet } from './worklets/audio-vad-worklet-contract';
import { Versioning } from 'versioning';


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
    private readonly whenLoaded: PromiseSource<void>;

    private encoderWorkerInstance: Worker = null;
    private encoderWorker: OpusEncoderWorker & Disposable = null;
    private vadWorkerInstance: Worker = null;
    private vadWorker: AudioVadWorker & Disposable = null;
    private encoderWorkletInstance: AudioWorkletNode = null;
    private encoderWorklet: OpusEncoderWorklet & Disposable = null;
    private vadWorkletInstance: AudioWorkletNode = null;
    private vadWorklet: AudioVadWorklet & Disposable = null;
    private contextRef: AudioContextRef = null;

    public origin: string = new URL('opus-media-recorder.ts', import.meta.url).origin;
    public source?: MediaStreamAudioSourceNode = null;
    public stream?: MediaStream;

    // TODO: clearer states
    public state: RecordingState = 'inactive';

    constructor() {
        this.whenLoaded = new PromiseSource<void>();
    }

    public async load(baseUri: string): Promise<void> {
        const encoderWorkerPath = Versioning.mapPath('/dist/opusEncoderWorker.js');
        this.encoderWorkerInstance = new Worker(encoderWorkerPath);
        this.encoderWorker = rpcClient<OpusEncoderWorker>(`${LogScope}.encoderWorker`, this.encoderWorkerInstance)

        const vadWorkerPath = Versioning.mapPath('/dist/vadWorker.js');
        this.vadWorkerInstance = new Worker(vadWorkerPath);
        this.vadWorker = rpcClient<AudioVadWorker>(`${LogScope}.vadWorker`, this.vadWorkerInstance)

        if (this.origin.includes('0.0.0.0')) {
            // use server address if the app is MAUI
            this.origin = baseUri;
        }
        const audioHubUrl = new URL('/api/hub/audio', this.origin).toString();
        await Promise.all([
            this.encoderWorker.create(Versioning.artifactVersions, audioHubUrl),
            this.vadWorker.create(Versioning.artifactVersions),
        ]);
        this.whenLoaded.resolve(undefined);
    }

    public async start(sessionId: string, chatId: string): Promise<void> {
        warnLog?.assert(sessionId != '', `start: sessionId is unspecified`);
        warnLog?.assert(chatId != '', `start: chatId is unspecified`);

        this.contextRef = await audioContextSource.getRef();

        await this.init(this.contextRef.context);
        this.contextRef.whenContextChanged().then(context => {
            if (context && this.state === 'recording') {
                this.stop()
                    .then(() => {
                        this.contextRef?.dispose();
                        this.contextRef = null;
                        void this.start(sessionId, chatId); // This call is recursive!
                    });
            }
        });

        if (this.source)
            this.source.disconnect();
        this.stream = await OpusMediaRecorder.getMicrophoneStream();
        if (!this.contextRef) {
            // audio context has been recreated
            warnLog?.log(`start(): audio context has been recreated`);
            await this.stop();
        }
        else {
            this.source = this.contextRef.context.createMediaStreamSource(this.stream);
            this.state = 'recording';
        }

        await Promise.all([
            this.encoderWorker.start(sessionId, chatId),
            await this.vadWorker.reset(),
        ]);
        this.source.connect(this.vadWorkletInstance);
        this.source.connect(this.encoderWorkletInstance);
    }

    public async stop(): Promise<void> {
        warnLog?.assert(this.state !== 'inactive', `stop: state == 'inactive'`);

        // Stop stream first
        if (this.source)
            this.source.disconnect();
        if (this.encoderWorkletInstance)
            this.encoderWorkletInstance.disconnect();
        if (this.vadWorkletInstance)
            this.vadWorkletInstance.disconnect();

        if (this.stream) {
            this.stream.getAudioTracks().forEach(t => t.stop());
            this.stream.getVideoTracks().forEach(t => t.stop());
        }
        this.stream = null;
        this.source = null;

        await this.encoderWorker.stop();

        this.contextRef?.dispose();
        this.contextRef = null;
        this.state = 'inactive';
    }

    // Private methods

    private async init(context: AudioContext): Promise<void> {
        if (context['initialized'])
            return;

        await this.whenLoaded;

        const encoderWorkerToWorkletChannel = new MessageChannel();
        const encoderWorkerToVadWorkerChannel = new MessageChannel();
        const t1 = this.encoderWorker.init(encoderWorkerToWorkletChannel.port1, encoderWorkerToVadWorkerChannel.port1);

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
        this.encoderWorkletInstance = new AudioWorkletNode(
            context,
            'opus-encoder-worklet-processor',
            encoderWorkletOptions);
        this.encoderWorklet = rpcClient<OpusEncoderWorklet>(`${LogScope}.encoderWorklet`, this.encoderWorkletInstance.port);
        void this.encoderWorklet.init(encoderWorkerToWorkletChannel.port2);

        const vadWorkerChannel = new MessageChannel();
        const t2 = this.vadWorker.init(vadWorkerChannel.port1, encoderWorkerToVadWorkerChannel.port2);

        // VAD worklet init
        const vadWorkletOptions: AudioWorkletNodeOptions = {
            numberOfInputs: 1,
            numberOfOutputs: 1,
            channelCount: 1,
            channelInterpretation: 'speakers',
            channelCountMode: 'explicit',
        };
        this.vadWorkletInstance = new AudioWorkletNode(context, 'audio-vad-worklet-processor', vadWorkletOptions);
        this.vadWorklet = rpcClient<AudioVadWorklet>(`${LogScope}.vadWorklet`, this.vadWorkletInstance.port);
        void this.vadWorklet.init(vadWorkerChannel.port2);

        await Promise.all([t1, t2]);
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
                // noinspection ExceptionCaughtLocallyJS
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
}

// Init

export const opusMediaRecorder = new OpusMediaRecorder();
globalThis['opusMediaRecorder'] = opusMediaRecorder;
