import { ObjectPool } from 'object-pool';
import { AudioPlayerController } from './audio-player-controller';
import { PlaybackState } from './worklets/feeder-audio-worklet-node';

/**
 * A lightweight facade of the AudioPlayerBackend.
 * They are separated because of requirement of an user gesture to create audioContext.
 */
export class AudioPlayer {

    private static controllerPool = new ObjectPool<AudioPlayerController>(() => AudioPlayerController.create());
    public static debug?: boolean = null;

    public onStartPlaying?: () => void = null;
    public onInitialized?: () => void = null;

    private readonly debug: boolean;
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
    private initPromise?: Promise<void> = null;
    /** we can do this with state, but it's clearer */
    private isBufferFull = false;
    private updateOffsetTickIntervalId?: number = null;


    public static async create(blazorRef: DotNet.DotNetObject, debug: boolean, id: string): Promise<AudioPlayer> {
        const controller = await this.controllerPool.get();
        if (AudioPlayer.debug)
            console.debug(`Created player with controllerId:${controller.id}`);
        const player = new AudioPlayer(controller, blazorRef, debug, id);
        await player.init();
        return player;
    }

    public constructor(controller: AudioPlayerController, blazorRef: DotNet.DotNetObject, debug: boolean, id: string) {
        this.blazorRef = blazorRef;
        this.debug = AudioPlayer.debug === null ? debug : AudioPlayer.debug;
        this.controller = controller;
        this.id = id;
    }

    public init(): Promise<void> {
        const { debug, state } = this;
        this.state = 'initializing';
        console.assert(state === 'uninitialized', `[AudioTrackPlayer #${this.id}] init: called in a wrong order`);

        this.initPromise = this.controller.init({
            onBufferLow: async () => {
                if (this.isBufferFull) {
                    this.isBufferFull = false;
                    if (debug)
                        console.debug(`[AudioTrackPlayer #${this.id}] onBufferLow, controllerId:${this.controller.id}`);
                    await this.invokeOnChangeReadiness(true);
                }
            },
            onBufferTooMuch: async () => {
                if (!this.isBufferFull) {
                    this.isBufferFull = true;
                    if (debug)
                        console.debug(`[AudioTrackPlayer #${this.id}] onBufferTooMuch, controllerId:${this.controller.id}`);
                    await this.invokeOnChangeReadiness(false);
                }
            },
            onStartPlaying: () => {
                if (debug)
                    console.debug(`[AudioTrackPlayer #${this.id}] onStartPlaying, controllerId:${this.controller.id}`);
                if (this.state === 'playing'){
                    console.warn("Unexpected onStartPlaying since audio player is already in playing state");
                    return;
                }
                this.state = 'playing';
                if (this.onStartPlaying !== null)
                    this.onStartPlaying();
                // eslint-disable-next-line @typescript-eslint/no-misused-promises
                this.updateOffsetTickIntervalId = self.setInterval(this.onUpdateOffsetTick, this.updateOffsetMs);
            },
            onPaused: async () => {
                if (debug)
                    console.debug(`[AudioTrackPlayer #${this.id}] onPaused, controllerId:${this.controller.id}`);
                if (this.state !== 'playing'){
                    console.warn(`Unexpected onPaused since audio player is not in playing state. State: ${this.state}`);
                    return;
                }
                this.state = 'paused';
                //self.clearInterval(this.updateOffsetTickIntervalId);
            },
            onResumed: async () => {
                if (debug)
                    console.debug(`[AudioTrackPlayer #${this.id}] onResumed, controllerId:${this.controller.id}`);
                if (this.state !== 'paused'){
                    console.warn(`Unexpected onPaused since audio player is not in playing state. State: ${this.state}`);
                    return;
                }
                this.state = 'playing';
                // // eslint-disable-next-line @typescript-eslint/no-misused-promises
                // this.updateOffsetTickIntervalId = self.setInterval(this.onUpdateOffsetTick, this.updateOffsetMs);
            },
            onStopped: async () => {
                if (debug)
                    console.debug(`[AudioTrackPlayer #${this.id}] onStopped, controllerId:${this.controller.id}`);
                if (this.state === 'stopped')
                    console.error('Unexpected onStopped since audio player is already in stopped state')
                // after stop we should notify about the real playback time
                await this.onUpdateOffsetTick();
                self.clearInterval(this.updateOffsetTickIntervalId);
                this.state = 'stopped';
            },
            onStarving: async () => {
                if (debug)
                    console.warn(`[AudioTrackPlayer #${this.id}] onStarving, controllerId:${this.controller.id}`);
                if (this.isBufferFull) {
                    this.isBufferFull = false;
                    if (debug)
                        console.debug(`[AudioTrackPlayer #${this.id}] onStarving, controllerId:${this.controller.id}`);
                    await this.invokeOnChangeReadiness(true);
                }
            },
            onEnded: async () => {
                this.state = 'ended';
                const { controller } = this;
                if (controller !== null) {
                    this.controller = null;
                    if (debug)
                        console.debug(`[AudioTrackPlayer #${this.id}] onEnded, release controllerId:${controller.id} to the pool`);
                    await Promise.all([AudioPlayer.controllerPool.release(controller), this.invokeOnPlayEnded()]);
                }
            }
        }).then(() => {
            if (this.onInitialized !== null) {
                this.onInitialized();
            }
            this.state = 'initialized';
        });
        return this.initPromise;
    }

    /** Called by Blazor without awaiting the result, so a call can be in the middle of appendAudio  */
    public async data(bytes: Uint8Array): Promise<void> {
        console.assert(this.controller !== null, `[AudioTrackPlayer #${this.id}] Controller must be presented. Lifetime error.`);
        console.assert(this.initPromise !== null, `[AudioTrackPlayer #${this.id}] Player isn't initialized. controllerId:${this.controller.id}.`);
        await this.initPromise;
        this.controller.enqueue(bytes);
    }

    public async end(): Promise<void> {
        if (this.debug)
            console.debug(`[AudioTrackPlayer #${this.id}] Got from blazor 'end()' controllerId:${this.controller.id}`);
        console.assert(this.controller !== null, `[AudioTrackPlayer #${this.id}] Controller must be presented. Lifetime error.`);
        console.assert(this.initPromise !== null, `[AudioTrackPlayer #${this.id}] Player isn't initialized. controllerId:${this.controller.id}.`);
        await this.initPromise;
        this.controller.enqueueEnd();
    }

    public async stop(): Promise<void> {
        if (this.debug)
            console.debug(`[AudioTrackPlayer #${this.id}] Got from blazor: stop(). controllerId:${this.controller.id} state:${this.state}`);
        // blazor can call stop() between create() and init() calls (if cancelled by user/server)
        if (this.initPromise !== null) {
            await this.initPromise;
            console.assert(this.controller !== null, `[AudioTrackPlayer #${this.id}] Controller must be created. Lifetime error.`);
            if (this.debug)
                console.debug(`[AudioTrackPlayer #${this.id}] Call controller stop(). controllerId:${this.controller.id} state:${this.state}`);
            this.controller.stop();
        }
    }

    public async pause(): Promise<void> {
        if (this.debug)
            console.debug(`[AudioTrackPlayer #${this.id}] Got from blazor: pause(). controllerId:${this.controller.id} state:${this.state}`);
        // blazor can call stop() between create() and init() calls (if cancelled by user/server)
        if (this.initPromise !== null) {
            await this.initPromise;
            console.assert(this.controller !== null, `[AudioTrackPlayer #${this.id}] Controller must be created. Lifetime error.`);
            if (this.debug)
                console.debug(`[AudioTrackPlayer #${this.id}] Call controller pause(). controllerId:${this.controller.id} state:${this.state}`);
            this.controller.pause();
            await this.onUpdatePause();
        }
    }

    public async resume(): Promise<void> {
        if (this.debug)
            console.debug(`[AudioTrackPlayer #${this.id}] Got from blazor: resume(). controllerId:${this.controller.id} state:${this.state}`);
        // blazor can call stop() between create() and init() calls (if cancelled by user/server)
        if (this.initPromise !== null) {
            await this.initPromise;
            console.assert(this.controller !== null, `[AudioTrackPlayer #${this.id}] Controller must be created. Lifetime error.`);
            if (this.debug)
                console.debug(`[AudioTrackPlayer #${this.id}] Call controller resume(). controllerId:${this.controller.id} state:${this.state}`);
            this.controller.resume();
        }
    }

    private onUpdateOffsetTick = async () => {
        try {
            const { state, controller, debug } = this;
            if (state === 'playing' && controller !== null) {
                const state: PlaybackState = await controller.getState();
                if (debug) {
                    console.debug(`[AudioTrackPlayer #${this.id}] onUpdateOffsetTick(controllerId:${controller.id}): ` +
                        `playbackTime = ${state.playbackTime}, bufferedTime = ${state.bufferedTime}`);
                }
                if (this.state !== 'playing')
                    return;

                await this.invokeOnPlayTimeChanged(state.playbackTime);
            }
        }
        catch (error) {
            console.error(`[AudioTrackPlayer #${this.id}] Unhandled error`, error);
        }
    };

    private onUpdatePause = async () => {
        try {
            const { controller, debug } = this;
            if (controller !== null) {
                const state: PlaybackState = await controller.getState();
                if (debug) {
                    console.debug(`[AudioTrackPlayer #${this.id}] onUpdatePause(controllerId:${controller.id}): ` +
                                      `playbackTime = ${state.playbackTime}, bufferedTime = ${state.bufferedTime}`);
                }
                await this.invokeOnPausedAt(state.playbackTime);
            }
        }
        catch (error) {
            console.error(`[AudioTrackPlayer #${this.id}] Unhandled error`, error);
        }
    }

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
