import { ObjectPool } from 'object-pool';
import { OpusMediaRecorder } from './opus-media-recorder';

const LogScope = 'AudioRecorder';

export class AudioRecorder {
    private static recorderPool =
        new ObjectPool<OpusMediaRecorder>((debug: boolean) => new OpusMediaRecorder(debug));
    private readonly debug: boolean = false;
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly isMicrophoneAvailable: boolean;
    private readonly sessionId: string;
    private readonly whenRecorderAvailable: Promise<OpusMediaRecorder>;
    private isRecording: boolean = false;

    public constructor(blazorRef: DotNet.DotNetObject, sessionId: string, debug: boolean) {
        this.blazorRef = blazorRef;
        this.sessionId = sessionId;
        this.isMicrophoneAvailable = false;
        this.debug = debug;

        if (blazorRef == null)
            console.error(`${LogScope}.ctor: blazorRef == null`);

        // Temporarily
        if (typeof navigator.mediaDevices === 'undefined' || !navigator.mediaDevices.getUserMedia) {
            alert('Please allow to use microphone.');

            if (navigator['getUserMedia'] !== undefined) {
                alert('This browser seems supporting deprecated getUserMedia API.');
            }
        } else {
            this.isMicrophoneAvailable = true;
        }

        this.whenRecorderAvailable = AudioRecorder.recorderPool.get(this.debug);
        this.whenRecorderAvailable.catch(ex => {
            console.error(`${LogScope}.constructor: recorder initialization failed.`, ex);
        });
    }

    public async dispose(): Promise<void> {
        const recorder = await this.whenRecorderAvailable;
        await AudioRecorder.recorderPool.release(recorder);
    }

    public static create(blazorRef: DotNet.DotNetObject, sessionId: string, isDebug: boolean) {
        return new AudioRecorder(blazorRef, sessionId, isDebug);
    }

    public async canRecord(): Promise<boolean> {
        try {
            const stream = await navigator.mediaDevices.getUserMedia({video: false, audio: true});
            stream.getAudioTracks().forEach(t => t.stop());
            stream.getVideoTracks().forEach(t => t.stop());
            return true;
        } catch (ex: any) {
            console.error(`${LogScope}.isMicrophoneAvailable: microphone is unavailable.`, ex);
            return false;
        }
    }

    public async startRecording(chatId : string): Promise<void> {
        try {
            if (this.isRecording)
                return;

            if (!this.isMicrophoneAvailable) {
                console.error(`${LogScope}.startRecording: microphone is unavailable.`);
                return;
            }

            const { blazorRef, sessionId } = this;
            const recorder = await this.whenRecorderAvailable;
            await recorder.start(sessionId, chatId);
            await blazorRef.invokeMethodAsync('OnRecordingStarted', chatId);
        }
        catch (e) {
            console.error(e);
        }
        finally {
            this.isRecording = true;
        }
    }

    public async stopRecording(): Promise<void> {
        try {
            if (!this.isRecording)
                return;
            if (this.debug)
                console.log(`${LogScope}.stopRecording: started`);

            const recorder = await this.whenRecorderAvailable;
            await recorder.stop();

            await this.blazorRef.invokeMethodAsync('OnRecordingStopped');
            if (this.debug)
                console.log(`${LogScope}.stopRecording: completed`);
        }
        catch(e) {
            console.error(e);
        }
        finally {
            this.isRecording = false;
        }
    }
}
