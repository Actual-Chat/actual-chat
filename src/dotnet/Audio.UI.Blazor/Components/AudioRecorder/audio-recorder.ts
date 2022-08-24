import { ObjectPool } from 'object-pool';
import { OpusMediaRecorder } from './opus-media-recorder';

const LogScope = 'AudioRecorder';

export class AudioRecorder {
    private static recorderPool =
        new ObjectPool<OpusMediaRecorder>(() => new OpusMediaRecorder());
    private readonly debug = false;
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly isMicrophoneAvailable: boolean;
    private recorder: OpusMediaRecorder;
    private state: 'inactive' | 'recording' = 'inactive';
    private readonly sessionId: string;

    public constructor(blazorRef: DotNet.DotNetObject, sessionId: string) {
        this.blazorRef = blazorRef;
        this.sessionId = sessionId;
        this.isMicrophoneAvailable = false;

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
    }

    public static create(blazorRef: DotNet.DotNetObject, sessionId: string) {
        return new AudioRecorder(blazorRef, sessionId);
    }

    public async canRecord(): Promise<boolean> {
        try {
            await navigator.mediaDevices.getUserMedia({video: false, audio: true});
            return true;
        } catch (ex: any) {
            console.error(`${LogScope}.isMicrophoneAvailable: microphone is unavailable.`, ex);
            return false;
        }
    }

    public async startRecording(chatId : string): Promise<void> {
        try {
            if (this.isRecording())
                return;

            if (!this.isMicrophoneAvailable) {
                console.error(`${LogScope}.startRecording: microphone is unavailable.`);
                return;
            }

            this.recorder = await AudioRecorder.recorderPool.get();

            const { blazorRef, sessionId } = this;
            await this.recorder.start(sessionId, chatId);
            await blazorRef.invokeMethodAsync('OnRecordingStarted', chatId);
        }
        catch (e) {
            console.error(e);
        }
        finally {
            this.state = 'recording';
        }
    }

    public async stopRecording(): Promise<void> {
        try {
            if (!this.isRecording())
                return;
            if (this.debug)
                console.log(`${LogScope}.stopRecording: started`);

            await this.recorder.stop();
            await AudioRecorder.recorderPool.release(this.recorder);

            await this.blazorRef.invokeMethodAsync('OnRecordingStopped');
            if (this.debug)
                console.log(`${LogScope}.stopRecording: completed`);
        }
        catch(e) {
            console.error(e);
        }
        finally {
            this.state = 'inactive';
        }
    }

    private isRecording() {
        return this.state === 'recording';
    }
}
