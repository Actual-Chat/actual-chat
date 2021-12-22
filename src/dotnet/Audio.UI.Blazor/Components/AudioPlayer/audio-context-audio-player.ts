import OGVDecoderAudioOpusW from 'ogv/dist/ogv-decoder-audio-opus-wasm';
import OGVDecoderAudioOpusWWasm from 'ogv/dist/ogv-decoder-audio-opus-wasm.wasm';
import OGVDemuxerWebMW from 'ogv/dist/ogv-demuxer-webm-wasm';
import OGVDemuxerWebMWWasm from 'ogv/dist/ogv-demuxer-webm-wasm.wasm';
import AudioFeeder, { PlaybackState } from 'audio-feeder';
import { OperationQueue, Operation } from './operation-queue';
import { nextTick } from './next-tick';
import { IAudioPlayer } from './IAudioPlayer';

/** Adapter class for ogv.js player */
export class AudioContextAudioPlayer implements IAudioPlayer {

    public static debug?: {
        debugMode: boolean;
        debugOperations: boolean;
        debugAppendAudioCalls: boolean;
        debugDecoder: boolean;
        debugFeeder: boolean;
        debugFeederStats: boolean;
    } = null;

    public static sharedAudioContext: AudioContext | null = null;
    /**
     * Helps to decrease initialization latency by creation the audio context as soon as we could,
     * but browsers don't allow to create it without user interaction.
     */
    public static initSharedAudioContext() {
        self.addEventListener('onkeydown', AudioContextAudioPlayer._initEventListener);
        self.addEventListener('mousedown', AudioContextAudioPlayer._initEventListener);
        self.addEventListener('pointerdown', AudioContextAudioPlayer._initEventListener);
        self.addEventListener('pointerup', AudioContextAudioPlayer._initEventListener);
        self.addEventListener('touchstart', AudioContextAudioPlayer._initEventListener);
    }

    public static create(blazorRef: DotNet.DotNetObject, debugMode: boolean) {
        const player = new AudioContextAudioPlayer(blazorRef, debugMode);
        if (debugMode) {
            self["_player"] = player;
        }
        return player;
    }

    /** We're only allowed to have 4 contexts on many browsers and there's no way to discard them */
    private static getOrCreateSharedAudioContext(): AudioContext | null {
        if (AudioContextAudioPlayer.sharedAudioContext === null) {
            if (AudioFeeder.isSupported()) {
                // We're only allowed 4 contexts on many browsers
                // and there's no way to discard them (!)...
                const context = new AudioContext({ sampleRate: 48000 });

                let node: ScriptProcessorNode | null = null;
                if (context.createScriptProcessor) {
                    node = context.createScriptProcessor(1024, 0, 2);
                } else if (context["createJavaScriptNode"]) {
                    node = context["createJavaScriptNode"](1024, 0, 2);
                } else {
                    throw new Error("Bad version of web audio API?");
                }

                // Don't actually run any audio, just start & stop the node
                node.connect(context.destination);
                node.disconnect();

                AudioContextAudioPlayer.sharedAudioContext = context;
            }
        }
        return AudioContextAudioPlayer.sharedAudioContext;
    }
    private static _initEventListener = () => {
        AudioContextAudioPlayer.removeInitListeners();
        AudioContextAudioPlayer.getOrCreateSharedAudioContext();
    };

    private static removeInitListeners() {
        self.removeEventListener('onkeydown', AudioContextAudioPlayer._initEventListener);
        self.removeEventListener('mousedown', AudioContextAudioPlayer._initEventListener);
        self.removeEventListener('pointerdown', AudioContextAudioPlayer._initEventListener);
        self.removeEventListener('pointerup', AudioContextAudioPlayer._initEventListener);
        self.removeEventListener('touchstart', AudioContextAudioPlayer._initEventListener);
    }

    private static getEmscriptenLoaderOptions(): EmscriptenLoaderOptions {
        return {
            locateFile: (filename: string) => {
                if (filename === "ogv-demuxer-webm-wasm.wasm")
                    return OGVDemuxerWebMWWasm;
                else if (filename === "ogv-decoder-audio-opus-wasm.wasm")
                    return OGVDecoderAudioOpusWWasm;
                // Allow secondary resources like the .wasm payload to be loaded by the emscripten code.
                // emscripten 1.37.25 loads memory initializers as data: URI
                else if (filename.slice(0, 5) === 'data:')
                    return filename;
                else throw new Error(`Emscripten module tried to load an unknown file: "${filename}"`);
            }
        };
    }
    /** each time loads OGVDemuxerWebMWWasm with HTTP call, at least until it's cached by browser */
    private static createDemuxer() {
        return OGVDemuxerWebMW(AudioContextAudioPlayer.getEmscriptenLoaderOptions()) as Promise<Demuxer>;
    }

    /** each time loads OGVDecoderAudioOpusWWasm with HTTP call, at least until it's cached by browser */
    private static createDecoder() {
        return OGVDecoderAudioOpusW(AudioContextAudioPlayer.getEmscriptenLoaderOptions()) as Promise<Decoder>;
    }

    public onStartPlaying?: () => void = null;
    public onInitialized?: () => void = null;

    /** How much seconds do we have in the buffer before we tell to blazor that we have enough data */
    private readonly _bufferTooMuchThreshold = 20.0;
    /**
     * How much seconds do we have in the buffer before we can start to play (from the start or after starving),
     * should be in sync with audio-feeder bufferSize
     */
    private readonly _bufferEnoughThreshold = 0.170;

    /** How many milliseconds can we block the main thread for processing */
    private readonly _processingThresholdMs = 10;
    /**
     * How much seconds do we have in the buffer before unblocking the queue,
     * must be less than _bufferTooMuchThreshold
     */
    private readonly _bufferUnblockThreshold = this._bufferTooMuchThreshold - 5;
    /** How often send offset update event to the blazor, in milliseconds */
    private readonly _updateOffsetMs = 200;

    private readonly _blazorRef: DotNet.DotNetObject;
    private readonly _debugMode: boolean;
    private readonly _debugOperations: boolean;
    private readonly _debugAppendAudioCalls: boolean;
    private readonly _debugDecoder: boolean;
    private readonly _debugFeeder: boolean;
    private readonly _debugFeederStats: boolean;

    private _demuxer?: Demuxer;
    private readonly _demuxerReady: Promise<Demuxer>;
    private _decoder?: Decoder;
    private readonly _decoderReady: Promise<Decoder>;
    private _audioContext: AudioContext;
    private readonly _feeder?: AudioFeeder;
    private _queue: OperationQueue;
    private _nextProcessingTickTimer: number | null;
    private _isProcessing: boolean;
    private _isAppending: boolean;
    private _isPlaying: boolean;
    private _isDisposed: boolean;
    private _isEndOfStreamReached: boolean;
    private _operationSequenceNumber: number;

    private _unblockQueue?: () => void;

    constructor(blazorRef: DotNet.DotNetObject, debugMode: boolean) {
        this._blazorRef = blazorRef;
        const debugOverride = AudioContextAudioPlayer.debug;
        if (debugOverride === null || debugOverride === undefined) {
            this._debugMode = debugMode;
            this._debugAppendAudioCalls = debugMode && false;
            this._debugOperations = debugMode && false;
            this._debugDecoder = debugMode && false;
            this._debugFeeder = debugMode && true;
            this._debugFeederStats = this._debugFeeder && true;
        }
        else {
            this._debugMode = debugOverride.debugMode;
            this._debugAppendAudioCalls = debugOverride.debugAppendAudioCalls;
            this._debugOperations = debugOverride.debugOperations;
            this._debugDecoder = debugOverride.debugDecoder;
            this._debugFeeder = debugOverride.debugFeeder;
            this._debugFeederStats = debugOverride.debugFeederStats;
        }

        this._demuxer = null;
        this._isProcessing = false;
        this._isDisposed = false;
        this._isPlaying = false;
        this._isAppending = false;
        this._isEndOfStreamReached = false;
        this._operationSequenceNumber = 0;
        this._decoder = null;
        this._feeder = null;
        this._unblockQueue = null;
        this._queue = new OperationQueue(this._debugOperations);
        this._nextProcessingTickTimer = null;
        this._demuxerReady = AudioContextAudioPlayer.createDemuxer()
            .then(demuxer => new Promise<Demuxer>(resolve => demuxer.init(() => {
                this._demuxer = demuxer;
                resolve(this._demuxer);
            })));

        this._decoderReady = AudioContextAudioPlayer.createDecoder()
            .then(decoder => new Promise<Decoder>(resolve => decoder.init(() => {
                this._decoder = decoder;
                resolve(this._decoder);
            })));
        this._audioContext = AudioContextAudioPlayer.getOrCreateSharedAudioContext();
        if (this._audioContext === null) {
            this._audioContext = new AudioContext({ sampleRate: 48000 });
        }
        if (this._audioContext.state === "suspended") {
            const _ = this._audioContext.resume();
        }
        this._feeder = new AudioFeeder({
            // we have 960 samples in opus frame (if it was recorded by our wasm recorder)
            // bufferSize must be power of 2, so 256, 512 (10ms-20ms), 1024 (21ms+) , 2048 (42ms+),
            // 4096(85ms), 8192(170ms), 16384(341ms). You can read about it at the webaudio spec
            // https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createscriptprocessor-buffersize-numberofinputchannels-numberofoutputchannels-buffersize
            // so we can block js main thread max up to 85ms without glitches
            bufferSize: 4096,
            audioContext: this._audioContext,
        });
        this._feeder.onbufferlow = () => this.unblockQueue('onbufferlow');
        this._feeder.onstarved = () => {
            if (this._isPlaying && this._isEndOfStreamReached) {
                this._feeder.onstarved = null;
                if (debugMode)
                    this.log(`audio ended.`);
                const _ = this.onUpdateOffsetTick();
                this.dispose();
                const __ = this.invokeOnPlaybackEnded();
                return;
            }
            this.unblockQueue('onstarved');
        };

    }

    public async initialize(byteArray: Uint8Array): Promise<void> {
        if (this._debugMode)
            this.log(`initialize(header: ${byteArray.length} bytes)`);
        try {
            this._feeder.init(1, 48000);
            this._feeder.bufferThreshold = this._bufferUnblockThreshold;
            if (this._demuxer === null) {
                if (this._debugMode)
                    this.log("initialize: awaiting creation of demuxer");
                await this._demuxerReady;
                if (this._debugMode)
                    this.log("initialize: header has been appended with a delay");
            }
            if (this._decoder === null) {
                if (this._debugMode)
                    this.log("initialize: awaiting creation of decoder");
                await this._decoderReady;
                if (this._debugMode)
                    this.log("initialize: decoder header has been created");
            }
            if (this._debugMode)
                this.log("initialize: awaiting creation of feeder");
            await this.feederReady();
            if (this._debugMode)
                this.log("initialize: feeder has been created");

            if (this._debugMode)
                this.log("initialize: start processing headers");
            await this.appendAudioAsync(byteArray, -1);
            if (this._debugMode)
                this.log(`initialize: done. found codec: ${this._demuxer.audioCodec}`);
            if (this.onInitialized !== null)
                this.onInitialized();
        } catch (error) {
            this.logError(`initialize: error ${error} ${error.stack}`);
        }
    }

    private feederReady(): Promise<void> {
        return new Promise<void>(resolve => this._feeder.waitUntilReady(resolve));
    }

    private get _isMetadataLoaded(): boolean {
        const { _decoder, _demuxer } = this;
        return _demuxer.loadedMetadata !== null
            && _demuxer.loadedMetadata !== false
            && _demuxer.audioCodec !== undefined
            && _demuxer.audioCodec !== null
            && _decoder.audioFormat !== null
            && _decoder.audioFormat !== undefined;
    }

    /** Called by Blazor without awaiting the result, so a call can be in the middle of appendAudio  */
    public appendAudioAsync(byteArray: Uint8Array, offset: number): Promise<void> {
        if (this._isAppending) {
            this.logError("Append called in wrong order");
        }
        this._isAppending = true;
        try {
            const { _decoder, _demuxer } = this;
            if (_decoder === null || _demuxer === null) {
                this.logError("called appendAudio on disposed object");
                return;
            }
            const operationSequenceNumber = this._operationSequenceNumber++;
            const operation: Operation = {
                execute: async () => {
                    try {
                        if (this._debugAppendAudioCalls) {
                            this.log(`appendAudio(size: ${byteArray.length}, `
                                + `offset: ${offset}) `
                                + `isMetadataLoaded: ${this._isMetadataLoaded}`);
                        }
                        await this.demuxEnqueue(byteArray);
                        while (await this.demuxProcess()) {
                            while (_demuxer.audioPackets.length > 0) {

                                const { packet, padding } = await this.demuxDequeue();
                                if (!this._isMetadataLoaded) {
                                    await this.decodeHeaderProcess(packet);
                                    // skip the first header packet without samples
                                    if (offset < 0)
                                        continue;
                                }
                                const samples = await this.decodeProcess(packet);
                                if (this._debugDecoder) {
                                    if (samples !== null && samples.length > 0) {
                                        this.log(`decodeProcess(${packet.byteLength} bytes, padding:${padding}) `
                                            + `returned ${samples[0].byteLength} `
                                            + `bytes / ${samples[0].length} samples, `
                                            + `isMetadataLoaded: ${this._isMetadataLoaded}`);
                                    }
                                    else {
                                        this.log(`decodeProcess(${packet.byteLength} bytes, padding: ${padding}) ` +
                                            "returned null");
                                    }
                                }
                                if (samples === null)
                                    continue;
                                this._feeder.bufferData(samples);
                                const playbackState = this._feeder.getPlaybackState();
                                const durationBuffered = playbackState.samplesQueued / this._feeder.targetRate;
                                const bufferedSpan = durationBuffered - playbackState.playbackPosition;

                                if (this._debugFeederStats) {
                                    this.log("Feeder stats [appendAudio]: "
                                        + `playbackPosition: ${playbackState.playbackPosition}, `
                                        + `durationBuffered: ${durationBuffered}, `
                                        + `bufferedSpan: ${bufferedSpan}, `
                                        + `playbackState: ${JSON.stringify(playbackState)} `);
                                }
                                if (bufferedSpan >= this._bufferEnoughThreshold) {
                                    if (!this._isPlaying) {
                                        if (this.onStartPlaying !== null)
                                            this.onStartPlaying();
                                        this._feeder.start();
                                        this._isPlaying = true;
                                        self.setTimeout(this.onUpdateOffsetTick, this._updateOffsetMs);
                                        if (this._debugFeeder) {
                                            this.log("Feeder start playing, "
                                                + `bufferDuration: ${this._feeder.durationBuffered}, `
                                                + `playbackState: ${JSON.stringify(this._feeder.getPlaybackState())}`);
                                        }
                                    }
                                }
                                // we buffered enough data, tell to blazor about it and block the operation queue
                                if (bufferedSpan >= this._bufferTooMuchThreshold) {
                                    await this.invokeOnChangeReadiness(false, playbackState.playbackPosition, 4);
                                    const blocker = new Promise<void>(resolve => this._unblockQueue = resolve);
                                    this._queue.prepend({
                                        execute: () => blocker,
                                        onSuccess: () => {
                                            if (this._debugOperations)
                                                this.logWarn("End blocking operation queue");
                                        },
                                        onStart: () => {
                                            if (this._debugOperations) {
                                                this.logWarn("Start blocking operation queue, "
                                                    + `bufferedSpan: ${bufferedSpan}`);
                                            }
                                        },
                                        onError: _ => { }
                                    });
                                }
                            }
                        }
                    } catch (error) {
                        this.logError(`appendAudio: error ${error} ${error.stack}`);
                        throw error;
                    }
                },
                onSuccess: () => {
                    if (this._debugOperations && this._debugAppendAudioCalls)
                        this.log(`End appendAudio operation #${operationSequenceNumber}`);
                },
                onStart: () => {
                    if (this._debugOperations && this._debugAppendAudioCalls)
                        this.log(`Start appendAudio operation #${operationSequenceNumber}`);
                },
                onError: _ => { }
            };
            this._queue.append(operation);
            const _ = this.onProcessingTick();
            return Promise.resolve();
        }
        finally {
            this._isAppending = false;
        }
    }

    private onUpdateOffsetTick = async () => {
        const feeder = this._feeder;
        if (feeder === null || this._isDisposed)
            return;
        let state: PlaybackState | null = null;
        try {
            state = feeder.getPlaybackState();
        }
        catch { /* feeder.getPlaybackState can try to read properties of undefined */ }
        if (state === null || state.playbackPosition === null)
            return;
        await this.invokeOnPlaybackTimeChanged(state.playbackPosition);
        if (this._isPlaying) {
            self.setTimeout(this.onUpdateOffsetTick, this._updateOffsetMs);
        }
    };

    private onProcessingTick = async () => {
        if (this._isProcessing) {
            this._nextProcessingTickTimer = self.setTimeout(this.onProcessingTick, 5);
            return;
        }
        this._isProcessing = true;
        try {
            if (this._nextProcessingTickTimer !== null) {
                clearTimeout(this._nextProcessingTickTimer);
                this._nextProcessingTickTimer = null;
            }
            const start = new Date().getTime();
            let hasMore: boolean = await this._queue.executeNext();
            const threshold = this._processingThresholdMs;
            while (hasMore) {
                const elapsed = new Date().getTime() - start;
                if (elapsed > threshold && hasMore) {
                    // let's give a chance to process browser events
                    if (this._debugOperations)
                        this.log(`Planning processing at the next tick, because we were working for ${elapsed} ms`);
                    nextTick(this.onProcessingTick);
                    break;
                }
                hasMore = await this._queue.executeNext();
            }
        }
        finally {
            this._isProcessing = false;
        }
    };

    public endOfStream(): void {
        if (this._debugMode) {
            this.log("endOfStream()");
        }
        this._queue.append({
            execute: (): Promise<void> => {
                if (this._debugMode)
                    this.log("endOfStream operation is reached");
                this._isEndOfStreamReached = true;
                return new Promise<void>(resolve => this._demuxer.flush(resolve));
            },
            onSuccess: () => {
                if (this._debugOperations)
                    this.log("End endOfStream operation");
            },
            onStart: () => {
                if (this._debugOperations)
                    this.log("Start endOfStream operation");
            },
            onError: _ => { }
        });
        const _ = this.onProcessingTick();

    }

    public stop(error: EndOfStreamError | null = null) {
        if (this._debugMode)
            this.log(`stop(error:${error})`);
        this._isPlaying = false;
        if (this._debugMode)
            this.log("Enqueue 'Abort' operation.");
        this._queue.prepend({
            execute: () => {
                if (this._feeder !== null) {
                    try {
                        this._feeder.muted = true;
                        this._feeder.stop();
                        this._feeder.flush();
                    }
                    catch { /* feeder._tempoChanger.flush can throw */ }
                }
                this._queue.clear();
                if (this._demuxer != null) {
                    return new Promise<void>(resolve => {
                        this._demuxer.flush(() => {
                            if (this._feeder !== null)
                                this._feeder.muted = false;
                            resolve();
                        });
                    });
                }
                if (this._feeder !== null)
                    this._feeder.muted = false;
            },
            onSuccess: () => {
                if (this._debugMode)
                    this.log("Aborted.");
            },
            onStart: () => { },
            onError: error => {
                if (this._debugMode)
                    this.logWarn(`Can't stop playing. Error: ${error.message}, ${error.stack}`);
            }
        });
        const _ = this.onProcessingTick();
    }

    public dispose(): void {
        if (this._isDisposed)
            return;
        this._isDisposed = true;
        if (this._debugMode)
            this.logWarn(`dispose()`);

        this.stop();
        this._demuxer?.flush(() => { this._demuxer?.close(); this._demuxer = null; });
        this._decoder?.close();
        this._decoder = null;
        this._feeder.flush();
        this._feeder.close();
        this._isPlaying = false;
        if (this._audioContext !== AudioContextAudioPlayer.sharedAudioContext) {
            this._audioContext.suspend()
                .then(() => this._audioContext.close().then(() => { this._audioContext = null; }));
        }
    }

    private demuxEnqueue(buffer: ArrayBuffer): Promise<void> {
        const demuxer = this._demuxer;
        if (demuxer === null)
            return Promise.reject("Demuxer is disposed");
        return new Promise(resolve => demuxer.receiveInput(buffer, resolve));
    }

    private demuxDequeue(): Promise<{ packet: ArrayBuffer; padding: number; }> {
        const demuxer = this._demuxer;
        if (demuxer === null)
            return Promise.reject("Demuxer is disposed");

        return new Promise<{ packet: ArrayBuffer; padding: number; }>(resolve =>
            demuxer.dequeueAudioPacket((packet, padding) => resolve({ packet, padding })));
    }

    private demuxProcess(): Promise<boolean> {
        const demuxer = this._demuxer;
        if (demuxer === null)
            return Promise.reject("Demuxer is disposed");
        if (demuxer.processing === true)
            return Promise.reject("Demuxer is processing");
        return new Promise<boolean>(resolve => demuxer.process(more => resolve(more)));
    }

    private decodeHeaderProcess(packet: ArrayBuffer): Promise<void> {
        const decoder = this._decoder;
        if (decoder === null)
            return Promise.reject("Decoder is disposed");
        if (decoder.processing === true)
            return Promise.reject("Decoder is processing");
        return new Promise<void>(resolve => decoder.processHeader(packet, _ => resolve()));
    }

    private decodeProcess(packet: ArrayBuffer): Promise<Float32Array[] | null> {
        const decoder = this._decoder;
        if (decoder === null)
            return Promise.reject("Decoder is disposed");
        if (decoder.processing === true)
            return Promise.reject("Decoder is processing");
        return new Promise<Float32Array[] | null>((resolve, reject) => {
            decoder.processAudio(packet, _ => {
                if (decoder.audioBuffer !== null && decoder.audioBuffer.length > 0)
                    resolve(decoder.audioBuffer);
                else
                    reject("Can't decode packet to the right format");
            });
        });
    }

    private unblockQueue(source: string) {
        const unblock = this._unblockQueue;
        this._unblockQueue = null;
        if (unblock !== null) {
            if (this._debugOperations)
                this.logWarn(`[${source}]: Unblocking queue`);
            unblock();
            const _ = this.invokeOnChangeReadiness(true, this._feeder.playbackPosition, 2);
        }
    }

    private invokeOnPlaybackTimeChanged(time: number): Promise<void> {
        return this._blazorRef.invokeMethodAsync("OnPlaybackTimeChanged", time);
    }

    private invokeOnPlaybackEnded(code: number | null = null, message: string | null = null): Promise<void> {
        return this._blazorRef.invokeMethodAsync("OnPlaybackEnded", code, message);
    }

    private invokeOnChangeReadiness(isBufferReady: boolean, time: number, readyState: number): Promise<void> {
        return this._blazorRef.invokeMethodAsync("OnChangeReadiness", isBufferReady, time, readyState);
    }

    private log(message: string) {
        console.debug(`AudioContextAudioPlayer: ${message}`);
    }

    private logWarn(message: string) {
        console.warn(`AudioContextAudioPlayer: ${message}`);
    }

    private logError(message: string) {
        console.error(`AudioContextAudioPlayer: ${message}`);
    }
}

AudioContextAudioPlayer.initSharedAudioContext();
