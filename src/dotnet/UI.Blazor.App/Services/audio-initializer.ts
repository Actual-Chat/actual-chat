import { getLogs } from 'logging';
import { AudioPlayer } from '../Components/AudioPlayer/audio-player';
import { opusMediaRecorder } from '../Components/AudioRecorder/opus-media-recorder';
import { audioContextSource, recordingAudioContextSource } from './audio-context-source';
import { ResolvedPromise } from 'actuallab-core';

const { infoLog, warnLog } = getLogs('AudioInfo');

export type BackgroundActivityState = 'Foreground' | 'BackgroundIdle' | 'BackgroundActive';

export class AudioInitializer {
    public static backgroundActivityState: BackgroundActivityState = 'Foreground';
    public static isRecorderInitialized = false;
    public static isPlayerInitialized = false;

    /** Called by Blazor */
    public static async init(backendRef: DotNet.DotNetObject, baseUri: string, canUseNNVad: boolean): Promise<void> {
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
    public static async setBackgroundActivityState(backgroundActivityState: BackgroundActivityState): Promise<void> {
        infoLog?.log(`setBackgroundActivityState:`, backgroundActivityState);
        this.backgroundActivityState = backgroundActivityState;
        await audioContextSource.setBackgroundActivityState(backgroundActivityState);
        await recordingAudioContextSource.setBackgroundActivityState(backgroundActivityState);
        if (backgroundActivityState === 'Foreground' || backgroundActivityState === 'BackgroundActive')
            await opusMediaRecorder.ensureConnected(true);
    }
}
