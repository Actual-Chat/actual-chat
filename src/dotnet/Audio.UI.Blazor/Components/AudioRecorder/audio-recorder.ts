import DetectRTC from 'detectrtc';
import { ObjectPool } from 'object-pool';
import { OpusMediaRecorder } from './opus-media-recorder';
import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'AudioRecorder';


const LogScope = 'AudioRecorder';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export class AudioRecorder {
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly sessionId: string;
    private readonly opusMediaRecorder: OpusMediaRecorder;

    private whenInitialized: Promise<void>;
    private isRecording: boolean = false;

    public static create(blazorRef: DotNet.DotNetObject, sessionId: string) {
        return new AudioRecorder(blazorRef, sessionId);
    }

    public constructor(blazorRef: DotNet.DotNetObject, sessionId: string) {
        this.blazorRef = blazorRef;
        this.sessionId = sessionId;

        errorLog?.assert(blazorRef != null, `blazorRef == null`);

        this.whenInitialized = new Promise<void>(resolve => DetectRTC.load(resolve));
        this.opusMediaRecorder = new OpusMediaRecorder();

    }

    public async dispose(): Promise<void> {
        await this.opusMediaRecorder.dispose();
    }

    public async canRecord(): Promise<boolean> {
        await this.whenInitialized;
        const hasMicrophone = DetectRTC.isAudioContextSupported
            && DetectRTC.hasMicrophone
            && DetectRTC.isGetUserMediaSupported
            && DetectRTC.isWebsiteHasMicrophonePermissions;

        if (!hasMicrophone) {
            // requests microphone permission
            try {
                const stream = await navigator.mediaDevices.getUserMedia({video: false, audio: true});
                stream.getAudioTracks().forEach(t => t.stop());
                stream.getVideoTracks().forEach(t => t.stop());
                this.whenInitialized = new Promise<void>(resolve => DetectRTC.load(resolve));
            }
            catch (error) {
                errorLog?.log(`canRecord: failed to request microphone permissions`, error);
                return false;
            }

            return true;
        }

        return hasMicrophone;
    }

    public async startRecording(chatId: string): Promise<boolean> {
        await this.whenInitialized;

        try {
            if (this.isRecording)
                return true;

            this.isRecording = true;

            if (!DetectRTC.hasMicrophone) {
                errorLog?.log(`startRecording: microphone is unavailable`);
                return false;
            }

            if (!DetectRTC.isWebsiteHasMicrophonePermissions) {
                errorLog?.log(`startRecording: microphone permission is required`);
                return false;
            }

            const { blazorRef, sessionId } = this;
            await this.opusMediaRecorder.start(sessionId, chatId);
            await blazorRef.invokeMethodAsync('OnRecordingStarted', chatId);
        }
        catch (error) {
            errorLog?.log(`startRecording: unhandled error:`, error);
            throw error;
        }

        return true;
    }

    public async stopRecording(): Promise<void> {
        try {
            if (!this.isRecording)
                return;
            debugLog?.log(`-> stopRecording`);

            await this.opusMediaRecorder.stop();

            await this.blazorRef.invokeMethodAsync('OnRecordingStopped');
        }
        catch (error) {
            errorLog?.log(`stopRecording: unhandled error:`, error);
        }
        finally {
            this.isRecording = false;
            debugLog?.log(`<- stopRecording`);
        }
    }
}
