import { Log } from 'logging';
import { AudioPlayer } from '../Components/AudioPlayer/audio-player';
import { opusMediaRecorder } from '../Components/AudioRecorder/opus-media-recorder';
import { audioContextSource, recordingAudioContextSource } from './audio-context-source';
import { ResolvedPromise } from 'promises';

const { infoLog, warnLog } = Log.get('AudioInfo');

export type BackgroundState = 'Foreground' | 'BackgroundIdle' | 'BackgroundActive';

export class AudioInitializer {
    private static backendRef: DotNet.DotNetObject = null;

    public static backgroundState: BackgroundState = 'Foreground';
    public static isRecorderInitialized = false;
    public static isPlayerInitialized = false;

    /** Called by Blazor */
    public static async init(backendRef1: DotNet.DotNetObject, baseUri: string, canUseNNVad: boolean): Promise<void> {
        this.backendRef = backendRef1;
        infoLog?.log(`-> init`);

        const initPlayer = async () => {
            try {
                await AudioPlayer.init();
                this.isPlayerInitialized = true;
            } catch (e) {
                warnLog?.log(`init: AudioPlayer.init failed:`, e);
                throw e;
            }
        };

        const initRecorder = async () => {
            try {
                await opusMediaRecorder.init(baseUri, canUseNNVad);
                this.isRecorderInitialized = true;
            } catch (e) {
                warnLog?.log(`init: opusMediaRecorder.init failed:`, e);
                throw e;
            }
        };

        const promises: Promise<void>[] = [
            this.isPlayerInitialized ? ResolvedPromise.Void : initPlayer(),
            this.isRecorderInitialized ? ResolvedPromise.Void : initRecorder(),
        ];
        await Promise.allSettled(promises);
        infoLog?.log(`<- init`);
    }

    /** Called by Blazor */
    public static async setBackgroundState(backgroundState: BackgroundState): Promise<void> {
        infoLog?.log(`setBackgroundState:`, backgroundState);
        this.backgroundState = backgroundState;
        await audioContextSource.updateBackgroundState(backgroundState);
        await recordingAudioContextSource.updateBackgroundState(backgroundState);
        if (backgroundState === 'Foreground' || backgroundState === 'BackgroundActive')
            await opusMediaRecorder.ensureConnected(true);
        else
            await opusMediaRecorder.disconnect();

    }
}
