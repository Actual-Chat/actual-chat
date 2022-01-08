import { IAudioPlayer } from "../../Components/AudioPlayer/IAudioPlayer";
import { AudioContextAudioPlayer } from "../../Components/AudioPlayer/audio-context-audio-player";
import { MseAudioPlayer } from "../../Components/AudioPlayer/mse-audio-player";


export class AudioPlayerTestPage implements IAudioPlayer {
    private _stats = {
        constructorStartTime: 0,
        constructorEndTime: 0,
        initializeStartTime: 0,
        initializeEndTime: 0,
        playingStartTime: 0,
    };

    public static blockMainThread(milliseconds: number) {
        console.warn(`Block main thread for ${milliseconds}`);
        const start = new Date().getTime();
        while (true) {
            if (new Date().getTime() - start > milliseconds) {
                break;
            }
        }
        console.warn("Unblock main thread");
    }

    public static create(isMsePlayer: boolean, blazorRef: DotNet.DotNetObject) {
        return new AudioPlayerTestPage(isMsePlayer, blazorRef);
    }

    private readonly player: IAudioPlayer;
    private isFirstAppendAudioCall: boolean = true;

    constructor(isMsePlayer: boolean, blazorRef: DotNet.DotNetObject) {
        this._stats.constructorStartTime = new Date().getTime();
        this.player = isMsePlayer
            ? new MseAudioPlayer(blazorRef, true)
            : new AudioContextAudioPlayer('test-page-player', blazorRef, true);
        this.player.onInitialized = () => this._stats.initializeEndTime = new Date().getTime();
        this.player.onStartPlaying = () => {
            this._stats.playingStartTime = new Date().getTime();
            console.warn("onStartPlaying called", this._stats);
            const _ = blazorRef.invokeMethodAsync("OnStartPlaying", this.getStats());
        };
        this._stats.constructorEndTime = new Date().getTime();
    }

    public getStats() {
        return this._stats;
    }

    public get onStartPlaying(): () => void | null {
        return this.player.onStartPlaying;
    }

    public get onInitialized(): () => void | null {
        return this.player.onInitialized;
    }

    public set onStartPlaying(value: () => void | null) {
        this.player.onStartPlaying = value;
    }

    public set onInitialized(value: () => void | null) {
        this.player.onInitialized = value;
    }

    public init(byteArray: Uint8Array): Promise<void> {
        return this.player.init(byteArray);
    }

    public appendAudio(byteArray: Uint8Array, offset: number): Promise<void> {
        if (this.isFirstAppendAudioCall) {
            this._stats.initializeStartTime = new Date().getTime();
            this._stats.initializeEndTime = 0;
            this._stats.playingStartTime = 0;
            this.isFirstAppendAudioCall = false;
        }
        return this.player.appendAudio(byteArray, offset);
    }

    public endOfStream(): void {
        this.player.endOfStream();
    }
    public stop(error: EndOfStreamError | null = null): void {
        this.player.stop(error);
    }
}
