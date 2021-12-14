import OGVDecoderAudioOpusW from 'ogv/dist/ogv-decoder-audio-opus-wasm';
import OGVDecoderAudioOpusWWasm from 'ogv/dist/ogv-decoder-audio-opus-wasm.wasm';
import OGVDemuxerWebMW from 'ogv/dist/ogv-demuxer-webm-wasm';
import OGVDemuxerWebMWWasm from 'ogv/dist/ogv-demuxer-webm-wasm.wasm';
import AudioFeeder from 'audio-feeder';
import Denque from 'denque';

interface Operation {
    execute: () => Promise<void>;
    onStart: () => void;
    onSuccess: () => void;
    onError: (error: Error) => void;
}

class OperationQueue {
    private readonly _debugMode: boolean;
    private readonly _queue: Denque<Operation>;

    constructor(debugMode: boolean) {
        this._debugMode = debugMode;
        this._queue = new Denque<Operation>();
    }

    public abort() {
        if (this._debugMode)
            this.log("Enqueue 'Abort' operation.");
        this._queue.unshift({
            execute: () => { this._queue.clear(); return Promise.resolve(); },
            onSuccess: () => {
                if (this._debugMode)
                    this.log("Aborted.");
            },
            onStart: () => { },
            onError: error => {
                if (this._debugMode)
                    this.logWarn(`Can't abort operations in queue. Error: ${error.message}, ${error.stack}`);
            }
        });
    }

    public append(operation: Operation): void {
        this._queue.push(operation);
    }

    public prepend(operation: Operation): void {
        this._queue.unshift(operation);
    }

    public async executeNext(): Promise<boolean> {
        const queue = this._queue;
        if (queue.length > 0) {
            const operation: Operation = queue.shift();
            try {
                operation.onStart();
                await operation.execute();
                operation.onSuccess();
            }
            catch (error) {
                if (this._debugMode) {
                    this.logError("Unhandled exception executing the current operation. Error: " +
                        (error instanceof Error ? `${error.message}, ${error.stack}` : error));
                }
                operation.onError(error as Error);
            }
            finally {
                return this._queue.length > 0;
            }
        }
    }

    private log(message: string) {
        console.debug(`[${new Date(Date.now()).toISOString()}] OperationQueue: ${message}`);
    }
    private logWarn(message: string) {
        console.warn(`[${new Date(Date.now()).toISOString()}] OperationQueue: ${message}`);
    }
    private logError(message: string) {
        console.error(`[${new Date(Date.now()).toISOString()}] OperationQueue: ${message}`);
    }
}

enum BufferState {
    Starving = -1,
    Nothing = 0,
    Enough = 1,
    TooMuch = 2
}

/** Adapter class for ogv.js player */
export class AudioContextAudioPlayer {

    private readonly _blazorRef: DotNet.DotNetObject;
    private readonly _debugMode: boolean;
    private readonly _debugOperations: boolean;
    private readonly _debugAppendAudioCalls: boolean;
    private readonly _debugDecoder: boolean;
    private readonly _debugFeeder: boolean;
    private readonly _debugFeederStats: boolean;

    /** How much seconds do we have in the buffer before we tell to blazor that we have enough data */
    private readonly _bufferTooMuchThreshold = 1.0;
    /**
     * How much seconds do we have in the buffer before we can start to play (from the start or after starving),
     * should be in sync with audio-feeder bufferSize
     */
    private readonly _bufferEnoughThreshold = 0.010;

    private _demuxer?: Demuxer;
    private _demuxerReady: Promise<Demuxer>;
    private _decoder?: Decoder;
    private _decoderReady: Promise<Decoder>;
    private _audioContext: AudioContext;
    private _feeder?: AudioFeeder;
    private _queue: OperationQueue;
    private _nextProcessingTickTimer: number | null;
    private _isProcessing: boolean;
    private _isAppending: boolean;
    private _isEndOfStreamReached: boolean;
    private _operationSequenceNumber: number;

    public static sharedAudioContext: AudioContext | null = null;
    private _state: BufferState;
    private _unblockQueue?: () => void;

    public static getOrCreateSharedAudioContext(): AudioContext | null {
        if (AudioContextAudioPlayer.sharedAudioContext === null) {
            console.warn("AudioContextAudioPlayer.sharedAudioContext is null, create one");
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

    constructor(blazorRef: DotNet.DotNetObject, debugMode: boolean) {
        this._debugMode = debugMode;
        this._blazorRef = blazorRef;
        this._demuxer = null;
        this._isProcessing = false;
        this._isAppending = false;
        this._isEndOfStreamReached = false;
        this._state = BufferState.Nothing;
        this._operationSequenceNumber = 0;
        this._decoder = null;
        this._feeder = null;
        this._unblockQueue = null;
        this._debugAppendAudioCalls = debugMode && false;
        this._debugOperations = debugMode && true;
        this._debugDecoder = debugMode && false;
        this._debugFeeder = debugMode && true;
        this._debugFeederStats = this._debugFeeder && false;
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
        if (this._audioContext.state === "suspended")
            this._audioContext.resume();
        this._feeder = new AudioFeeder({
            // TODO: adjust this buffer size and maybe inside the WebAudioBackend
            // we have 960 samples in opus frame (if it was recorded by our wasm recorder)
            // bufferSize must be power of 2, so 512 (10ms-20ms) or 1024 (20ms+)
            bufferSize: 512,
            audioContext: this._audioContext,
        });
        this._feeder.onbufferlow = () => this.unblockQueue('onbufferlow');
        this._feeder.onstarved = () => {
            if (this._isEndOfStreamReached) {
                this._feeder.onstarved = null;
                if (debugMode)
                    this.log(`audio ended.`);
                this.dispose();
                this.invokeOnPlaybackEnded();
                return;
            }
            this.unblockQueue('onstarved');
        };

    }

    public static create(blazorRef: DotNet.DotNetObject, debugMode: boolean) {
        const player = new AudioContextAudioPlayer(blazorRef, debugMode);
        if (debugMode) {
            self["_player"] = player;
        }
        return player;
    }

    public async initialize(byteArray: Uint8Array): Promise<void> {
        if (this._debugMode)
            this.log(`initialize(header: ${byteArray.length} bytes)`);
        try {
            this._feeder.init(1, 48000);
            this._feeder.bufferThreshold = this._bufferTooMuchThreshold - 0.5;
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
                                const samples = await this.decodeProcess(packet);
                                if (this._debugDecoder) {
                                    this.log("decodeProcess returned " + (samples instanceof Float32Array
                                        ? `${samples.byteLength} bytes / ${samples.length} samples`
                                        : samples
                                    ) + `isMetadataLoaded: ${this._isMetadataLoaded}`);
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
                                    if (this._state === BufferState.Nothing) {
                                        this._feeder.start();
                                        if (this._debugFeeder) {
                                            this.log("Feeder start playing, "
                                                + `bufferDuration: ${this._feeder.durationBuffered}, `
                                                + `playbackState: ${JSON.stringify(this._feeder.getPlaybackState())}`);
                                        }
                                    }
                                    this._state = BufferState.Enough;
                                }
                                // we buffered enough data, tell to blazor about it and block the operation queue
                                if (bufferedSpan >= this._bufferTooMuchThreshold) {
                                    this._state = BufferState.TooMuch;
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
            this.onProcessingTick();
            return Promise.resolve();
        }
        finally {
            this._isAppending = false;
        }
    }

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
            while (await this._queue.executeNext());
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
        this.onProcessingTick();

    }

    public stop(error: EndOfStreamError | null) {
        if (this._debugMode)
            this.log(`stop(error:${error})`);
        this._queue.abort();
        this.onProcessingTick();
    }

    public dispose(): void {
        if (this._debugMode)
            this.log(`dispose()`);

        this._queue.abort();
        this._demuxer?.flush(() => { this._demuxer?.close(); this._demuxer = null; });
        this._decoder?.close();
        this._decoder = null;
        this._feeder.flush();
        this._feeder.close();
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

    private decodeProcess(packet: ArrayBuffer): Promise<Float32Array[] | null> {
        const decoder = this._decoder;
        if (decoder === null)
            return Promise.reject("Decoder is disposed");
        if (decoder.processing === true)
            return Promise.reject("Decoder is processing");

        return new Promise<Float32Array[] | null>((resolve, reject) => {
            if (!this._isMetadataLoaded) {
                decoder.processHeader(packet, _ => resolve(null));
            }
            else {
                decoder.processAudio(packet, _ => {
                    if (decoder.audioBuffer !== null && decoder.audioBuffer.length > 0)
                        resolve(decoder.audioBuffer);
                    else
                        reject("Can't decode packet to the right format");
                });
            }
        });
    }

    private unblockQueue(source: string) {
        const unblock = this._unblockQueue;
        this._unblockQueue = null;
        if (unblock !== null) {
            if (this._debugOperations)
                this.logWarn(`[${source}]: Unblocking queue`);
            unblock();
            this.invokeOnChangeReadiness(true, this._feeder.playbackPosition, 2);
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
        console.debug(`[${new Date(Date.now()).toISOString()}] AudioContextAudioPlayer: ${message}`);
    }

    private logWarn(message: string) {
        console.warn(`[${new Date(Date.now()).toISOString()}] AudioContextAudioPlayer: ${message}`);
    }

    private logError(message: string) {
        console.error(`[${new Date(Date.now()).toISOString()}] AudioContextAudioPlayer: ${message}`);
    }
}

// We're only allowed 4 contexts on many browsers and there's no way to discard them so we create
// one as soon as we could, anyway some of browsers don't allow to create without it user interaction
AudioContextAudioPlayer.getOrCreateSharedAudioContext();
