import { MseAudioPlayer } from './mse-audio-player';
import { AudioContextAudioPlayer } from './audio-context-audio-player';

export class AudioPlayer {
    private static _isMsePlayer = false;

    public static create(blazorRef: DotNet.DotNetObject, debugMode: boolean): object {
        return AudioPlayer._isMsePlayer
            ? MseAudioPlayer.create(blazorRef, debugMode)
            : AudioContextAudioPlayer.create(blazorRef, debugMode);
    }

    public static changePlayerImplementation(useMse: boolean) {
        this._isMsePlayer = useMse;
        console.log(`Switched player implementation to ${this._isMsePlayer ? "MSE" : "AudioContext"}`);
    }

    public static isMsePlayer(): boolean {
        return this._isMsePlayer;
    }

    public static debug?: {
        debugMode: boolean;
        debugOperations: boolean;
        debugAppendAudioCalls: boolean;
        debugDecoder: boolean;
        debugFeeder: boolean;
        debugFeederStats: boolean;
    } = null;
}