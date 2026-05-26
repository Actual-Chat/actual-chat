// TODO: remove eslint-disables and fix errors
/* eslint-disable @typescript-eslint/no-unused-vars,@typescript-eslint/no-unnecessary-condition,@typescript-eslint/require-await,@typescript-eslint/no-unsafe-member-access,@typescript-eslint/no-explicit-any,@typescript-eslint/no-redundant-type-constituents,@typescript-eslint/no-unsafe-argument */
import { AUDIO, AC, whenAppConstantsReady } from 'app-constants';
import { Disposable } from 'disposable';
import { Versioning } from 'versioning';
import { catchErrors, delayAsync, delayAsyncWith, PromiseSource, ResolvedPromise, retry } from 'actuallab-core';
import { rpcClient, rpcClientServer, RpcNoWait, rpcNoWait } from 'rpc';
import { BrowserInit } from '../../../UI.Blazor/Services/BrowserInit/browser-init';
import { BrowserInfo } from '../../../UI.Blazor/Services/BrowserInfo/browser-info';
import { ConnectivityUI } from '../../../UI.Blazor/Services/ConnectivityUI/connectivity-ui';
import { DebugUI } from '../../../UI.Blazor/Services/DebugUI/debug-ui';
import { Api, WorkerKind } from 'api';
import { audioContextSource, recordingAudioContextSource, AppAudioContext, AudioContextRef, AudioContextAction } from '../../Services/audio-context-source';
import { AudioContextTrait, AttachedAudioContextTrait } from '../../Services/audio-context-traits';
import { AudioVadWorker } from './workers/audio-vad-worker-contract';
import { AudioVadWorklet } from './worklets/audio-vad-worklet-contract';
import { OpusEncoderWorker } from './workers/opus-encoder-worker-contract';
import { OpusEncoderWorklet } from './worklets/opus-encoder-worklet-contract';
import { OpusEncoderProcessorOptions } from './worklets/opus-encoder-worklet-processor';
import { AudioInitializer } from '../../Services/audio-initializer';
import { AudioDiagnosticsState } from './audio-recorder';
import { RecorderStateServer } from './opus-media-recorder-contracts';
import { getLogs } from 'logging';
import { Interactive } from 'interactive';
import { DeviceInfo } from 'device-info';
import { AudioVadProcessorOptions } from './worklets/audio-vad-worklet-processor';
import { RecordingActivity } from './recording-activity';
import { AudioRecorderState } from './audio-recorder-state';
import { SharedSettings } from 'shared-settings';
import { SharedSettingsWorkerSync } from 'shared-settings-worker';

/*
┌─────────────────────────────────┐  ┌──────────────────────┐
│                                 │  │            web worker│◄────────┐
│ ┌───┐    ┌────────────┐    ┌────┼──►VAD worker            │         │
│ │MIC├─┬─►│VAD worklet ├────┘    │  └──────────┬───────────┘         │
│ └───┘ │  └────────────┘         │             │isVoiceFound         │
│       │                         │ ┌───────────▼────────────┐        │
│       │ ┌───────────────┐       │ │              web worker│        │    ┌───────┐
│       └─►Encoder worklet├───────┼─►                        ├────────┼───►│  RPC  │
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

const { logScope, infoLog, debugLog, warnLog, errorLog } = getLogs('OpusMediaRecorder');

/** Trait that manages the recording pipeline (VAD + encoder worklets) */
class RecordingPipelineTrait implements AudioContextTrait {
    public readonly name = 'recording-pipeline';
    private readonly recorder: OpusMediaRecorder;

    constructor(recorder: OpusMediaRecorder) {
        this.recorder = recorder;
    }

    public async attach(context: AppAudioContext): Promise<AttachedRecordingPipeline> {
        debugLog?.log(`-> RecordingPipelineTrait.attach()`);
        return new AttachedRecordingPipeline(this.recorder, context);
    }
}

/** Attached recording pipeline that manages VAD and encoder worklets */
class AttachedRecordingPipeline implements AttachedAudioContextTrait {
    private readonly recorder: OpusMediaRecorder;
    private readonly context: AudioContext;
    public encoderWorkletInstance: AudioWorkletNode | null = null;
    public encoderWorklet: OpusEncoderWorklet & Disposable | null = null;
    public vadWorkletInstance: AudioWorkletNode | null = null;
    public vadWorklet: AudioVadWorklet & Disposable | null = null;

    constructor(recorder: OpusMediaRecorder, context: AudioContext) {
        this.recorder = recorder;
        this.context = context;
    }

    public async initialize(): Promise<void> {
        await whenAppConstantsReady;
        const context = this.context;
        const recorder = this.recorder;

        const encoderWorkerToWorkletChannel = new MessageChannel();
        const encoderWorkerToVadWorkerChannel = new MessageChannel();
        const t1 = recorder.encoderWorker.init(
            encoderWorkerToWorkletChannel.port1,
            encoderWorkerToVadWorkerChannel.port1);

        debugLog?.log(`AttachedRecordingPipeline.initialize(): encoder worklet init...`);
        // Encoder worklet init
        const encoderWorkletOptions: AudioWorkletNodeOptions = {
            numberOfInputs: 1,
            numberOfOutputs: 1,
            channelCount: 1,
            channelInterpretation: 'speakers',
            channelCountMode: 'explicit',
            processorOptions: {
                timeSlice: 20, // hard-coded 20ms at the codec level
                sampleRate: context.sampleRate,
            } as OpusEncoderProcessorOptions,
        };
        this.encoderWorkletInstance = new AudioWorkletNode(
            context,
            'opus-encoder-worklet-processor',
            encoderWorkletOptions);
        this.encoderWorklet = rpcClientServer<OpusEncoderWorklet>(
            `${logScope}.encoderWorklet`,
            this.encoderWorkletInstance.port,
            recorder);
        await this.encoderWorklet.init(AC, encoderWorkerToWorkletChannel.port2);
        debugLog?.log(`AttachedRecordingPipeline.initialize(): encoder worklet init completed`);

        const vadWorkerChannel = new MessageChannel();
        const t2 = recorder.vadWorker.init(vadWorkerChannel.port1, encoderWorkerToVadWorkerChannel.port2);

        debugLog?.log(`AttachedRecordingPipeline.initialize(): vad worklet init...`);
        // VAD worklet init
        const vadWorkletOptions: AudioWorkletNodeOptions = {
            numberOfInputs: 1,
            numberOfOutputs: 1,
            channelCount: 1,
            channelInterpretation: 'speakers',
            channelCountMode: 'explicit',
            processorOptions: {
                sampleRate: context.sampleRate,
            } as AudioVadProcessorOptions,
        };
        this.vadWorkletInstance = new AudioWorkletNode(
            context,
            'audio-vad-worklet-processor',
            vadWorkletOptions);
        this.vadWorklet = rpcClient<AudioVadWorklet>(`${logScope}.vadWorklet`, this.vadWorkletInstance.port);
        void this.vadWorklet.init(AC, vadWorkerChannel.port2, rpcNoWait);
        debugLog?.log(`AttachedRecordingPipeline.initialize(): vad worklet init completed`);

        await Promise.all([t1, t2]);
    }

    public async onClosed(): Promise<void> {
        debugLog?.log(`AttachedRecordingPipeline.onClosed()`);

        await catchErrors(
            () => this.encoderWorkletInstance?.disconnect(),
            e => warnLog?.log('onClosed encoderWorkletInstance.disconnect error:', e));
        this.encoderWorkletInstance = null;
        await catchErrors(
            () => {
                if (this.encoderWorklet) {
                    void this.encoderWorklet.terminate(rpcNoWait);
                    this.encoderWorklet.dispose();
                }
            },
            e => warnLog?.log('onClosed encoderWorklet.dispose error:', e));
        this.encoderWorklet = null;

        await catchErrors(
            () => this.vadWorkletInstance?.disconnect(),
            e => warnLog?.log('onClosed vadWorkletInstance.disconnect error:', e));
        this.vadWorkletInstance = null;
        await catchErrors(
            () => {
                if (this.vadWorklet) {
                    void this.vadWorklet.terminate(rpcNoWait);
                    this.vadWorklet.dispose();
                }
            },
            e => warnLog?.log('onClosed vadWorklet.dispose error:', e));
        this.vadWorklet = null;

        await this.recorder.stopMicrophoneStream();
        debugLog?.log(`onClosed(): microphone stream has been closed`);

        await catchErrors(
            () => this.recorder.encoderWorker?.stop(),
            e => warnLog?.log('onClosed encoderWorker.stop error:', e));
        await catchErrors(
            () => this.recorder.vadWorker?.reset(),
            e => warnLog?.log('onClosed vadWorker.reset error:', e));
        await catchErrors(
            () => this.recorder.source?.disconnect(),
            e => warnLog?.log('onClosed source.disconnect error:', e));
        this.recorder.source = null;
        this.recorder.stream = null;
    }
}

export class OpusMediaRecorder implements RecorderStateServer {
    private state: 'inactive' | 'initializing' | 'recording' | 'stopped'  = 'inactive';
    private whenInitialized: PromiseSource<void>;

    private encoderWorkerInstance: Worker;
    public encoderWorker: OpusEncoderWorker & Disposable;
    private vadWorkerInstance: Worker;
    public vadWorker: AudioVadWorker & Disposable;

    private readonly recordingPipelineTrait: RecordingPipelineTrait;
    private recordingContextRef?: AudioContextRef;
    private playbackContextRef?: AudioContextRef;
    private recordingAction?: AudioContextAction;
    private chatId?: string;

    public origin: string = new URL(import.meta.url).origin;
    public source: MediaStreamAudioSourceNode | null = null;
    public stream: MediaStream | null = null;
    private heartbeatTimerId: ReturnType<typeof setInterval> | undefined;
    private heartbeatSuspendedUntil = 0;

    private get isRecording(): boolean {
        return !!(this.stream && this.state === 'recording');
    }

    public static async stopStreamTracks(stream: MediaStream | null): Promise<void> {
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

        // Better integration with native mobile audio pipeline
        if ('audioSession' in navigator && typeof navigator.audioSession === 'object') {
            (navigator.audioSession as any)['type'] = 'playback';
            (navigator.audioSession as any)['type'] = 'auto'; // Hack for iOS Safari
            (navigator.audioSession as any)['type'] = 'playback';
        }

        infoLog?.log('<- stopStreamTracks()');
    }

    public static async getMicrophoneStream(): Promise<MediaStream> {
        /**
         * [Chromium]{@link https://github.com/chromium/chromium/blob/main/third_party/blink/renderer/modules/mediastream/media_constraints_impl.cc#L98-L116}
         * [Chromium]{@link https://github.com/chromium/chromium/blob/main/third_party/blink/renderer/platform/mediastream/media_constraints.cc#L358-L372}
         */
        const isAndroid = !!(/Android/i.exec(navigator.userAgent));
        let stream: MediaStream | null = null;
        try {
            infoLog?.log('-> getMicrophoneStream');
            let hasDefaultMic = false;
            if (navigator.mediaDevices?.enumerateDevices) {
                const devices = await navigator.mediaDevices.enumerateDevices();
                for (const device of devices) {
                    if (device.kind === 'audioinput' && device.deviceId === 'default') {
                        hasDefaultMic = true;
                        break;
                    }
                }
            }
            // Better integration with native mobile audio pipeline - we are resetting to defaults
            if ('audioSession' in navigator) {
                (navigator.audioSession as any)['type'] = 'auto';
            }

            const constraints : MediaStreamConstraints & any = {
                audio: {
                    channelCount: 1,
                    sampleRate: DeviceInfo.isFirefox ? undefined : AUDIO.rec.sampleRate, // FF doesn't support sample rate
                    sampleSize: 32,
                    echoCancellation: true,
                    autoGainControl: !(BrowserInfo.appKind === 'Android' || isAndroid), // Android auto gain delays recording and produces zeroes instead of signal
                    noiseSuppression: true,
                    // MediaTrackConstraints.latency is in seconds, not milliseconds.
                    latency: 0.02,
                },
                video: false,
            };
            if (hasDefaultMic) {
                try {
                    constraints.audio.deviceId = { exact: 'default' };
                    stream = await navigator.mediaDevices.getUserMedia(constraints);
                }
                catch {
                    constraints.audio.deviceId = null;
                }
            }
            stream ??= await navigator.mediaDevices.getUserMedia(constraints);
            // Better integration with native mobile audio pipeline - SHOULD BE AFTER ACQUIRING THE STREAM!
            if ('audioSession' in navigator) {
                (navigator.audioSession as any)['type'] = 'play-and-record';
            }
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
        this.recordingPipelineTrait = new RecordingPipelineTrait(this);
    }

    public getSessionToken(minLifespanMs?: number): Promise<string> {
        return Api.getSessionToken(minLifespanMs);
    }

    public async init(baseUri: string, canUseNNVad: boolean): Promise<void> {
        debugLog?.log(`-> init()`, baseUri, canUseNNVad);
        this.state = 'initializing';
        if (this.whenInitialized.isCompleted)
            return;

        await whenAppConstantsReady;

        debugLog?.log(`init(): create encoder worker`);
        if (!this.encoderWorker) {
            const encoderWorkerPath = Versioning.mapPath('/dist/opusEncoderWorker.js');
            this.encoderWorkerInstance = new Worker(encoderWorkerPath, { type: 'module' });
            this.encoderWorker = rpcClientServer<OpusEncoderWorker>(`${logScope}.encoderWorker`, this.encoderWorkerInstance, this);
            Api.onDisconnectRequested(WorkerKind.Recording)
                .add(() => void this.encoderWorker?.disconnectApi(rpcNoWait));
            DebugUI.registerAudioRecorderOffsetHandler(offsetMs =>
                void this.encoderWorker?.setRecorderOffset(offsetMs, rpcNoWait));
        }

        debugLog?.log(`init(): create vad worker`);
        if (!this.vadWorker) {
            const vadWorkerPath = Versioning.mapPath('/dist/vadWorker.js');
            this.vadWorkerInstance = new Worker(vadWorkerPath, { type: 'module' });
            this.vadWorker = rpcClientServer<AudioVadWorker>(`${logScope}.vadWorker`, this.vadWorkerInstance, this);
        }

        if (BrowserInfo.hostKind === 'MauiApp') {
            // Use server address if the app is MAUI
            this.origin = baseUri;
        }
        debugLog?.log(`init(): call create on workers`);
        const apiUrl = new URL('/rpc/ws', this.origin).toString().replace(/^http/, 'ws');
        SharedSettings.update({ apiUrl });

        await this.encoderWorker.create(
            AC,
            Versioning.assetMap,
            SharedSettings.all,
            apiUrl,
            { type: 'rpc-timeout', timeoutMs: 5_000 });
        debugLog?.log(`init(): encoderWorker created`);

        SharedSettingsWorkerSync.register(this.encoderWorker);

        const updateWorkerConnectivityUI = () => {
            void this.encoderWorker?.onConnectivityUpdate(
                ConnectivityUI.isOnline,
                ConnectivityUI.isConnected,
                ConnectivityUI.isBlazorServer,
                rpcNoWait)
        }
        ConnectivityUI.isOnlineChanged.add(updateWorkerConnectivityUI);
        ConnectivityUI.isConnectedChanged.add(updateWorkerConnectivityUI);
        await ConnectivityUI.whenReady;
        void this.encoderWorker.onConnectivityUpdate(
            ConnectivityUI.isOnline, ConnectivityUI.isConnected, ConnectivityUI.isBlazorServer, rpcNoWait);

        await this.vadWorker.create(
            AC,
            Versioning.assetMap,
            SharedSettings.all,
            canUseNNVad,
            { type: 'rpc-timeout', timeoutMs: 5_000 });
        debugLog?.log(`init(): vadWorker created`);
        SharedSettingsWorkerSync.register(this.vadWorker);

        // Register the trait with the recording context source
        void recordingAudioContextSource.addTrait(this.recordingPipelineTrait);

        this.state = 'stopped';
        this.whenInitialized.resolve(undefined);
        debugLog?.log(`<- init()`);
    }

    public async start(chatId: string, repliedChatEntryId: string): Promise<void> {
        RecordingActivity.setRecording(this.isRecording);
        AudioRecorderState.setRecording(this.isRecording);

        debugLog?.log('-> start(): #', chatId);
        if (!chatId)
            throw new Error('start: chatId is unspecified.');

        debugLog?.log(`start(): awaiting whenInitialized`);
        await this.ensureInitialized();
        debugLog?.log(`start(): whenInitialized completed`);

        this.state = 'recording';
        this.chatId = chatId;

        // Create refs for both recording and playback contexts
        this.recordingContextRef = recordingAudioContextSource.createRef(this.recordingPipelineTrait);
        this.playbackContextRef = audioContextSource.createRef(); // No-op ref for playback

        // Run the recording action
        this.recordingAction = this.recordingContextRef.run(async (context) => {
            try {
                debugLog?.log(`start(): awaiting encoder worker start, worklet start and vad worker reset ...`);
                if (this.chatId === chatId && this.stream)
                    return; // Already started

                // Get the attached pipeline
                const pipeline = this.recordingContextRef?.getTrait<AttachedRecordingPipeline>(this.recordingPipelineTrait);
                if (!pipeline) {
                    throw new Error('Recording pipeline not attached');
                }

                // Initialize pipeline if not already done
                if (!pipeline.encoderWorkletInstance) {
                    await pipeline.initialize();
                }

                await Promise.all([
                    this.encoderWorker.start(chatId, repliedChatEntryId),
                    this.vadWorker.reset(),
                    pipeline.encoderWorklet?.start(rpcNoWait)
                ]);
                this.startHeartbeat();

                await this.startMicrophoneStream(context, pipeline);
                RecordingActivity.setRecording(this.isRecording);
                AudioRecorderState.setRecording(this.isRecording);
            }
            catch (e) {
                this.state = 'stopped';
                this.stopHeartbeat();
                await this.stopMicrophoneStream();
                throw e;
            }
            debugLog?.log('<- start()');
        });
    }

    public async stop(): Promise<void> {
        this.state = 'stopped';
        this.chatId = undefined;
        this.stopHeartbeat();

        debugLog?.log(`-> stop()`);

        await catchErrors(
            () => this.encoderWorker?.stop(),
            e => warnLog?.log('stop encoderWorker.stop error:', e));
        await catchErrors(
            () => this.vadWorker?.reset(),
            e => warnLog?.log('stop vadWorker.reset error:', e));

        try {
            await this.stopMicrophoneStream();
            RecordingActivity.setRecording(this.isRecording);
            AudioRecorderState.setRecording(this.isRecording);
        }
        finally {
            this.recordingAction?.dispose();
            this.recordingAction = undefined;
            this.recordingContextRef?.dispose();
            this.recordingContextRef = undefined;
            this.playbackContextRef?.dispose();
            this.playbackContextRef = undefined;
            debugLog?.log(`<- stop()`);
        }
    }

    public async terminate(): Promise<void> {
        this.stopHeartbeat();
        await this.encoderWorker?.stop();
        await this.vadWorker?.reset();
        this.recordingAction?.dispose();
        this.recordingAction = undefined;
        this.recordingContextRef?.dispose();
        this.recordingContextRef = undefined;
        this.playbackContextRef?.dispose();
        this.playbackContextRef = undefined;
        this.encoderWorkerInstance.terminate();
        this.vadWorkerInstance.terminate();
        this.whenInitialized = new PromiseSource<void>();
        AudioInitializer.isRecorderInitialized = false;
    }

    public async ensureConnected(quickReconnect: boolean): Promise<void> {
        await this.encoderWorker?.ensureConnected(quickReconnect, rpcNoWait);
    }

    public async conversationSignal(): Promise<void> {
        await this.vadWorker?.conversationSignal(rpcNoWait);
    }

    public async runDiagnostics(diagnosticsState: AudioDiagnosticsState): Promise<AudioDiagnosticsState> {
        diagnosticsState.isRecorderInitialized = this.whenInitialized?.isCompleted;
        diagnosticsState.hasMicrophoneStream = this.stream != null;
        infoLog?.log('runDiagnostics: ', diagnosticsState);

        const timeout = 500;
        const pipeline = this.recordingContextRef?.getTrait<AttachedRecordingPipeline>(this.recordingPipelineTrait);
        diagnosticsState = (await Promise.race([this.vadWorker?.runDiagnostics(diagnosticsState), delayAsyncWith(timeout, diagnosticsState)])) ?? diagnosticsState;
        diagnosticsState = (await Promise.race([this.encoderWorker?.runDiagnostics(diagnosticsState), delayAsyncWith(timeout, diagnosticsState)])) ?? diagnosticsState;
        diagnosticsState = (await Promise.race([pipeline?.vadWorklet?.runDiagnostics(diagnosticsState), delayAsyncWith(timeout, diagnosticsState)])) ?? diagnosticsState;
        diagnosticsState = (await Promise.race([pipeline?.encoderWorklet?.runDiagnostics(diagnosticsState), delayAsyncWith(timeout, diagnosticsState)])) ?? diagnosticsState;

        // As we are having issues with starting recording - let's recreate AudioContext
        await recordingAudioContextSource.reset();
        await audioContextSource.reset();
        Interactive.isInteractive = false;

        return diagnosticsState;
    }

    // recorder state event handlers called by JS recording pipeline

    public onConnectionStateChanged(isConnected: boolean, _noWait?: RpcNoWait): Promise<void> {
        AudioRecorderState.setConnected(isConnected);
        return ResolvedPromise.Void;
    }

    public onVoiceStateChanged(isVoiceActive: boolean, _noWait?: RpcNoWait): Promise<void> {
        RecordingActivity.setVoiceActive(isVoiceActive);
        AudioRecorderState.setVoiceActive(isVoiceActive);
        return ResolvedPromise.Void;
    }

    public onAudioPowerChange(power: number, _noWait?: RpcNoWait): Promise<void> {
        RecordingActivity.setAudioPower(power);
        return ResolvedPromise.Void;
    }

    public microphoneIsCaptured(noWait?: RpcNoWait): Promise<void> {
        AudioRecorderState.microphoneIsCaptured();
        return ResolvedPromise.Void;
    }

    public onRecorderShutdown(reason: string, _noWait?: RpcNoWait): Promise<void> {
        // Worker auto-shuts-down its pipeline (e.g. heartbeat-lost while main thread was hung).
        // Run regular stop() to release the microphone and propagate state to the UI.
        warnLog?.log(`onRecorderShutdown: reason=${reason}`);
        void this.stop();
        return ResolvedPromise.Void;
    }

    // Debug-only: stop sending heartbeats to the encoder worker for the given duration so the
    // worker watchdog can fire. Used by DebugUI.suspendAudioRecorderHeartbeat to simulate a hung main thread.
    public suspendHeartbeat(durationMs: number): void {
        this.heartbeatSuspendedUntil = Date.now() + durationMs;
        infoLog?.log(`suspendHeartbeat: heartbeats paused for ${durationMs}ms`);
    }

    // Private/Internal methods

    private startHeartbeat(): void {
        this.stopHeartbeat();
        const sendHeartbeat = () => {
            if (Date.now() < this.heartbeatSuspendedUntil)
                return; // Debug suspension via DebugUI.suspendAudioRecorderHeartbeat
            void this.encoderWorker?.heartbeat(rpcNoWait);
        };
        // Send the first heartbeat immediately so the worker doesn't have to wait a full interval after start().
        sendHeartbeat();
        this.heartbeatTimerId = setInterval(sendHeartbeat, AUDIO.rec.heartbeat.intervalMs);
    }

    private stopHeartbeat(): void {
        if (this.heartbeatTimerId === undefined)
            return;
        clearInterval(this.heartbeatTimerId);
        this.heartbeatTimerId = undefined;
    }

    private async ensureInitialized(): Promise<void> {
        if (this.state !== 'inactive') {
            if (this.whenInitialized.isCompleted)
                return;

            await Promise.race([this.whenInitialized, delayAsync(5000)]);
            if (this.whenInitialized.isCompleted)
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

    private async startMicrophoneStream(context: AudioContext, pipeline: AttachedRecordingPipeline): Promise<void> {
        if (this.stream?.active && this.source?.context === context)
            return;

        await this.stopMicrophoneStream();
        debugLog?.log(`startMicrophoneStream(): getting microphone stream`);
        try {
            this.stream = await OpusMediaRecorder.getMicrophoneStream();
            this.source = context.createMediaStreamSource(this.stream);

            // After acquiring the stream, AudioContext might be suspended, so we need to resume it
            if (Interactive.isAlwaysInteractive) {
                await context.resume();
            }
            else if (context.state === 'suspended') {
                await recordingAudioContextSource.interactiveResume(context as AppAudioContext);
            }

            if (!pipeline.vadWorkletInstance)
                throw new Error('startMicrophoneStream(): vadWorkletInstance is not initialized');
            if (!pipeline.encoderWorkletInstance)
                throw new Error('startMicrophoneStream(): encoderWorkletInstance is not initialized');
            this.source.connect(pipeline.vadWorkletInstance);
            this.source.connect(pipeline.encoderWorkletInstance);
            debugLog?.log(`startMicrophoneStream(): microphone stream has been connected to the pipeline`);
        } catch (e) {
            await this.stopMicrophoneStream();
            warnLog?.log('startMicrophoneStream(): getMicrophoneStream() failed:', e);
        }
    }

    public async stopMicrophoneStream(): Promise<void> {
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
