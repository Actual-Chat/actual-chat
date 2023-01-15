import { AudioPlayer } from '../../Components/AudioPlayer/audio-player';

export class AudioPlayerTestPage {
    private stats = {
        constructorStartTime: 0,
        constructorEndTime: 0,
        playingStartTime: 0,
    };

    public static blockMainThread(milliseconds: number) {
        console.warn(`Blocking main thread for ${milliseconds}`);
        const start = new Date().getTime();
        // eslint-disable-next-line no-constant-condition
        while (true) {
            if (new Date().getTime() - start > milliseconds) {
                break;
            }
        }
        console.warn('Main thread unblocked');
    }

    public static async create(blazorRef: DotNet.DotNetObject): Promise<AudioPlayerTestPage> {
        const player = await AudioPlayer.create(blazorRef, '0');
        return new AudioPlayerTestPage(blazorRef, player);
    }

    private readonly player: AudioPlayer;

    constructor(blazorRef: DotNet.DotNetObject, player: AudioPlayer) {
        this.stats.constructorStartTime = new Date().getTime();
        this.player = player;
        this.player.onStartPlaying = () => {
            this.stats.playingStartTime = new Date().getTime();
            console.warn('onStartPlaying, stats:', this.stats);
            void blazorRef.invokeMethodAsync('OnStartPlaying', this.getStats());
        };
        this.stats.constructorEndTime = new Date().getTime();
        this.stats.playingStartTime = 0;
    }

    public getStats() {
        return this.stats;
    }

    public data(byteArray: Uint8Array): Promise<void> {
        return this.player.data(byteArray);
    }

    public end(): Promise<void> {
        return this.player.end();
    }
    public stop(): Promise<void> {
        return this.player.stop();
    }
    public pause(): Promise<void> {
        return this.player.pause();
    }
    public resume(): Promise<void> {
        return this.player.resume();
    }
}
