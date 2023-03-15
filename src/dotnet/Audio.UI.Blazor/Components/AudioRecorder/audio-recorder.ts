import DetectRTC from 'detectrtc';
import { opusMediaRecorder } from './opus-media-recorder';
import { Log } from 'logging';
import { BrowserInfo } from '../../../UI.Blazor/Services/BrowserInfo/browser-info';
import { PromiseSource } from 'promises';

const { debugLog, warnLog, errorLog } = Log.get('AudioRecorder');

export class AudioRecorder {
    private readonly sessionId: string;

    private whenInitialized: Promise<void>;
    private state: 'starting' | 'failed' | 'recording' | 'stopped' = 'stopped';

    /** Called by Blazor  */
    public static create(sessionId: string) {
        return new AudioRecorder(sessionId);
    }

    public constructor(sessionId: string) {
        this.sessionId = sessionId;
        this.whenInitialized = new Promise<void>(resolve => DetectRTC.load(resolve));
    }

    /** Called by Blazor  */
    public async dispose(): Promise<void> {
        debugLog?.log(`-> dispose()`);
        try {
            await opusMediaRecorder.stop();
        } catch (e) {
            errorLog?.log(`dispose: failed to stop recording`, e);
            throw e;
        }
    }

    /** Called by Blazor  */
    public async requestPermission(): Promise<boolean> {
        debugLog?.log(`-> requestPermission()`);
        await this.whenInitialized;

        const hasMicrophone = DetectRTC.isAudioContextSupported
            && DetectRTC.hasMicrophone
            && DetectRTC.isGetUserMediaSupported
            && DetectRTC.isWebsiteHasMicrophonePermissions;

        if (!hasMicrophone) {
            // Requests microphone permission
            try {
                debugLog?.log(`requestPermission: detecting active tracks to stop`);
                const stream = await navigator.mediaDevices.getUserMedia({video: false, audio: true});
                const audioTracks = stream.getAudioTracks();
                const videoTracks = stream.getVideoTracks();
                debugLog?.log(`requestPermission: found `, audioTracks.length, 'audio tracks, ', videoTracks.length, 'video tracks to stop, stopping...');
                audioTracks.forEach(t => t.stop());
                videoTracks.forEach(t => t.stop());
                this.whenInitialized = new Promise<void>(resolve => DetectRTC.load(resolve));
            }
            catch (error) {
                errorLog?.log(`requestPermission: failed to request microphone permissions`, error);
                return false;
            }

            return true;
        }

        return hasMicrophone;
    }

    /** Called by Blazor  */
    public async startRecording(chatId: string, repliedChatEntryId: string): Promise<boolean> {
        debugLog?.log(`-> startRecording(), ChatId =`, chatId);
        await this.whenInitialized;

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

            if (!DetectRTC.isWebsiteHasMicrophonePermissions && !isMaui) {
                if (navigator.userAgent.toLowerCase().includes('firefox')) {
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

            await opusMediaRecorder.start(this.sessionId, chatId, repliedChatEntryId);
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

    /** Called by Blazor  */
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
}
