import DetectRTC from 'detectrtc';
import { Log } from 'logging';
import { PromiseSource } from 'promises';
import { DeviceInfo } from 'device-info';
import { opusMediaRecorder } from './opus-media-recorder';
import { BrowserInfo } from '../../../UI.Blazor/Services/BrowserInfo/browser-info';


const { debugLog, warnLog, errorLog } = Log.get('AudioRecorder');

export class AudioRecorder {
    private readonly recorderId: string;

    private static whenInitialized: Promise<void> | null;
    private state: 'starting' | 'failed' | 'recording' | 'stopped' = 'stopped';

    public static init(): Promise<void> {
        debugLog?.log(`-> init()`);
        AudioRecorder.whenInitialized = new Promise<void>(resolve => {
            DetectRTC.load(resolve);
            debugLog?.log(`<- init(): resolved`);
        });

        return AudioRecorder.whenInitialized;
    }

    /** Called by Blazor  */
    public static create(recorderId: string) {
        return new AudioRecorder(recorderId);
    }

    public constructor(recorderId: string) {
        this.recorderId = recorderId;
        if (!AudioRecorder.whenInitialized)
            void AudioRecorder.init();
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

    /** Called by Blazor  */
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

            await opusMediaRecorder.start(this.recorderId, chatId, repliedChatEntryId);
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
