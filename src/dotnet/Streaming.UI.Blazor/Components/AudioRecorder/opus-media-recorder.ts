/* eslint-disable @typescript-eslint/ban-types */
import { AudioContextRef, AudioContextRefOptions } from '../../Services/audio-context-ref';
import { recordingAudioContextSource } from '../../Services/audio-context-source';
import { AudioVadWorker } from './workers/audio-vad-worker-contract';
import { AudioVadWorklet } from './worklets/audio-vad-worklet-contract';
import { Disposable } from 'disposable';
import { rpcClient, rpcClientServer, RpcNoWait, rpcNoWait } from 'rpc';
import { ProcessorOptions } from './worklets/opus-encoder-worklet-processor';
import {
    catchErrors,
    debounce,
    delayAsync,
    delayAsyncWith,
    PromiseSource,
    retry
} from 'promises';
import { OpusEncoderWorker } from './workers/opus-encoder-worker-contract';
import { OpusEncoderWorklet } from './worklets/opus-encoder-worklet-contract';
import { Versioning } from 'versioning';
import { Log } from 'logging';
import { RecorderStateChanged, RecorderStateEventHandler } from "./opus-media-recorder-contracts";
import * as signalR from "@microsoft/signalr";
import { AudioInitializer } from "../../Services/audio-initializer";
import { BrowserInit } from "../../../UI.Blazor/Services/BrowserInit/browser-init";
import { AudioDiagnosticsState } from "./audio-recorder";
import { BrowserInfo } from "../../../UI.Blazor/Services/BrowserInfo/browser-info";
import { SAMPLE_RATE } from './constants';

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

const { logScope, infoLog, debugLog, warnLog, errorLog } = Log.get('OpusMediaRecorder');
const RecordingFailedInterval = 500;

export class OpusMediaRecorder implements RecorderStateEventHandler {
    public static readonly audioPowerChanged$: signalR.Subject<number> = new signalR.Subject<number>();

    private whenInitialized: PromiseSource<void>;
    private initStarted = false;

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
    private sessionToken: string | null;
    private encoderWorkerSessionToken: string | null;
    private pauseContextRef: () => void | null = null;

    public origin: string = new URL('opus-media-recorder.ts', import.meta.url).origin;
    public source?: MediaStreamAudioSourceNode = null;
    public stream?: MediaStream;

    public static async stopStreamTracks(stream?: MediaStream): Promise<void> {
        if (!stream)
            return;

        infoLog?.log('-> stopStreamTracks()');
        [...stream.getTracks()].forEach(track => {
            try {
                track.stop();
                stream.removeTrack(track);
            }
            catch (e) {
                warnLog?.log('stopStreamTracks(): track.stop() error:', e);
            }
        });

        // better integration with native mobile audio pipeline
        if ('audioSession' in navigator) {
            navigator.audioSession['type'] = 'playback'; // 'play-and-record'
        }
        infoLog?.log('<- stopStreamTracks()');
    }

    public static async getMicrophoneStream(): Promise<MediaStream> {
        /**
         * [Chromium]{@link https://github.com/chromium/chromium/blob/main/third_party/blink/renderer/modules/mediastream/media_constraints_impl.cc#L98-L116}
         * [Chromium]{@link https://github.com/chromium/chromium/blob/main/third_party/blink/renderer/platform/mediastream/media_constraints.cc#L358-L372}
         */
        let stream: MediaStream = null;
        try {
            infoLog?.log('-> getMicrophoneStream');
            function getConstraints(): MediaStreamConstraints & any {
                return {
                    audio: {
                        channelCount: 1,
                        sampleRate: SAMPLE_RATE,
                        sampleSize: 32,
                        echoCancellation: true,
                        autoGainControl: true,
                        noiseSuppression: true,
                        latency: 20,
                        advanced: [
                            // The constraint sets going earlier have higher priority!
                            // 1. Echo cancellation
                            { googExperimentalEchoCancellation: { exact: true } },
                            { googEchoCancellation2: { exact: true } },
                            { googEchoCancellation: { exact: true } },
                            { echoCancellation: { exact: true } },
                            // { echoCancellationType: { exact: "browser" } },
                            // 2. Auto gain control
                            { googExperimentalAutoGainControl: { exact: true } },
                            { googAutoGainControl: { exact: true } },
                            { autoGainControl: { exact: true } },
                            // 3. Noise suppression
                            { googExperimentalNoiseSuppression: { exact: true } },
                            { googNoiseSuppression2: { exact: true } },
                            { googNoiseSuppression: { exact: true } },
                            { noiseSuppression: { exact: true } },
                            // 4. Misc.
                            { googTypingNoiseDetection: { exact: true } },
                            { googHighpassFilter: { exact: true } },
                            { googAudioMirroring: { exact: false } },
                            { latency: { exact: 20 } },
                        ],
                    },
                    video: false,
                };
            }

            // better integration with native mobile audio pipeline
            if ('audioSession' in navigator) {
                navigator.audioSession['type'] = 'play-and-record'; // 'playback'
            }
            stream = await navigator.mediaDevices.getUserMedia(getConstraints());
            const tracks = stream.getAudioTracks();
            const audioTrack = tracks[0];
            if (!audioTrack) {
                // noinspection ExceptionCaughtLocallyJS
                throw new Error('UnknownError, media track not found.');
            }

            infoLog?.log(
                '<- getMicrophoneStream(), active:', stream.active,
                ', constraints:', audioTrack.getConstraints(),
                ', settings:', audioTrack.getSettings());
            return stream;
        }
        catch (e) {
            await OpusMediaRecorder.stopStreamTracks(stream);
            errorLog?.log('Error getting microphone stream', e);
            throw e;
        }
    }

    constructor() {
        this.whenInitialized = new PromiseSource<void>();
    }

    public async init(baseUri: string, canUseNNVad: boolean): Promise<void> {
        debugLog?.log(`-> init()`, baseUri, canUseNNVad);
        this.initStarted = true;
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
        const hubUrl = new URL('/api/hub/streams', this.origin).toString();

        await this.encoderWorker.create(
            Versioning.artifactVersions,
            hubUrl,
            { type: 'rpc-timeout', timeoutMs: 5_000 });

        debugLog?.log(`init(): encoderWorker created`);

        await this.vadWorker.create(
            Versioning.artifactVersions,
            canUseNNVad,
            { type: 'rpc-timeout', timeoutMs: 5_000 });

        debugLog?.log(`init(): vadWorker created`);

        this.whenInitialized.resolve(undefined);
        debugLog?.log(`<- init()`);
    }

    public subscribeToStateChanges(onStateChanged: RecorderStateChanged): void {
        this.onStateChanged = onStateChanged;
        // set current state
        if (onStateChanged)
            void onStateChanged(this.isRecording, this.isConnected, this.isVoiceActive);
    }

    public async start(chatId: string, repliedChatEntryId: string): Promise<void> {
        const onStateChanged = this.onStateChanged;
        // set current state
        if (onStateChanged)
            void onStateChanged(this.isRecording, this.isConnected, this.isVoiceActive);

        debugLog?.log('-> start(): #', chatId);
        if (!chatId)
            throw new Error('start: chatId is unspecified.');

        debugLog?.log(`start(): awaiting whenInitialized`);
        await this.ensureInitialized();
        debugLog?.log(`start(): whenInitialized completed`);

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
            debugLog?.log(`start.detach(): microphone stream has been closed`);

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
            this.stream = null;
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
                warnLog?.log('start.attach(): getMicrophoneStream() failed:', e);
            }
            debugLog?.log(`<- start.attach()`);
        }

        const options: AudioContextRefOptions = {
            attach: attach,
            detach: _ => retry(2, () => detach()),
        }
        const contextRef = recordingAudioContextSource.getRef('recording', options);
        this.pauseContextRef = contextRef.use();
        try {
            debugLog?.log(`start(): awaiting whenFirstTimeReady...`);
            await contextRef.whenFirstTimeReady();
            this.contextRef = contextRef;

            debugLog?.log(`start(): awaiting encoder worker start and vad worker reset ...`);
            await Promise.all([
                this.encoderWorker.start(chatId, repliedChatEntryId),
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

    public async setSessionToken(sessionToken: string): Promise<void> {
        this.sessionToken = sessionToken;

        // We don't want to wait here - this method can complete immediately,
        // all we need
        this.whenInitialized.then(() => {
            if (this.encoderWorkerSessionToken === this.sessionToken)
                return; // Concurrent call to this method already applied the change

            this.encoderWorkerSessionToken = sessionToken;
            void this.encoderWorker.setSessionToken(this.sessionToken);
        });
    }

    public async stop(): Promise<void> {
        debugLog?.log(`-> stop()`);
        try {
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

    public async terminate(): Promise<void> {
        await this.encoderWorker?.stop();
        await this.vadWorker?.reset();
        this.pauseContextRef?.();
        this.pauseContextRef = null;
        this.encoderWorkerInstance.terminate();
        this.vadWorkerInstance.terminate();
        this.whenInitialized = new PromiseSource<void>();
        AudioInitializer.isRecorderInitialized = false;
    }

    public async reconnect(): Promise<void> {
        await this.encoderWorker?.reconnect(rpcNoWait);
    }

    public async disconnect(): Promise<void> {
        await this.encoderWorker?.disconnect(rpcNoWait);
    }

    public async conversationSignal(): Promise<void> {
        await this.vadWorker?.conversationSignal(rpcNoWait);
    }

    public async runDiagnostics(diagnosticsState: AudioDiagnosticsState): Promise<AudioDiagnosticsState> {
        diagnosticsState.isRecorderInitialized = this.whenInitialized && this.whenInitialized.isCompleted();
        diagnosticsState.hasMicrophoneStream = this.stream != null;
        warnLog?.log('runDiagnostics: ', diagnosticsState);

        const timeout = 500;
        diagnosticsState = (await Promise.race([this.vadWorker?.runDiagnostics(diagnosticsState), delayAsyncWith(timeout, diagnosticsState)])) ?? diagnosticsState;
        diagnosticsState = (await Promise.race([this.encoderWorker?.runDiagnostics(diagnosticsState), delayAsyncWith(timeout, diagnosticsState)])) ?? diagnosticsState;
        diagnosticsState = (await Promise.race([this.vadWorklet?.runDiagnostics(diagnosticsState), delayAsyncWith(timeout, diagnosticsState)])) ?? diagnosticsState;
        diagnosticsState = (await Promise.race([this.encoderWorklet?.runDiagnostics(diagnosticsState), delayAsyncWith(timeout, diagnosticsState)])) ?? diagnosticsState;

        if (diagnosticsState.lastFrameProcessedAt == 0 || diagnosticsState.lastEncoderWorkletFrameProcessedAt) {
            await recordingAudioContextSource.context.suspend();
            if (BrowserInfo.appKind === 'MauiApp')
                await recordingAudioContextSource.context.resume(); // we don't need user interaction to resume
        }

        return diagnosticsState;
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

    private async ensureInitialized(): Promise<void> {
        if (this.initStarted) {
            if (this.whenInitialized.isCompleted())
                return;

            await Promise.race([this.whenInitialized, delayAsync(5000)]);
            if (this.whenInitialized.isCompleted())
                return;
        }
        // retry init again
        const origin = window.location.origin;
        const isMaui = origin.includes('0.0.0.0');
        let baseUri = origin.replace(/\/?$/, '/');
        if (isMaui) {
            await BrowserInit.whenInitialized;
            baseUri = BrowserInit.baseUri;
        }
        await this.init(baseUri, true);
    }

    private recordingFailedDebounced = debounce(() => this.recordingFailed(), RecordingFailedInterval);
    private async recordingFailed(): Promise<void> {
        await this.onRecordingStateChanged(false);
    }

    private async stopMicrophoneStream(): Promise<void> {
        infoLog?.log('stopMicrophoneStream()');
        const stream = this.stream;
        try {
            if (this.source)
                this.source.disconnect();
            this.source = null;
            this.stream = null;
        }
        finally {
            await OpusMediaRecorder.stopStreamTracks(stream);
        }
    }
}

// Init

export const opusMediaRecorder = new OpusMediaRecorder();
globalThis['opusMediaRecorder'] = opusMediaRecorder;
