import { ObjectPool } from 'object-pool';
import { AudioPlayerController } from './audio-player-controller';
import { PlaybackState } from './worklets/feeder-audio-worklet-node';
import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'AudioPlayer';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

/**
 * A lightweight facade of the AudioPlayerBackend.
 * They are separated because of requirement of an user gesture to create audioContext.
 */
export class AudioPlayer {

    private static controllerPool = new ObjectPool<AudioPlayerController>(() => AudioPlayerController.create());

    public onStartPlaying?: () => void = null;
    public onInitialized?: () => void = null;

    private readonly id: string;
    /** How often send offset update event to the blazor, in milliseconds */
    private readonly updateOffsetMs = 200;
    private readonly blazorRef: DotNet.DotNetObject;

    private controller?: AudioPlayerController = null;
    private state: 'uninitialized' | 'initializing' | 'initialized' | 'playing' | 'paused' | 'stopped' | 'ended' = 'uninitialized';
    /**
     * We can't await init() on the blazor side, because it will cause a round-trip delay (blazor server),
     * so if data comes before initialization, we will wait this promise.
     */
    private whenInitialized?: Promise<void> = null;
    /** we can do this with state, but it's clearer */
    private isBufferFull = false;
    private updateOffsetTickIntervalId?: number = null;

    public static async create(blazorRef: DotNet.DotNetObject, id: string): Promise<AudioPlayer> {
        const controller = await this.controllerPool.get();
        const player = new AudioPlayer(controller, blazorRef, id);
        await player.init();
        return player;
    }

    public constructor(controller: AudioPlayerController, blazorRef: DotNet.DotNetObject, id: string) {
        this.blazorRef = blazorRef;
        this.controller = controller;
        this.id = id;
        debugLog?.log(`[#${this.id}/${this.controller?.id}] constructor`);
    }

    public init(): Promise<void> {
        warnLog?.assert(this.state === 'uninitialized', `[#${this.id}] init() is called more than once!`);
        this.state = 'initializing';

        this.whenInitialized = this.controller.init({
            onBufferLow: async () => {
                if (this.isBufferFull) {
                    this.isBufferFull = false;
                    debugLog?.log(`[#${this.id}/${this.controller?.id}] onBufferLow`);
                    await this.invokeOnChangeReadiness(true);
                }
            },
            onBufferTooMuch: async () => {
                if (!this.isBufferFull) {
                    this.isBufferFull = true;
                    debugLog?.log(`[#${this.id}/${this.controller?.id}] onBufferTooMuch`);
                    await this.invokeOnChangeReadiness(false);
                }
            },
            onStartPlaying: () => {
                debugLog?.log(`[#${this.id}/${this.controller?.id}] onStartPlaying`);
                if (this.state === 'playing') {
                    warnLog?.log(`[#${this.id}/${this.controller?.id}] onStartPlaying: already in playing state: ${this.state}`);
                    return;
                }
                this.state = 'playing';
                if (this.onStartPlaying !== null)
                    this.onStartPlaying();
                // eslint-disable-next-line @typescript-eslint/no-misused-promises
                this.updateOffsetTickIntervalId = self.setInterval(this.onUpdateOffsetTick, this.updateOffsetMs);
            },
            onPaused: async () => {
                debugLog?.log(`[#${this.id}/${this.controller?.id}] onPaused`);
                if (this.state !== 'playing') {
                    warnLog?.log(`[#${this.id}/${this.controller?.id}] onPaused: already in non-playing state: ${this.state}`);
                    return;
                }
                this.state = 'paused';
                //self.clearInterval(this.updateOffsetTickIntervalId);
            },
            onResumed: async () => {
                debugLog?.log(`[#${this.id}/${this.controller?.id}] onResumed`);
                if (this.state !== 'paused') {
                    warnLog?.log(`[#${this.id}/${this.controller?.id}] onResumed: already in non-paused state: ${this.state}`);
                    return;
                }
                this.state = 'playing';
                // // eslint-disable-next-line @typescript-eslint/no-misused-promises
                // this.updateOffsetTickIntervalId = self.setInterval(this.onUpdateOffsetTick, this.updateOffsetMs);
            },
            onStopped: async () => {
                debugLog?.log(`[#${this.id}/${this.controller?.id}] onStopped`);
                if (this.state === 'stopped')
                    warnLog?.log(`[#${this.id}/${this.controller?.id}] onStopped: already in stopped state: ${this.state}`);
                // after stop we should notify about the real playback time
                await this.onUpdateOffsetTick();
                self.clearInterval(this.updateOffsetTickIntervalId);
                this.state = 'stopped';
            },
            onStarving: async () => {
                warnLog?.log(`[#${this.id}/${this.controller?.id}] onStarving`);
                if (this.isBufferFull) {
                    this.isBufferFull = false;
                    debugLog?.log(`[#${this.id}/${this.controller?.id}] onStarving: buffer is full`);
                    await this.invokeOnChangeReadiness(true);
                }
            },
            onEnded: async () => {
                debugLog?.log(`[#${this.id}/${this.controller?.id}] onEnded`);
                this.state = 'ended';
                const { controller } = this;
                if (controller !== null) {
                    debugLog?.log(`[#${this.id}/${this.controller?.id}] onEnded: releasing controller to the pool`);
                    this.controller = null;
                    await Promise.all([AudioPlayer.controllerPool.release(controller), this.invokeOnPlayEnded()]);
                }
            }
        }).then(() => {
            if (this.onInitialized !== null) {
                this.onInitialized();
            }
            this.state = 'initialized';
        });
        return this.whenInitialized;
    }

    /** Called by Blazor without awaiting the result, so a call can be in the middle of appendAudio  */
    public async data(bytes: Uint8Array): Promise<void> {
        warnLog?.assert(this.controller !== null, `[#${this.id}] no controller!`);
        warnLog?.assert(this.whenInitialized !== null, `[#${this.id}/${this.controller?.id}] init() wasn't called!`);
        await this.whenInitialized;
        this.controller.enqueue(bytes);
    }

    public async end(): Promise<void> {
        debugLog?.log(`[#${this.id}/${this.controller?.id}] end`);
        warnLog?.assert(this.controller !== null, `[#${this.id}] no controller!`);
        warnLog?.assert(this.whenInitialized !== null, `[#${this.id}/${this.controller?.id}] init() wasn't called!`);
        await this.whenInitialized;
        this.controller.enqueueEnd();
    }

    public async stop(): Promise<void> {
        debugLog?.log(`[#${this.id}/${this.controller?.id}] end, state: ${this.state}`);
        // blazor can call stop() between create() and init() calls (if cancelled by user/server)
        if (this.whenInitialized !== null) {
            await this.whenInitialized;
            warnLog?.assert(this.controller !== null, `[#${this.id}] no controller!`);
            debugLog?.log(`[#${this.id}/${this.controller?.id}] end: calling controller.stop(), state: ${this.state}`);
            this.controller.stop();
        }
    }

    public async pause(): Promise<void> {
        debugLog?.log(`[#${this.id}/${this.controller?.id}] pause, state: ${this.state}`);
        // blazor can call stop() between create() and init() calls (if cancelled by user/server)
        if (this.whenInitialized !== null) {
            await this.whenInitialized;
            warnLog?.assert(this.controller !== null, `[#${this.id}] no controller!`);
            debugLog?.log(`[#${this.id}/${this.controller?.id}] end: calling controller.pause(), state: ${this.state}`);
            this.controller.pause();
            await this.onUpdatePause();
        }
    }

    public async resume(): Promise<void> {
        debugLog?.log(`[#${this.id}/${this.controller?.id}] resume, state: ${this.state}`);
        // blazor can call stop() between create() and init() calls (if cancelled by user/server)
        if (this.whenInitialized !== null) {
            await this.whenInitialized;
            warnLog?.assert(this.controller !== null, `[#${this.id}] no controller!`);
            debugLog?.log(`[#${this.id}/${this.controller?.id}] end: calling controller.resume(), state: ${this.state}`);
            this.controller.resume();
        }
    }

    private onUpdateOffsetTick = async () => {
        try {
            const { state, controller } = this;
            if (state === 'playing' && controller !== null) {
                const state: PlaybackState = await controller.getState();
                debugLog?.log(
                    `[#${this.id}/${this.controller?.id}] onUpdateOffsetTick:`,
                        `playbackTime:`, state.playbackTime,
                        `bufferedTime:`, state.bufferedTime);
                if (this.state !== 'playing')
                    return;

                await this.invokeOnPlayTimeChanged(state.playbackTime);
            }
        }
        catch (error) {
            errorLog?.log(`[#${this.id}/${this.controller?.id}] onUpdateOffsetTick: unhandled error:`, error);
        }
    };

    private onUpdatePause = async () => {
        try {
            if (this.controller !== null) {
                const state: PlaybackState = await this.controller.getState();
                debugLog?.log(
                    `[#${this.id}/${this.controller?.id}] onUpdatePause:`,
                    `playbackTime:`, state.playbackTime,
                    `bufferedTime:`, state.bufferedTime);
                await this.invokeOnPausedAt(state.playbackTime);
            }
        }
        catch (error) {
            errorLog?.log(`[#${this.id}/${this.controller?.id}] onUpdatePause: unhandled error:`, error);
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
