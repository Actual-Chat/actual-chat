import { Log } from 'logging';
import { AudioPlayer } from '../Components/AudioPlayer/audio-player';
import { opusMediaRecorder } from '../Components/AudioRecorder/opus-media-recorder';
import {AudioRecorder} from "../Components/AudioRecorder/audio-recorder";

const { infoLog, warnLog } = Log.get('AudioInfo');

export class AudioInitializer {
    private static backendRef: DotNet.DotNetObject = null;

    public static isRecorderInitialized = false;
    public static isPlayerInitialized = false;

    public static async init(backendRef1: DotNet.DotNetObject, baseUri: string, canUseNNVad: boolean): Promise<void> {
        this.backendRef = backendRef1;
        infoLog?.log(`-> init`);

        if (!this.isPlayerInitialized) {
            try {
                await AudioPlayer.init();
                this.isPlayerInitialized = true;
            }
            catch (e) {
                warnLog?.log(`init: AudioPlayer.init failed:`, e);
                throw e;
            }
        }

        if (!this.isRecorderInitialized) {
            try {
                await AudioRecorder.init();
                await opusMediaRecorder.init(baseUri, canUseNNVad);
                this.isRecorderInitialized = true;
            }
            catch (e) {
                warnLog?.log(`init: opusMediaRecorder.init failed:`, e);
                throw e;
            }
        }

        globalThis["audioInitializer"] = this;
        infoLog?.log(`<- init`);
    }
}

