import { Log } from 'logging';
import { AudioPlayer } from '../Components/AudioPlayer/audio-player';
import { opusMediaRecorder } from '../Components/AudioRecorder/opus-media-recorder';
import {AudioRecorder} from "../Components/AudioRecorder/audio-recorder";
import {audioContextSource} from "./audio-context-source";

const { infoLog, warnLog } = Log.get('AudioInfo');

export type BackgroundState = 'Foreground' | 'BackgroundActive' | 'BackgroundIdle';

export class AudioInitializer {
    private static backendRef: DotNet.DotNetObject = null;

    public static backgroundState: BackgroundState = 'Foreground';
    public static isRecorderInitialized = false;
    public static isPlayerInitialized = false;

    /** Called by Blazor */
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

    /** Called by Blazor */
    public static async updateBackgroundState(backgroundState: BackgroundState): Promise<void> {
        infoLog?.log(`-> updateBackgroundState`);
        if (backgroundState === 'BackgroundActive' || backgroundState === 'Foreground') {
            await audioContextSource.resumeAudio();
        }
        else {
            await audioContextSource.suspendAudio();
        }
        this.backgroundState = backgroundState;
        infoLog?.log(`<- updateBackgroundState`);
    }
}

