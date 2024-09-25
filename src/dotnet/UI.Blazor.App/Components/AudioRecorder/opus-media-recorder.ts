/* eslint-disable @typescript-eslint/ban-types */
import { AUDIO_REC as AR } from '_constants';
import { Disposable } from 'disposable';
import { Versioning } from 'versioning';
import { catchErrors, debounce, delayAsync, delayAsyncWith, PromiseSource, retry } from 'promises';
import { rpcClient, rpcClientServer, RpcNoWait, rpcNoWait } from 'rpc';
import { Observable, Subject } from 'rxjs';
import { BrowserInit } from '../../../UI.Blazor/Services/BrowserInit/browser-init';
import { BrowserInfo } from '../../../UI.Blazor/Services/BrowserInfo/browser-info';
import { recordingAudioContextSource } from '../../Services/audio-context-source';
import { AudioContextRef, AudioContextRefOptions } from '../../Services/audio-context-ref';
import { AudioVadWorker } from './workers/audio-vad-worker-contract';
import { AudioVadWorklet } from './worklets/audio-vad-worklet-contract';
import { OpusEncoderWorker } from './workers/opus-encoder-worker-contract';
import { OpusEncoderWorklet } from './worklets/opus-encoder-worklet-contract';
import { ProcessorOptions } from './worklets/opus-encoder-worklet-processor';
import { AudioInitializer } from '../../Services/audio-initializer';
import { AudioDiagnosticsState } from './audio-recorder';
import { RecorderState, RecorderStateChanged, RecorderStateServer } from './opus-media-recorder-contracts';
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

const { logScope, infoLog, debugLog, warnLog, errorLog } = Log.get('OpusMediaRecorder');
const RecordingFailedInterval = 500;

export class OpusMediaRecorder implements RecorderStateServer {
    private static readonly audioPowerChangedSubject: Subject<number> = new Subject<number>();
    private static readonly recorderStateChangedSubject: Subject<RecorderState> = new Subject<RecorderState>();

    public static get audioPowerChanged$(): Observable<number> {
        return OpusMediaRecorder.audioPowerChangedSubject.asObservable();
    }

    public static get recorderStateChanged$(): Observable<RecorderState> {
        return OpusMediaRecorder.recorderStateChangedSubject.asObservable();
    }

    private state: 'inactive' | 'initializing' | 'recording' | 'stopped'  = 'inactive';
    private lastState: RecorderState = { isRecording: false, isConnected: false, isVoiceActive: false };
    private whenInitialized: PromiseSource<void>;

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
                        sampleRate: AR.SAMPLE_RATE,
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
        this.state = 'initializing';
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

        if (BrowserInfo.hostKind === 'MauiApp') {
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

        const detach = async () => {
            await catchErrors(
                () => this.encoderWorkletInstance?.disconnect(),
                e => warnLog?.log('start.detach encoderWorkletInstance.disconnect error:', e));
            this.encoderWorkletInstance = null;
            await catchErrors(
                () => {
                    if (this.encoderWorklet) {
                        void this.encoderWorklet.terminate(rpcNoWait);
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
                        void this.vadWorklet.terminate(rpcNoWait);
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
            debugLog?.log(`-> init.attach()`);

            if (!this.encoderWorkletInstance
                || !this.vadWorkletInstance
                || this.encoderWorkletInstance.context !== context
                || this.vadWorkletInstance.context !== context) {

                if (this.encoderWorkletInstance) {
                    void this.vadWorklet?.terminate(rpcNoWait);
                    void this.encoderWorklet?.terminate(rpcNoWait);
                    await detach();
                }

                const encoderWorkerToWorkletChannel = new MessageChannel();
                const encoderWorkerToVadWorkerChannel = new MessageChannel();
                const t1 = this.encoderWorker.init(
                    encoderWorkerToWorkletChannel.port1,
                    encoderWorkerToVadWorkerChannel.port1);

                debugLog?.log(`init.attach(): encoder worklet init...`);
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
                debugLog?.log(`init.attach(): encoder worklet init completed`);

                const vadWorkerChannel = new MessageChannel();
                const t2 = this.vadWorker.init(vadWorkerChannel.port1, encoderWorkerToVadWorkerChannel.port2);

                debugLog?.log(`init.attach(): vad worklet init...`);
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
                debugLog?.log(`init.attach(): vad worklet init completed`);

                await Promise.all([t1, t2]);
            }

            // Acquire new stream and terminate old one if recording
            if (this.state === 'recording')
                await this.startMicrophoneStream(context);
            else
                await this.stopMicrophoneStream();

            debugLog?.log(`<- init.attach()`);
        }

        const options: AudioContextRefOptions = {
            attach: attach,
            detach: _ => retry(2, () => detach()),
        }
        this.contextRef = recordingAudioContextSource.getRef('recording', options);
        this.state = 'stopped';
        this.whenInitialized.resolve(undefined);
        debugLog?.log(`<- init()`);
    }

    public subscribeToStateChanges(onStateChanged: RecorderStateChanged): void {
        this.onStateChanged = onStateChanged;
        this.stateChanged();
    }

    public async start(chatId: string, repliedChatEntryId: string): Promise<void> {
        this.stateChanged();

        debugLog?.log('-> start(): #', chatId);
        if (!chatId)
            throw new Error('start: chatId is unspecified.');

        debugLog?.log(`start(): awaiting whenInitialized`);
        await this.ensureInitialized();
        debugLog?.log(`start(): whenInitialized completed`);

        await this.stop();
        debugLog?.log(`start(): after stop() call`);

        this.state = 'recording';
        const contextRef = this.contextRef;
        this.pauseContextRef = contextRef.use();
        try {
            debugLog?.log(`start(): awaiting whenFirstTimeReady...`);
            await contextRef.whenFirstTimeReady();

            await this.startMicrophoneStream(contextRef.currentContext);

            debugLog?.log(`start(): awaiting encoder worker start, worklet start and vad worker reset ...`);
            await Promise.all([
                this.encoderWorker.start(chatId, repliedChatEntryId),
                this.vadWorker.reset(),
                this.encoderWorklet.start(rpcNoWait)
            ]);
        }
        catch (e) {
            this.state = 'stopped';
            await this.stopMicrophoneStream();
            this.pauseContextRef?.();
            throw e;
        }
        debugLog?.log('<- start()');
    }

    public setSessionToken(sessionToken: string): void {
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
        this.state = 'stopped';

        if (!this.stream && !this.source)
            return;

        debugLog?.log(`-> stop()`);

        await catchErrors(
            () => this.encoderWorker?.stop(),
            e => warnLog?.log('stop encoderWorker.stop error:', e));
        await catchErrors(
            () => this.vadWorker?.reset(),
            e => warnLog?.log('stop vadWorker.reset error:', e));

        try {
            await this.stopMicrophoneStream();
            await this.encoderWorker?.stop();
        }
        finally {
            this.pauseContextRef?.();
            this.pauseContextRef = null;
            debugLog?.log(`<- stop()`);
        }
    }

    public async terminate(): Promise<void> {
        await this.encoderWorker?.stop();
        await this.vadWorker?.reset();
        this.pauseContextRef?.();
        this.pauseContextRef = null;
        this.contextRef = null;
        this.encoderWorkerInstance.terminate();
        this.vadWorkerInstance.terminate();
        void this.vadWorklet?.terminate(rpcNoWait);
        void this.encoderWorklet?.terminate(rpcNoWait);
        this.whenInitialized = new PromiseSource<void>();
        AudioInitializer.isRecorderInitialized = false;
    }

    public async ensureConnected(quickReconnect: boolean): Promise<void> {
        await this.encoderWorker?.ensureConnected(quickReconnect, rpcNoWait);
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
            if (BrowserInfo.hostKind === 'MauiApp')
                await recordingAudioContextSource.context.resume(); // we don't need user interaction to resume
        }

        return diagnosticsState;
    }

    // recorder state event handlers

    public onConnectionStateChanged(isConnected: boolean, _noWait?: RpcNoWait): Promise<void> {
        if (this.isConnected === isConnected)
            return Promise.resolve(undefined);

        this.isConnected = isConnected;
        this.stateChanged();
        return Promise.resolve(undefined);
    }

    public onRecordingStateChanged(isRecording: boolean, _noWait?: RpcNoWait): Promise<void> {
        if (this.isRecording === isRecording)
            return Promise.resolve(undefined);

        this.isRecording = isRecording;
        this.stateChanged();
        return Promise.resolve(undefined);
    }

    public onVoiceStateChanged(isVoiceActive: boolean, _noWait?: RpcNoWait): Promise<void> {
        if (this.isVoiceActive === isVoiceActive)
            return Promise.resolve(undefined);

        this.isVoiceActive = isVoiceActive;
        this.stateChanged();
        return Promise.resolve(undefined);
    }

    public onAudioPowerChange(power: number, _noWait?: RpcNoWait): Promise<void> {
        OpusMediaRecorder.audioPowerChangedSubject.next(power);

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
        if (this.state !== 'inactive') {
            if (this.whenInitialized.isCompleted())
                return;

            await Promise.race([this.whenInitialized, delayAsync(5000)]);
            if (this.whenInitialized.isCompleted())
                return;
        }
        // retry init again
        const origin = window.location.origin;
        let baseUri = origin.replace(/\/?$/, '/');
        if (BrowserInfo.hostKind === 'MauiApp') {
            await BrowserInit.whenInitialized;
            baseUri = BrowserInit.baseUri;
        }
        await this.init(baseUri, true);
    }

    private stateChanged(): void {
        const onStateChanged = this.onStateChanged;
        // set current state
        if (onStateChanged)
            void onStateChanged(this.isRecording, this.isConnected, this.isVoiceActive);


        const lastState = this.lastState;
        const state = { isRecording: this.isRecording, isConnected: this.isConnected, isVoiceActive: this.isVoiceActive };
        if (!state.isRecording == lastState.isRecording && state.isConnected == lastState.isConnected && state.isVoiceActive == lastState.isVoiceActive)
            return;

        this.lastState = state;
        OpusMediaRecorder.recorderStateChangedSubject.next(state);
    }

    private recordingFailedDebounced = debounce(() => this.recordingFailed(), RecordingFailedInterval);
    private async recordingFailed(): Promise<void> {
        await this.onRecordingStateChanged(false);
    }

    private async startMicrophoneStream(context: AudioContext): Promise<void> {
        if (this.stream?.active && this.source?.context === context)
            return;

        await this.stopMicrophoneStream();
        debugLog?.log(`startMicrophoneStream(): getting microphone stream`);
        try {
            this.stream = await OpusMediaRecorder.getMicrophoneStream();
            this.source = context.createMediaStreamSource(this.stream);
            this.source.connect(this.vadWorkletInstance);
            this.source.connect(this.encoderWorkletInstance);
            debugLog?.log(`startMicrophoneStream(): microphone stream has been connected to the pipeline`);
        } catch (e) {
            await this.stopMicrophoneStream();
            warnLog?.log('startMicrophoneStream(): getMicrophoneStream() failed:', e);
        }
    }

    private async stopMicrophoneStream(): Promise<void> {
        if (!this.stream && !this.source)
            return;

        infoLog?.log('stopMicrophoneStream()');
        const stream = this.stream;
        try {
            this.source?.disconnect();
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
