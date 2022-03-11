import { ObjectPool } from 'object-pool';
import { OpusMediaRecorder } from './opus-media-recorder';

const LogScope = 'AudioRecorder';

export class AudioRecorder {
    private static recorderPool = new ObjectPool<OpusMediaRecorder>(() => new OpusMediaRecorder());
    private readonly debug = false;
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly isMicrophoneAvailable: boolean;
    private recorder: OpusMediaRecorder;
    private state: 'inactive' | 'recording' = 'inactive';
    private readonly sessionId: string;
    private readonly chatId: string;

    public constructor(blazorRef: DotNet.DotNetObject, sessionId: string, chatId: string) {
        this.blazorRef = blazorRef;
        this.sessionId = sessionId;
        this.chatId = chatId;
        this.isMicrophoneAvailable = false;

        if (blazorRef == null)
            console.error(`${LogScope}.constructor: blazorRef == null`);

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

    public static create(blazorRef: DotNet.DotNetObject, sessionId: string, chatId: string) {
        return new AudioRecorder(blazorRef, sessionId, chatId);
    }

    public static async initRecorderPool(): Promise<void> {
        const recorder = await this.recorderPool.get();
        await this.recorderPool.release(recorder);
    }

    public async startRecording(): Promise<void> {
        try {
            if (this.isRecording())
                return;

            if (!this.isMicrophoneAvailable) {
                console.error(`${LogScope}.startRecording: microphone is unavailable.`);
                return;
            }

            this.recorder = await AudioRecorder.recorderPool.get();

            const { blazorRef, sessionId, chatId } = this;
            await this.recorder.start(sessionId, chatId);
            await blazorRef.invokeMethodAsync('OnStartRecording');
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
