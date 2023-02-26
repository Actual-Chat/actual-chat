import { ObjectPool } from 'object-pool';
import { AudioPlayerController } from './audio-player-controller';
import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'AudioPlayer';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export class AudioPlayer {
    private static controllerPool = new ObjectPool<AudioPlayerController>(() => AudioPlayerController.create());

    private readonly id: string;
    /** How often send offset update event to the blazor, in milliseconds */
    private readonly updateOffsetMs = 200;
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly whenInitialized: Promise<void>;
    private readonly controller: AudioPlayerController;

    private state: 'uninitialized' | 'initialized' | 'playing' | 'paused' | 'stopped' | 'ended' = 'uninitialized';
    private isBufferFull = false;
    private updateOffsetTickIntervalId: number = null;

    public onStartPlaying?: () => void;

    public constructor(controller: AudioPlayerController, blazorRef: DotNet.DotNetObject, id: string) {
        this.blazorRef = blazorRef;
        this.controller = controller;
        this.id = id;
        debugLog?.log(`[#${this.id}/${this.controller.id}] constructor`);

        this.whenInitialized = this.init();
    }

    public static async create(blazorRef: DotNet.DotNetObject, id: string): Promise<AudioPlayer> {
        const controller = await this.controllerPool.get();
        return new AudioPlayer(controller, blazorRef, id);
    }

    private async init(): Promise<void> {
        warnLog?.assert(this.state === 'uninitialized', `[#${this.id}] init() is called more than once!`);

        await this.controller.use({
            onBufferLow: async () => {
                if (this.isBufferFull) {
                    this.isBufferFull = false;
                    debugLog?.log(`[#${this.id}/${this.controller.id}] onBufferLow`);
                    await this.invokeOnChangeReadiness(true);
                }
            },
            onBufferTooMuch: async () => {
                if (!this.isBufferFull) {
                    this.isBufferFull = true;
                    debugLog?.log(`[#${this.id}/${this.controller.id}] onBufferTooMuch`);
                    await this.invokeOnChangeReadiness(false);
                }
            },
            onStartPlaying: () => {
                debugLog?.log(`[#${this.id}/${this.controller.id}] onStartPlaying`);
                if (this.state === 'playing') {
                    warnLog?.log(`[#${this.id}/${this.controller.id}] onStartPlaying: already in playing state: ${this.state}`);
                    return;
                }
                this.state = 'playing';
                if (this.onStartPlaying)
                    this.onStartPlaying();
                // eslint-disable-next-line @typescript-eslint/no-misused-promises
                this.updateOffsetTickIntervalId = self.setInterval(this.onUpdateOffsetTick, this.updateOffsetMs);
            },
            onPaused: async () => {
                debugLog?.log(`[#${this.id}/${this.controller.id}] onPaused`);
                if (this.state !== 'playing') {
                    warnLog?.log(`[#${this.id}/${this.controller.id}] onPaused: already in non-playing state: ${this.state}`);
                    return;
                }
                this.state = 'paused';
                //self.clearInterval(this.updateOffsetTickIntervalId);
            },
            onResumed: async () => {
                debugLog?.log(`[#${this.id}/${this.controller.id}] onResumed`);
                if (this.state !== 'paused') {
                    warnLog?.log(`[#${this.id}/${this.controller.id}] onResumed: already in non-paused state: ${this.state}`);
                    return;
                }
                this.state = 'playing';
                // // eslint-disable-next-line @typescript-eslint/no-misused-promises
                // this.updateOffsetTickIntervalId = self.setInterval(this.onUpdateOffsetTick, this.updateOffsetMs);
            },
            onStopped: async () => {
                debugLog?.log(`[#${this.id}/${this.controller.id}] onStopped`);
                if (this.state === 'stopped')
                    warnLog?.log(`[#${this.id}/${this.controller.id}] onStopped: already in stopped state: ${this.state}`);
                // after stop we should notify about the real playback time
                await this.onUpdateOffsetTick();
                if (this.updateOffsetTickIntervalId)
                    self.clearInterval(this.updateOffsetTickIntervalId);
                this.state = 'stopped';
            },
            onStarving: async () => {
                warnLog?.log(`[#${this.id}/${this.controller.id}] onStarving`);
                if (this.isBufferFull) {
                    this.isBufferFull = false;
                    debugLog?.log(`[#${this.id}/${this.controller.id}] onStarving: buffer is full`);
                    await this.invokeOnChangeReadiness(true);
                }
            },
            onEnded: async () => {
                debugLog?.log(`[#${this.id}/${this.controller.id}] onEnded`);
                if (this.updateOffsetTickIntervalId)
                    self.clearInterval(this.updateOffsetTickIntervalId);
                this.state = 'ended';
                const controller = this.controller;
                if (controller !== null) {
                    debugLog?.log(`[#${this.id}/${this.controller.id}] onEnded: releasing controller to the pool`);
                    await Promise.all([AudioPlayer.controllerPool.release(controller), this.invokeOnPlayEnded()]);
                }
            }
        });
        this.state = 'initialized';
    }

    /** Called by Blazor without awaiting the result, so a call can be in the middle of appendAudio  */
    public async data(bytes: Uint8Array): Promise<void> {
        await this.whenInitialized;
        this.controller.decode(bytes);
    }

    public async end(): Promise<void> {
        await this.whenInitialized;
        debugLog?.log(`[#${this.id}/${this.controller.id}] end: state: ${this.state}`);
        await this.controller.end();
    }

    public async stop(): Promise<void> {
        await this.whenInitialized;
        debugLog?.log(`[#${this.id}/${this.controller.id}] stop: state: ${this.state}`);
        await this.controller.stop();
    }

    public async pause(): Promise<void> {
        await this.whenInitialized;
        debugLog?.log(`[#${this.id}/${this.controller.id}] pause: state: ${this.state}`);
        await this.controller.pause();
        await this.onUpdatePause();
    }

    public async resume(): Promise<void> {
        await this.whenInitialized;
        debugLog?.log(`[#${this.id}/${this.controller.id}] resume: state: ${this.state}`);
        await this.controller.resume();
    }

    private onUpdateOffsetTick = async () => {
        try {
            if (this.state === 'playing') {
                const state = await this.controller.getState();
                debugLog?.log(
                    `[#${this.id}/${this.controller.id}] onUpdateOffsetTick:`,
                        `playbackTime:`, state.playbackTime,
                        `bufferedTime:`, state.bufferedTime);
                if (this.state !== 'playing')
                    return;

                await this.invokeOnPlayTimeChanged(state.playbackTime);
            }
        }
        catch (error) {
            errorLog?.log(`[#${this.id}/${this.controller.id}] onUpdateOffsetTick: unhandled error:`, error);
        }
    };

    private onUpdatePause = async () => {
        try {
            const state = await this.controller.getState();
            debugLog?.log(
                `[#${this.id}/${this.controller.id}] onUpdatePause:`,
                `playbackTime:`, state.playbackTime,
                `bufferedTime:`, state.bufferedTime);
            await this.invokeOnPausedAt(state.playbackTime);
        }
        catch (error) {
            errorLog?.log(`[#${this.id}/${this.controller.id}] onUpdatePause: unhandled error:`, error);
        }
    }

    // Backend methods

    private invokeOnPlayTimeChanged(time: number): Promise<void> {
        return this.blazorRef.invokeMethodAsync('OnPlayTimeChanged', time);
    }

    private invokeOnPausedAt(time: number): Promise<void> {
        return this.blazorRef.invokeMethodAsync('OnPausedAt', time);
    }

    private invokeOnPlayEnded(message: string | null = null): Promise<void> {
        return this.blazorRef.invokeMethodAsync('OnPlayEnded', message);
    }

    private invokeOnChangeReadiness(isBufferReady: boolean): Promise<void> {
        return this.blazorRef.invokeMethodAsync('OnChangeReadiness', isBufferReady);
    }
}
