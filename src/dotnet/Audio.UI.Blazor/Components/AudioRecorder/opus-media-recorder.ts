/* eslint-disable @typescript-eslint/ban-types */
import { AudioContextRef, AudioContextRefOptions } from '../../Services/audio-context-ref';
import { audioContextSource } from '../../Services/audio-context-source';
import { AudioVadWorker } from './workers/audio-vad-worker-contract';
import { AudioVadWorklet } from './worklets/audio-vad-worklet-contract';
import { Disposable } from 'disposable';
import { rpcClient, rpcNoWait } from 'rpc';
import { ProcessorOptions } from './worklets/opus-encoder-worklet-processor';
import { catchErrors, PromiseSource, retry } from 'promises';
import { OpusEncoderWorker } from './workers/opus-encoder-worker-contract';
import { OpusEncoderWorklet } from './worklets/opus-encoder-worklet-contract';
import { Versioning } from 'versioning';
import { Log } from 'logging';


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

const { logScope, debugLog, warnLog, errorLog } = Log.get('OpusMediaRecorder');

class ChatRecording {
    constructor(
        public readonly sessionId: string,
        public readonly chatId: string
    ) { }
}

type RecorderState = ChatRecording | null;

export class OpusMediaRecorder {
    private readonly whenInitialized: PromiseSource<void>;

    private encoderWorkerInstance: Worker = null;
    private encoderWorker: OpusEncoderWorker & Disposable = null;
    private vadWorkerInstance: Worker = null;
    private vadWorker: AudioVadWorker & Disposable = null;
    private encoderWorkletInstance: AudioWorkletNode = null;
    private encoderWorklet: OpusEncoderWorklet & Disposable = null;
    private vadWorkletInstance: AudioWorkletNode = null;
    private vadWorklet: AudioVadWorklet & Disposable = null;
    private state: RecorderState = null;
    private contextRef: AudioContextRef = null;

    public origin: string = new URL('opus-media-recorder.ts', import.meta.url).origin;
    public source?: MediaStreamAudioSourceNode = null;
    public stream?: MediaStream;

    constructor() {
        this.whenInitialized = new PromiseSource<void>();
    }

    public async init(baseUri: string): Promise<void> {
        const encoderWorkerPath = Versioning.mapPath('/dist/opusEncoderWorker.js');
        this.encoderWorkerInstance = new Worker(encoderWorkerPath);
        this.encoderWorker = rpcClient<OpusEncoderWorker>(`${logScope}.encoderWorker`, this.encoderWorkerInstance)

        const vadWorkerPath = Versioning.mapPath('/dist/vadWorker.js');
        this.vadWorkerInstance = new Worker(vadWorkerPath);
        this.vadWorker = rpcClient<AudioVadWorker>(`${logScope}.vadWorker`, this.vadWorkerInstance)

        if (this.origin.includes('0.0.0.0')) {
            // use server address if the app is MAUI
            this.origin = baseUri;
        }
        const audioHubUrl = new URL('/api/hub/audio', this.origin).toString();
        await Promise.all([
            this.encoderWorker.create(Versioning.artifactVersions, audioHubUrl),
            this.vadWorker.create(Versioning.artifactVersions),
        ]);
        this.whenInitialized.resolve(undefined);
    }

    public async start(sessionId: string, chatId: string, repliedChatEntryId: string): Promise<void> {
        debugLog?.log('start: #', chatId, 'sessionId=', sessionId);
        if (!sessionId || !chatId)
            throw new Error('start: sessionId or chatId is unspecified.');

        await this.whenInitialized;
        await this.stop();

        const attach = async (context: AudioContext) => {
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
            this.encoderWorklet = rpcClient<OpusEncoderWorklet>(`${logScope}.encoderWorklet`, this.encoderWorkletInstance.port);
            await this.encoderWorklet.init(encoderWorkerToWorkletChannel.port2);

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
            this.vadWorklet = rpcClient<AudioVadWorklet>(`${logScope}.vadWorklet`, this.vadWorkletInstance.port);
            void this.vadWorklet.init(vadWorkerChannel.port2, rpcNoWait);

            await Promise.all([t1, t2]);

            this.stream = await OpusMediaRecorder.getMicrophoneStream();
            this.source = context.createMediaStreamSource(this.stream);
            this.source.connect(this.vadWorkletInstance);
            this.source.connect(this.encoderWorkletInstance);
        }

        const detach = async () => {
            if (this.state == null)
                return;

            await catchErrors(
                () => this.encoderWorkletInstance?.disconnect(),
                e => warnLog.log('start.detach encoderWorkletInstance.disconnect error:', e));
            this.encoderWorkletInstance = null;
            await catchErrors(
                () => this.encoderWorklet?.dispose(),
                e => warnLog.log('start.detach encoderWorklet.dispose error:', e));
            this.encoderWorklet = null;

            await catchErrors(
                () => this.vadWorkletInstance?.disconnect(),
                e => warnLog.log('start.detach vadWorkletInstance.disconnect error:', e));
            this.vadWorkletInstance = null;
            await catchErrors(
                () => this.vadWorklet?.dispose(),
                e => warnLog.log('start.detach vadWorklet.dispose error:', e));
            this.vadWorklet = null;

            if (this.stream) {
                const tracks = new Array<MediaStreamTrack>()
                tracks.push(...this.stream.getAudioTracks());
                tracks.push(...this.stream.getVideoTracks());
                for (let track of tracks) {
                    await catchErrors(
                        () => track.stop(),
                        e => warnLog.log('start.detach track.stop error:', e));
                    await catchErrors(
                        () => this.stream.removeTrack(track),
                        e => warnLog.log('start.detach stream.removeTrack error:', e));
                }
            }
            this.stream = null;

            await catchErrors(
                () => this.encoderWorker?.stop(),
                e => warnLog.log('start.detach encoderWorker.stop error:', e));
            await catchErrors(
                () => this.source?.disconnect(),
                e => warnLog.log('start.detach source.disconnect error:', e));
            this.source = null;

            this.state = null;
        }

        const options: AudioContextRefOptions = {
            attach: attach,
            detach: _ => retry(2, () => detach()),
        }
        const contextRef = await audioContextSource.getRef('recording', options);
        try {
            await contextRef.whenFirstTimeReady();
            this.contextRef = contextRef;
            this.state = new ChatRecording(sessionId, chatId);

            await Promise.all([
                this.encoderWorker.start(sessionId, chatId, repliedChatEntryId),
                await this.vadWorker.reset(),
            ]);
        }
        catch (e) {
            void contextRef.disposeAsync();
            throw e;
        }
    }

    public async stop(): Promise<void> {
        debugLog?.log(`stop`);
        await this.whenInitialized;
        if (!this.contextRef)
            return;

        debugLog?.log('stop: disposing audioContextRef')
        await this.contextRef.disposeAsync();
        this.contextRef = null;
    }

    // Private methods

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
