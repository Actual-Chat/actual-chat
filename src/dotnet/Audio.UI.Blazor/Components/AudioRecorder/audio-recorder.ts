import DetectRTC from 'detectrtc';
import { Log } from 'logging';
import { DeviceInfo } from 'device-info';
import { OpusMediaRecorder, opusMediaRecorder } from './opus-media-recorder';
import { BrowserInfo } from '../../../UI.Blazor/Services/BrowserInfo/browser-info';
import { BrowserInit } from '../../../UI.Blazor/Services/BrowserInit/browser-init';
import { EventHandler } from 'event-handling';
import { AudioPlayer } from '../AudioPlayer/audio-player';
import { audioContextSource } from '../../Services/audio-context-source';
import { VoiceActivityChange } from './workers/audio-vad-contract';


const { debugLog, warnLog, errorLog } = Log.get('AudioRecorder');

export class AudioDiagnosticsState {
    public isPlayerInitialized?: boolean = null;
    public isRecorderInitialized?: boolean = null;
    public hasMicrophonePermission?: boolean = null;
    public isAudioContextSourceActive?: boolean = null;
    public isAudioContextActive?: boolean = null;
    public hasMicrophoneStream?: boolean = null;
    public isVadActive?: boolean = null;
    public lastVadEvent?: VoiceActivityChange = null;
    public lastVadFrameProcessedAt?: number = null;
    public isConnected?: boolean = null;
    public lastFrameProcessedAt?: number = null;
    public vadWorkletState?: 'running' | 'stopped' | 'inactive' = null;
    public lastVadWorkletFrameProcessedAt?: number = null;
    public encoderWorkletState?: 'running' | 'stopped' | 'inactive' = null;
    public lastEncoderWorkletFrameProcessedAt?: number = null;
}

export class AudioRecorder {
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly onReconnected: EventHandler<void>;

    private static whenInitialized: Promise<void> | null;
    private state: 'starting' | 'failed' | 'recording' | 'stopped' = 'stopped';

    public static init(): Promise<void> {
        if (this.whenInitialized)
            return this.whenInitialized;

        debugLog?.log(`-> init()`);
        return this.whenInitialized = new Promise<void>(resolve => {
            DetectRTC.load(resolve);
            debugLog?.log(`<- init(): resolved`);
        });
    }

    public static async terminate(): Promise<void> {
        debugLog?.log(`-> terminate()`);
        await opusMediaRecorder.stop();
        await opusMediaRecorder.terminate();
        debugLog?.log(`<- terminate()`);
    }

    /** Called from Blazor */
    public static create(blazorRef: DotNet.DotNetObject) {
        return new AudioRecorder(blazorRef);
    }

    public constructor(blazorRef: DotNet.DotNetObject) {
        this.blazorRef = blazorRef;
        this.onReconnected = BrowserInit.reconnectedEvents.add(() => this.reconnect());
        opusMediaRecorder.subscribeToStateChanges((isRecording, isConnected, isVoiceActive) =>
            this.onRecordingStateChange(isRecording, isConnected, isVoiceActive));
        void AudioRecorder.init();
    }

    /** Called from Blazor */
    public async dispose(): Promise<void> {
        debugLog?.log(`-> dispose()`);
        if (this.onReconnected)
            BrowserInit.reconnectedEvents.remove(this.onReconnected);
        try {
            await opusMediaRecorder.stop();
        } catch (e) {
            errorLog?.log(`dispose: failed to stop recording`, e);
            throw e;
        }
    }

    /** Called from Blazor  */
    public async checkPermission(): Promise<PermissionState> {
        debugLog?.log(`-> checkPermission()`);
        try {
            await AudioRecorder.whenInitialized;

            const isMaui = BrowserInfo.appKind == 'MauiApp';
            const hasMicrophone = DetectRTC.isAudioContextSupported
                && DetectRTC.hasMicrophone
                && DetectRTC.isGetUserMediaSupported
                && (DetectRTC.isWebsiteHasMicrophonePermissions || isMaui);

            debugLog?.log(`checkPermission(): hasMicrophone=`,
                hasMicrophone,
                DetectRTC.isAudioContextSupported,
                DetectRTC.hasMicrophone,
                DetectRTC.isGetUserMediaSupported,
                DetectRTC.isWebsiteHasMicrophonePermissions);

            if ('permissions' in navigator && !DeviceInfo.isFirefox) {
                // @ts-ignore
                const status = await navigator.permissions.query({ name: 'microphone' });
                return status.state;
            }

            return hasMicrophone
                ? 'prompt'
                : 'denied';
        }
        finally {
            debugLog?.log(`<- checkPermission()`);
        }
    }

    /** Called from Blazor  */
    public async requestPermission(): Promise<boolean> {
        debugLog?.log(`-> requestPermission()`);
        try {
            await AudioRecorder.whenInitialized;

            const isMaui = BrowserInfo.appKind == 'MauiApp';
            const hasMicrophone = DetectRTC.isAudioContextSupported
                && DetectRTC.hasMicrophone
                && DetectRTC.isGetUserMediaSupported
                && (DetectRTC.isWebsiteHasMicrophonePermissions || isMaui);

            debugLog?.log(`requestPermission(): hasMicrophone=`,
                hasMicrophone,
                DetectRTC.isAudioContextSupported,
                DetectRTC.hasMicrophone,
                DetectRTC.isGetUserMediaSupported,
                DetectRTC.isWebsiteHasMicrophonePermissions);

            if (!hasMicrophone) {
                // Requests microphone permission
                let stream: MediaStream = null;
                try {
                    debugLog?.log(`requestPermission: detecting active tracks to stop`);
                    if ('audioSession' in navigator) {
                        navigator.audioSession['type'] = 'play-and-record'; // 'playback'
                    }
                    if (BrowserInfo.appKind === 'MauiApp' && DeviceInfo.isIos ) {
                        // iOS MAUI keeps microphone acquired, so let's return true there to avoid user complains
                        return true;
                    }
                    else {
                        stream = await navigator.mediaDevices.getUserMedia({ audio: true, video: false });
                        await OpusMediaRecorder.stopStreamTracks(stream);
                    }

                    // update DetectRTC with new microphone permission if granted
                    AudioRecorder.whenInitialized = new Promise<void>(resolve => DetectRTC.load(resolve));
                }
                catch (error) {
                    errorLog?.log(`requestPermission: failed to request microphone permissions`, error);
                    return false;
                }
                finally {
                    await OpusMediaRecorder.stopStreamTracks(stream);
                    stream = null;
                }

                return true;
            }

            return hasMicrophone;
        }
        finally {
            debugLog?.log(`<- requestPermission()`);
        }
    }

    /** Called from Blazor  */
    public async startRecording(chatId: string, repliedChatEntryId: string, sessionToken: string): Promise<boolean> {
        debugLog?.log(`-> startRecording(), ChatId =`, chatId);
        await AudioRecorder.whenInitialized;

        try {
            if (sessionToken)
                await opusMediaRecorder.setSessionToken(sessionToken);

            if (this.state === 'recording' || this.state === 'starting') {
                warnLog?.log('startRecording: it seems that server and client states are inconsistent');
                return true;
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
            throw e;
        }
        finally {
            debugLog?.log(`<- startRecording()`);
        }

        return true;
    }

    /** Called from Blazor  */
    public async stopRecording(): Promise<void> {
        try {
            debugLog?.log(`-> stopRecording`);
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

    /** Called from Blazor */
    public reconnect(): Promise<void> {
        debugLog?.log(`reconnect()`);
        return opusMediaRecorder.reconnect();
    }

    /** Called from Blazor */
    public conversationSignal(): Promise<void> {
        debugLog?.log(`conversationSignal()`);
        return opusMediaRecorder.conversationSignal();
    }

    /** Called from Blazor */
    public async runDiagnostics(): Promise<AudioDiagnosticsState> {
        const diagnosticsState = new AudioDiagnosticsState();
        diagnosticsState.isPlayerInitialized = AudioPlayer.isInitialized;

        const isMaui = BrowserInfo.appKind == 'MauiApp';
        const hasMicrophone = DetectRTC.isAudioContextSupported
            && DetectRTC.hasMicrophone
            && DetectRTC.isGetUserMediaSupported
            && DetectRTC.isWebsiteHasMicrophonePermissions;
        if (!isMaui)
            diagnosticsState.hasMicrophonePermission = hasMicrophone;

        diagnosticsState.isAudioContextSourceActive = audioContextSource.isActive;
        diagnosticsState.isAudioContextActive = audioContextSource.context && audioContextSource.context.state === 'running';
        warnLog?.log('runDiagnostics: ', diagnosticsState);
        return await opusMediaRecorder.runDiagnostics(diagnosticsState);
    }

    private async onRecordingStateChange(isRecording: boolean, isConnected: boolean, isVoiceActive: boolean): Promise<void> {
        try {
            await this.blazorRef.invokeMethodAsync('OnRecordingStateChange', isRecording, isConnected, isVoiceActive);
        }
        catch (error) {
            errorLog?.log(`onRecordingStateChange: unhandled error:`, error);
        }
    }
}
