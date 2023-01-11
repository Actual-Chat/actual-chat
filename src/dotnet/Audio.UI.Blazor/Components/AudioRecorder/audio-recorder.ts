import { ObjectPool } from 'object-pool';
import { OpusMediaRecorder } from './opus-media-recorder';
import { Log, LogLevel } from 'logging';

const LogScope = 'AudioRecorder';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export class AudioRecorder {
    private static recorderPool = new ObjectPool<OpusMediaRecorder>(() => new OpusMediaRecorder());
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly isMicrophoneAvailable: boolean;
    private readonly sessionId: string;
    private readonly whenRecorderAvailable: Promise<OpusMediaRecorder>;
    private isRecording: boolean = false;

    public static create(blazorRef: DotNet.DotNetObject, sessionId: string) {
        return new AudioRecorder(blazorRef, sessionId);
    }

    public constructor(blazorRef: DotNet.DotNetObject, sessionId: string) {
        this.blazorRef = blazorRef;
        this.sessionId = sessionId;
        this.isMicrophoneAvailable = false;

        errorLog?.assert(blazorRef != null, `blazorRef == null`);

        // Temporarily
        if (typeof navigator.mediaDevices === 'undefined' || !navigator.mediaDevices.getUserMedia) {
            alert('Please allow to use microphone.');

            if (navigator['getUserMedia'] !== undefined) {
                alert('This browser seems supporting deprecated getUserMedia API.');
            }
        } else {
            this.isMicrophoneAvailable = true;
        }

        this.whenRecorderAvailable = AudioRecorder.recorderPool.get();
        this.whenRecorderAvailable.catch(error => {
            errorLog?.log(`constructor: recorder initialization error:`, error);
        });
    }

    public async dispose(): Promise<void> {
        const recorder = await this.whenRecorderAvailable;
        await AudioRecorder.recorderPool.release(recorder);
    }

    public async canRecord(): Promise<boolean> {
        try {
            const stream = await navigator.mediaDevices.getUserMedia({video: false, audio: true});
            stream.getAudioTracks().forEach(t => t.stop());
            stream.getVideoTracks().forEach(t => t.stop());
            return true;
        } catch (error) {
            errorLog?.log(`canRecord: microphone is unavailable, error:`, error);
            return false;
        }
    }

    public async startRecording(chatId: string): Promise<void> {
        try {
            if (this.isRecording)
                return;

            if (!this.isMicrophoneAvailable) {
                errorLog?.log(`startRecording: microphone is unavailable`);
                return;
            }

            const { blazorRef, sessionId } = this;
            const recorder = await this.whenRecorderAvailable;
            await recorder.start(sessionId, chatId);
            await blazorRef.invokeMethodAsync('OnRecordingStarted', chatId);
        }
        catch (error) {
            errorLog?.log(`startRecording: unhandled error:`, error);
        }
        finally {
            this.isRecording = true;
        }
    }

    public async stopRecording(): Promise<void> {
        try {
            if (!this.isRecording)
                return;
            debugLog?.log(`-> stopRecording`);

            const recorder = await this.whenRecorderAvailable;
            await recorder.stop();

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
