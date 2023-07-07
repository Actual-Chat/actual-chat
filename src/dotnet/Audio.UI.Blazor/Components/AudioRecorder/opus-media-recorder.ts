/* eslint-disable @typescript-eslint/ban-types */
import { AudioContextRef, AudioContextRefOptions } from '../../Services/audio-context-ref';
import { audioContextSource } from '../../Services/audio-context-source';
import { AudioVadWorker } from './workers/audio-vad-worker-contract';
import { AudioVadWorklet } from './worklets/audio-vad-worklet-contract';
import { Disposable } from 'disposable';
import { rpcClient, rpcClientServer, RpcNoWait, rpcNoWait } from 'rpc';
import { ProcessorOptions } from './worklets/opus-encoder-worklet-processor';
import { catchErrors, debounce, PromiseSource, ResolvedPromise, retry } from 'promises';
import { OpusEncoderWorker } from './workers/opus-encoder-worker-contract';
import { OpusEncoderWorklet } from './worklets/opus-encoder-worklet-contract';
import { Versioning } from 'versioning';
import { Log } from 'logging';
import { RecorderStateChanged, RecorderStateEventHandler } from "./opus-media-recorder-contracts";
import * as signalR from "@microsoft/signalr";

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
const RecordingFailedInterval = 500;

export class OpusMediaRecorder implements RecorderStateEventHandler {
    public static readonly audioPowerChanged$: signalR.Subject<number> = new signalR.Subject<number>();

    private readonly whenInitialized: PromiseSource<void>;

    private encoderWorkerInstance: Worker = null;
    private encoderWorker: OpusEncoderWorker & Disposable = null;
    private vadWorkerInstance: Worker = null;
    private vadWorker: AudioVadWorker & Disposable = null;
    private encoderWorkletInstance: AudioWorkletNode = null;
    private encoderWorklet: OpusEncoderWorklet & Disposable = null;
    private vadWorkletInstance: AudioWorkletNode = null;
    private vadWorklet: AudioVadWorklet & Disposable = null;
    private contextRef: AudioContextRef = null;
    private onStateChanged?: RecorderStateChanged;
    private isRecording: boolean = false;
    private isConnected: boolean = false;
    private isVoiceActive: boolean = false;

    public origin: string = new URL('opus-media-recorder.ts', import.meta.url).origin;
    public source?: MediaStreamAudioSourceNode = null;
    public stream?: MediaStream;

    constructor() {
        this.whenInitialized = new PromiseSource<void>();
    }

    public async init(baseUri: string, canUseNNVad: boolean): Promise<void> {
        debugLog?.log(`-> init()`);
        if (this.whenInitialized.isCompleted())
            return;

        debugLog?.log(`init(): create encoder worker`);
        if (!this.encoderWorker) {
            const encoderWorkerPath = Versioning.mapPath('/dist/opusEncoderWorker.js');
            this.encoderWorkerInstance = new Worker(encoderWorkerPath);
            this.encoderWorker = rpcClientServer<OpusEncoderWorker>(`${logScope}.encoderWorker`, this.encoderWorkerInstance, this);
        }

        debugLog?.log(`init(): create vad worker`);
        if (!this.vadWorker) {
            const vadWorkerPath = Versioning.mapPath('/dist/vadWorker.js');
            this.vadWorkerInstance = new Worker(vadWorkerPath);
            this.vadWorker = rpcClientServer<AudioVadWorker>(`${logScope}.vadWorker`, this.vadWorkerInstance, this);
        }

        if (this.origin.includes('0.0.0.0')) {
            // Use server address if the app is MAUI
            this.origin = baseUri;
        }
        debugLog?.log(`init(): call create on workers`);
        const audioHubUrl = new URL('/api/hub/audio', this.origin).toString();
        await Promise.all([
            this.encoderWorker.create(
                Versioning.artifactVersions,
                audioHubUrl,
               { type: 'rpc-timeout', timeoutMs: 20_000 }),
            this.vadWorker.create(
                Versioning.artifactVersions,
                canUseNNVad,
                { type: 'rpc-timeout', timeoutMs: 20_000 }),
        ]);
        this.whenInitialized.resolve(undefined);
        debugLog?.log(`<- init()`);
    }

    public async start(recorderId: string, chatId: string, repliedChatEntryId: string, onStateChanged: RecorderStateChanged): Promise<void> {
        this.onStateChanged = onStateChanged;
        debugLog?.log('-> start(): #', chatId);
        if (!recorderId || !chatId)
            throw new Error('start: recorderId or chatId is unspecified.');

        await this.whenInitialized;
        debugLog?.log(`start(): awaited whenInitialized`);

        await this.stop();
        debugLog?.log(`start(): after stop() call`);

        const detach = async () => {
            await catchErrors(
                () => this.encoderWorkletInstance?.disconnect(),
                e => warnLog?.log('start.detach encoderWorkletInstance.disconnect error:', e));
            this.encoderWorkletInstance = null;
            await catchErrors(
                () => {
                    if (this.encoderWorklet) {
                        void this.encoderWorklet.stop(rpcNoWait);
                        this.encoderWorklet.dispose();
                    }
                },
                e => warnLog?.log('start.detach encoderWorklet.dispose error:', e));
            this.encoderWorklet = null;

            await catchErrors(
                () => this.vadWorkletInstance?.disconnect(),
                e => warnLog?.log('start.detach vadWorkletInstance.disconnect error:', e));
            this.vadWorkletInstance = null;
            await catchErrors(
                () => {
                    if (this.vadWorklet) {
                        void this.vadWorklet.stop(rpcNoWait);
                        this.vadWorklet.dispose();
                    }
                },
                e => warnLog?.log('start.detach vadWorklet.dispose error:', e));
            this.vadWorklet = null;

            await this.stopMicrophoneStream();

            await catchErrors(
                () => this.encoderWorker?.stop(),
                e => warnLog?.log('start.detach encoderWorker.stop error:', e));
            await catchErrors(
                () => this.vadWorker?.reset(),
                e => warnLog?.log('start.detach vadWorker.reset error:', e));
            await catchErrors(
                () => this.source?.disconnect(),
                e => warnLog?.log('start.detach source.disconnect error:', e));
            this.source = null;
        }

        const attach = async (context: AudioContext) => {
            debugLog?.log(`-> start.attach()`);

            if (!this.encoderWorkletInstance
                || !this.vadWorkletInstance
                || this.encoderWorkletInstance.context !== context
                || this.vadWorkletInstance.context !== context) {

                if (this.encoderWorkletInstance) {
                    void this.vadWorklet?.stop(rpcNoWait);
                    void this.encoderWorklet?.stop(rpcNoWait);
                    await detach();
                }

                const encoderWorkerToWorkletChannel = new MessageChannel();
                const encoderWorkerToVadWorkerChannel = new MessageChannel();
                const t1 = this.encoderWorker.init(
                    encoderWorkerToWorkletChannel.port1,
                    encoderWorkerToVadWorkerChannel.port1);

                debugLog?.log(`start.attach(): encoder worklet init...`);
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
                this.encoderWorklet = rpcClientServer<OpusEncoderWorklet>(
                    `${logScope}.encoderWorklet`,
                    this.encoderWorkletInstance.port,
                    this);
                await this.encoderWorklet.init(encoderWorkerToWorkletChannel.port2);
                debugLog?.log(`start.attach(): encoder worklet init completed`);

                const vadWorkerChannel = new MessageChannel();
                const t2 = this.vadWorker.init(vadWorkerChannel.port1, encoderWorkerToVadWorkerChannel.port2);

                debugLog?.log(`start.attach(): vad worklet init...`);
                // VAD worklet init
                const vadWorkletOptions: AudioWorkletNodeOptions = {
                    numberOfInputs: 1,
                    numberOfOutputs: 1,
                    channelCount: 1,
                    channelInterpretation: 'speakers',
                    channelCountMode: 'explicit',
                };
                this.vadWorkletInstance = new AudioWorkletNode(
                    context,
                    'audio-vad-worklet-processor',
                    vadWorkletOptions);
                this.vadWorklet = rpcClient<AudioVadWorklet>(`${logScope}.vadWorklet`, this.vadWorkletInstance.port);
                void this.vadWorklet.init(vadWorkerChannel.port2, rpcNoWait);
                debugLog?.log(`start.attach(): vad worklet init completed`);

                await Promise.all([t1, t2]);
            }

            // stop active microphone stream if exists
            await this.stopMicrophoneStream();
            debugLog?.log(`start.attach(): getting microphone stream`);
            try {
                const stream = await OpusMediaRecorder.getMicrophoneStream();
                // multiple microphone stream can be acquired with multiple attach() calls
                if (!this.stream) {
                    this.stream = stream;
                    this.source = context.createMediaStreamSource(this.stream);
                    this.source.connect(this.vadWorkletInstance);
                    this.source.connect(this.encoderWorkletInstance);
                }
                else {
                    await OpusMediaRecorder.stopStreamTracks(stream);
                }
                debugLog?.log(`start.attach(): microphone stream has been connected to the pipeline`);
            }
            catch (e) {
                await this.stopMicrophoneStream();
                warnLog?.log('start.attach getMicrophoneStream() error:', e);
            }
            debugLog?.log(`<- start.attach()`);
        }

        const options: AudioContextRefOptions = {
            attach: attach,
            detach: _ => retry(2, () => detach()),
        }
        const contextRef = await audioContextSource.getRef('recording', options);
        try {
            debugLog?.log(`start(): awaiting whenFirstTimeReady...`);
            await contextRef.whenFirstTimeReady();
            this.contextRef = contextRef;

            debugLog?.log(`start(): awaiting encoder worker start and vad worker reset ...`);
            await Promise.all([
                this.encoderWorker.start(recorderId, chatId, repliedChatEntryId),
                this.vadWorker.reset(),
            ]);
        }
        catch (e) {
            await this.stopMicrophoneStream();
            void contextRef.disposeAsync();
            throw e;
        }
        debugLog?.log('<- start()');
    }

    public async stop(): Promise<void> {
        debugLog?.log(`-> stop()`);
        try {
            await this.whenInitialized;
            await this.stopMicrophoneStream();
            void this.vadWorklet?.stop(rpcNoWait);
            void this.encoderWorklet?.stop(rpcNoWait);
            if (!this.contextRef)
                return;

            debugLog?.log('stop: disposing audioContextRef')
            await this.contextRef.disposeAsync();
            this.contextRef = null;
        }
        finally {
            debugLog?.log(`<- stop()`);
        }
    }


    // recorder state event handlers

    public onConnectionStateChanged(isConnected: boolean, _noWait?: RpcNoWait): Promise<void> {
        if (this.isConnected === isConnected)
            return Promise.resolve(undefined);

        this.isConnected = isConnected;

        const onStateChanged = this.onStateChanged;
        if (onStateChanged)
            void onStateChanged(this.isRecording, this.isConnected, this.isVoiceActive);

        return Promise.resolve(undefined);
    }

    public onRecordingStateChanged(isRecording: boolean, _noWait?: RpcNoWait): Promise<void> {
        if (this.isRecording === isRecording)
            return Promise.resolve(undefined);

        this.isRecording = isRecording;

        const onStateChanged = this.onStateChanged;
        if (onStateChanged)
            void onStateChanged(this.isRecording, this.isConnected, this.isVoiceActive);

        return Promise.resolve(undefined);
    }

    public onVoiceStateChanged(isVoiceActive: boolean, _noWait?: RpcNoWait): Promise<void> {
        if (this.isVoiceActive === isVoiceActive)
            return Promise.resolve(undefined);

        this.isVoiceActive = isVoiceActive;

        const onStateChanged = this.onStateChanged;
        if (onStateChanged)
            void onStateChanged(this.isRecording, this.isConnected, this.isVoiceActive);

        return Promise.resolve(undefined);
    }

    public onAudioPowerChange(power: number, _noWait?: RpcNoWait): Promise<void> {
        OpusMediaRecorder.audioPowerChanged$.next(power);

        return Promise.resolve(undefined);
    }

    public recordingInProgress(noWait?: RpcNoWait): Promise<void> {
        if (!this.isRecording) {
            void this.onRecordingStateChanged(true);
        }
        this.recordingFailedDebounced();
        return Promise.resolve(undefined);
    }

    // Private methods

    private recordingFailedDebounced = debounce(() => this.recordingFailed(), RecordingFailedInterval);
    private async recordingFailed(): Promise<void> {
        await this.onRecordingStateChanged(false);
    }

    private async stopMicrophoneStream(): Promise<void> {
        warnLog?.log('stopMicrophoneStream()');
        if (this.source)
            this.source.disconnect();
        this.source = null;

        await OpusMediaRecorder.stopStreamTracks(this.stream);
        this.stream = null;
    }

    private static async stopStreamTracks(stream?: MediaStream): Promise<void> {
        if (stream) {
            const tracks = new Array<MediaStreamTrack>()
            tracks.push(...stream.getAudioTracks());
            tracks.push(...stream.getVideoTracks());
            for (let track of tracks) {
                await catchErrors(
                    () => track.stop(),
                    e => warnLog?.log('start.detach track.stop error:', e));
                await catchErrors(
                    () => stream.removeTrack(track),
                    e => warnLog?.log('start.detach stream.removeTrack error:', e));
            }
        }
    }

    private static async getMicrophoneStream(): Promise<MediaStream> {
        /**
         * [Chromium]{@link https://github.com/chromium/chromium/blob/main/third_party/blink/renderer/modules/mediastream/media_constraints_impl.cc#L98-L116}
         * [Chromium]{@link https://github.com/chromium/chromium/blob/main/third_party/blink/renderer/platform/mediastream/media_constraints.cc#L358-L372}
         */
        let mediaStream: MediaStream = null;
        try {
            warnLog?.log('-> getMicrophoneStream');
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
            mediaStream = await navigator.mediaDevices.getUserMedia(constraints as MediaStreamConstraints);
            const tracks = mediaStream.getAudioTracks();
            if (!tracks[0]) {
                // noinspection ExceptionCaughtLocallyJS
                throw new Error('UnknownError, media track not found.');
            }

            warnLog?.log('<- getMicrophoneStream. mediaStream.active =', mediaStream.active);
            return mediaStream;
        }
        catch (e) {
            await OpusMediaRecorder.stopStreamTracks(mediaStream);
            warnLog?.log('Error getting microphone stream', e);
            throw e;
        }
    }
}

// Init

export const opusMediaRecorder = new OpusMediaRecorder();
globalThis['opusMediaRecorder'] = opusMediaRecorder;
