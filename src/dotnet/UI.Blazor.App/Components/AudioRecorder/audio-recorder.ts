// TODO: Fix ESLint errors
/* eslint-disable @typescript-eslint/no-misused-promises, @typescript-eslint/no-unnecessary-condition */
import { opusMediaRecorder } from './opus-media-recorder';
import { BrowserInfo } from '../../../UI.Blazor/Services/BrowserInfo/browser-info';
import { AudioPlayer } from '../AudioPlayer/audio-player';
import { recordingAudioContextSource } from '../../Services/audio-context-source';
import { VoiceActivityChange } from './workers/audio-vad-contract';
import { getLogs } from 'logging';
import { throttle } from 'actuallab-core';
import { WebMicrophonePermissionHandler } from './web-microphone-permission-handler';
import { AudioRecorderState, describeRecordingError } from './audio-recorder-state';
import { Subscription } from 'rxjs';

const { debugLog, infoLog, warnLog, errorLog } = getLogs('AudioRecorder');

export class AudioDiagnosticsState {
    public isPlayerInitialized?: boolean;
    public isRecorderInitialized?: boolean;
    public hasMicrophonePermission?: boolean;
    public isAudioContextSourceMaintained?: boolean;
    public isAudioContextRunning?: boolean;
    public hasMicrophoneStream?: boolean;
    public isVadActive?: boolean;
    public lastVadEvent?: VoiceActivityChange;
    public lastVadFrameProcessedAt?: number;
    public isConnected?: boolean;
    public vadWorkletState?: 'running' | 'ready' | 'inactive' | 'terminated';
    public lastVadWorkletFrameProcessedAt?: number;
    public encoderWorkletState?: 'running' | 'ready' | 'inactive' | 'terminated';
    public lastEncoderWorkletFrameProcessedAt?: number;
}

const HEARTBEAT_INTERVAL = 2000; // ms

export class AudioRecorder {
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly recorderStateChangedSubscription: Subscription;
    private readonly recordingHeartbeatSubscription: Subscription;
    private readonly recordingFailedSubscription: Subscription;

    private state: 'starting' | 'failed' | 'recording' | 'stopped' = 'stopped';
    private chatId?: string;

    public static async terminate(): Promise<void> {
        debugLog?.log(`-> terminate()`);
        await opusMediaRecorder.stop();
        await opusMediaRecorder.terminate();
        debugLog?.log(`<- terminate()`);
    }

    // Called from Blazor
    public static create(blazorRef: DotNet.DotNetObject) {
        return new AudioRecorder(blazorRef);
    }

    public constructor(blazorRef: DotNet.DotNetObject) {
        this.blazorRef = blazorRef;
        this.recorderStateChangedSubscription = AudioRecorderState.stateChanged$.subscribe(state => this.onRecordingStateChange(
            state.isRecording,
            state.isSignalDetected,
            state.isConnected,
            state.isVoiceActive));
        this.recordingHeartbeatSubscription = AudioRecorderState.recordingHeartbeat$.subscribe(() => this.heartbeatThrottled());
        this.recordingFailedSubscription = AudioRecorderState.recordingFailed$.subscribe(
            f => this.onRecordingFailed(f.chatId, f.failure));
    }

    // Called from Blazor
    public async dispose(): Promise<void> {
        debugLog?.log(`-> dispose()`);
        this.chatId = undefined;
        try {
            await opusMediaRecorder.stop();
            this.recorderStateChangedSubscription.unsubscribe();
            this.recordingHeartbeatSubscription.unsubscribe();
            this.recordingFailedSubscription.unsubscribe();
        } catch (e) {
            errorLog?.log(`dispose: failed to stop recording`, e);
            throw e;
        }
    }

    // Called from Blazor. Returns '' on success, otherwise "<result>:<code>" - see RecorderStartResult.
    public async startRecording(chatId: string, repliedChatEntryId: string): Promise<string> {
        debugLog?.log(`-> startRecording(), ChatId =`, chatId);
        this.chatId = chatId;

        try {
            if (this.state === 'recording' || this.state === 'starting') {
                warnLog?.log('startRecording: it seems that server and client states are inconsistent');
                return '';
            }

            this.state = 'starting';
            await opusMediaRecorder.start(chatId, repliedChatEntryId);
            if (this.state !== 'starting')
                // noinspection ExceptionCaughtLocallyJS
                throw new Error('Recording has been stopped.')

            this.state = 'recording';
        }
        catch (e) {
            errorLog?.log(`startRecording: unhandled error:`, e);
            this.state = 'failed';
            this.chatId = undefined;
            // Returned, not rethrown: an exception reaches Blazor as an opaque JSException.
            return describeRecordingError(e);
        }
        finally {
            debugLog?.log(`<- startRecording()`);
        }

        return '';
    }

    // Called from Blazor
    public async stopRecording(): Promise<void> {
        try {
            debugLog?.log(`-> stopRecording`);
            this.chatId = undefined;
            await opusMediaRecorder.stop();
        }
        catch (error) {
            errorLog?.log(`stopRecording: unhandled error:`, error);
            this.state = 'failed';
            throw error;
        }
        finally {
            this.state = 'stopped';
            debugLog?.log(`<- stopRecording`);
        }
    }

    // Called from Blazor
    public conversationSignal(): Promise<void> {
        debugLog?.log(`conversationSignal()`);
        return opusMediaRecorder.conversationSignal();
    }

    // Called from Blazor
    public async runDiagnostics(): Promise<AudioDiagnosticsState> {
        const diagnosticsState = new AudioDiagnosticsState();
        diagnosticsState.isPlayerInitialized = AudioPlayer.isInitialized;

        const isMaui = BrowserInfo.hostKind == 'MauiApp';
        const hasMicrophone = await WebMicrophonePermissionHandler.hasMicrophone();
        const hasPermission = await WebMicrophonePermissionHandler.hasPermission();
        if (!isMaui)
            diagnosticsState.hasMicrophonePermission = hasMicrophone && hasPermission;

        diagnosticsState.isAudioContextSourceMaintained = recordingAudioContextSource.isMaintained;
        diagnosticsState.isAudioContextRunning = recordingAudioContextSource.isContextRunning;
        infoLog?.log('runDiagnostics: ', diagnosticsState);
        return await opusMediaRecorder.runDiagnostics(diagnosticsState);
    }

    // Private methods

    private async onRecordingFailed(chatId: string, failure: string): Promise<void> {
        try {
            await this.blazorRef.invokeMethodAsync('OnRecordingFailed', chatId, failure);
        }
        catch (error) {
            errorLog?.log(`onRecordingFailed: unhandled error:`, error);
        }
    }

    private readonly heartbeatThrottled = throttle(() => this.heartbeat(), HEARTBEAT_INTERVAL);
    private async heartbeat(): Promise<void> {
        try {
            const chatId = this.chatId;
            if (!chatId) {
                void this.stopRecording();
                return;
            }

            const isRecording = await this.blazorRef.invokeMethodAsync<boolean>('IsRecording', chatId);
            if (isRecording)
                return;

            debugLog?.log(`heartbeat: recording is stopped`);
            void this.stopRecording();
        } catch (e) {
            warnLog?.log('heartbeat: failed', e);
            void this.stopRecording();
        }
    }

    private async onRecordingStateChange(isRecording: boolean, isSignalDetected: boolean, isConnected: boolean, isVoiceActive: boolean): Promise<void> {
        try {
            await this.blazorRef.invokeMethodAsync('OnRecordingStateChange', isRecording, isSignalDetected, isConnected, isVoiceActive);
        }
        catch (error) {
            errorLog?.log(`onRecordingStateChange: unhandled error:`, error);
        }
    }
}
