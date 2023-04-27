import { AudioPlayer } from '../Components/AudioPlayer/audio-player';
import { opusMediaRecorder } from '../Components/AudioRecorder/opus-media-recorder';
import {delayAsync, PromiseSource} from 'promises';
import { Log } from 'logging';

const { infoLog, warnLog } = Log.get('AudioInfo');

export class AudioInfo {
    private static backendRef: DotNet.DotNetObject = null;

    public static whenReady: PromiseSource<void> = new PromiseSource<void>();
    public static isRecorderInitialized = false;
    public static isPlayerInitialized = false;

    public static async init(backendRef1: DotNet.DotNetObject, baseUri: string): Promise<void> {
        this.backendRef = backendRef1;
        infoLog?.log(`init`);

        try {
            if (!this.isPlayerInitialized) {
                await AudioPlayer.init();
                this.isPlayerInitialized = true;
            }

            if (!this.isRecorderInitialized) {
                await opusMediaRecorder.init(baseUri);
                this.isRecorderInitialized = true;
            }
        }
        catch (e) {
            warnLog?.log(
                `failed: `,
                e,
                'isRecorderInitialized:',
                this.isRecorderInitialized,
                'isPlayerInitialized:',
                this.isPlayerInitialized);
            throw e;
        }

        this.whenReady.resolve(undefined);
        globalThis["audioInfo"] = this;
        infoLog?.log(`ready`);
    }
}

