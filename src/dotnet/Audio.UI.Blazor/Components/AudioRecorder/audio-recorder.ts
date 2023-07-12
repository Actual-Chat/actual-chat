import DetectRTC from 'detectrtc';
import { Log } from 'logging';
import { PromiseSource } from 'promises';
import { DeviceInfo } from 'device-info';
import { opusMediaRecorder } from './opus-media-recorder';
import { BrowserInfo } from '../../../UI.Blazor/Services/BrowserInfo/browser-info';
import {BrowserInit} from "../../../UI.Blazor/Services/BrowserInit/browser-init";
import {EventHandler} from "event-handling";


const { debugLog, warnLog, errorLog } = Log.get('AudioRecorder');

export class AudioRecorder {
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly recorderId: string;
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

    /** Called from Blazor */
    public static create(blazorRef: DotNet.DotNetObject, recorderId: string) {
        return new AudioRecorder(blazorRef, recorderId);
    }

    public constructor(blazorRef: DotNet.DotNetObject, recorderId: string) {
        this.blazorRef = blazorRef;
        this.recorderId = recorderId;
        this.onReconnected = BrowserInit.reconnectedEvents.add(() => this.reconnect());
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
                    stream = await navigator.mediaDevices.getUserMedia({ audio: true, video: false });

                    // update DetectRTC with new microphone permission if granted
                    AudioRecorder.whenInitialized = new Promise<void>(resolve => DetectRTC.load(resolve));
                }
                catch (error) {
                    errorLog?.log(`requestPermission: failed to request microphone permissions`, error);
                    return false;
                }
                finally {
                    if (stream) {
                        const audioTracks = stream.getAudioTracks();
                        const videoTracks = stream.getVideoTracks();
                        debugLog?.log(`requestPermission: found `, audioTracks.length, 'audio tracks, ', videoTracks.length, 'video tracks to stop, stopping...');
                        audioTracks.forEach(t => t.stop());
                        videoTracks.forEach(t => t.stop());
                    }
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
    public async startRecording(chatId: string, repliedChatEntryId: string): Promise<boolean> {
        debugLog?.log(`-> startRecording(), ChatId =`, chatId);
        await AudioRecorder.whenInitialized;

        try {
            if (this.state === 'recording' || this.state === 'starting') {
                warnLog?.log('startRecording: seems like server and client state are not consistent');
                return true;
            }

            this.state = 'starting';
            const isMaui = BrowserInfo.appKind == 'MauiApp';
            if (!DetectRTC.hasMicrophone) {
                errorLog?.log(`startRecording: microphone is unavailable`);
                return false;
            }
            debugLog?.log(`startRecording(), after hasMicrophone`);

            if (!DetectRTC.isWebsiteHasMicrophonePermissions && !isMaui) {
                if (DeviceInfo.isFirefox) {
                    // Firefox doesn't support microphone permissions query
                    const hasMicrophonePromise = new PromiseSource<boolean>();
                    navigator.mediaDevices.getUserMedia({ audio: true, video: false })
                        .then(
                            stream => {
                                stream.getAudioTracks().forEach(t => t.stop());
                                stream.getVideoTracks().forEach(t => t.stop());
                                hasMicrophonePromise.resolve(true);
                            },
                            () => {
                                hasMicrophonePromise.resolve(false);
                            });
                    const hasMicrophone = await hasMicrophonePromise;
                    if (!hasMicrophone) {
                        errorLog?.log(`startRecording: microphone permission is required`);
                        return false;
                    }
                }
                else {
                    errorLog?.log(`startRecording: microphone permission is required`);
                    return false;
                }
            }
            debugLog?.log(`startRecording(), after isWebsiteHasMicrophonePermissions`);

            await opusMediaRecorder.start(
                this.recorderId,
                chatId,
                repliedChatEntryId,
                (isRecording, isConnected, isVoiceActive) =>
                    this.onRecordingStateChange(isRecording, isConnected, isVoiceActive));
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

    private async onRecordingStateChange(isRecording: boolean, isConnected: boolean, isVoiceActive: boolean): Promise<void> {
        try {
            await this.blazorRef.invokeMethodAsync('OnRecordingStateChange', isRecording, isConnected, isVoiceActive);
        }
        catch (error) {
            errorLog?.log(`onRecordingStateChange: unhandled error:`, error);
        }
    }
}
