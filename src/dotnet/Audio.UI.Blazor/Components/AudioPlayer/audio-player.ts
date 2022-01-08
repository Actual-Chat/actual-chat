import { MseAudioPlayer } from './mse-audio-player';
import { AudioContextAudioPlayer } from './audio-context-audio-player';
import { IAudioPlayer } from './IAudioPlayer';

export class AudioPlayer {
    private static _isMsePlayer = false;

    public static create(playerId: string, blazorRef: DotNet.DotNetObject, debugMode: boolean): IAudioPlayer {
        return AudioPlayer._isMsePlayer
            ? MseAudioPlayer.create(blazorRef, debugMode)
            : AudioContextAudioPlayer.create(playerId, blazorRef, debugMode);
    }

    public static changePlayerImplementation(useMse: boolean) {
        this._isMsePlayer = useMse;
        console.log(`Switched player implementation to ${this._isMsePlayer ? "MSE" : "AudioContext"}`);
    }

    public static isMsePlayer(): boolean {
        return this._isMsePlayer;
    }
}
