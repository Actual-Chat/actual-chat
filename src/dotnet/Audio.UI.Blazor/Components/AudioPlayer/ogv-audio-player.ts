import { OGVPlayer } from 'ogv';
import { SignalrStreamFile } from './signalr-stream-file';
import OpusWasm from 'ogv/dist/ogv-decoder-audio-opus-wasm.wasm';
import OpusJs from 'ogv/dist/ogv-decoder-audio-opus-wasm.js';
import WebmDemuxerJs from 'ogv/dist/ogv-demuxer-webm-wasm.js';
import WebmDemuxerWasm from 'ogv/dist/ogv-demuxer-webm-wasm.wasm';

self.OGVLoader.base = "/dist/ogv";

const _ogvFiles = [
    OpusWasm,
    OpusJs,
    WebmDemuxerJs,
    WebmDemuxerWasm,
];

/** Adapter class for ogv.js player */
export class OgvAudioPlayer {
    private readonly _debugMode: boolean;
    private readonly _blazorRef: DotNet.DotNetObject;
    private readonly _player: OGVPlayer;
    private readonly _stream: SignalrStreamFile;
    private _lastReadyState: number;

    constructor(blazorRef: DotNet.DotNetObject, debugMode: boolean) {
        this._debugMode = debugMode;
        this._blazorRef = blazorRef;
        this._lastReadyState = -1;
        this._stream = new SignalrStreamFile(this._debugMode);
        this._player = new OGVPlayer({
            debug: true,
            stream: this._stream,
            worker: false,
            simd: false,
            threading: false
        });
        this._player.addEventListener('ended', e => {
            const _ = this.invokeOnPlaybackEnded();
            if (debugMode)
                this.log('playing is ended');
        });
        this._player.addEventListener('error', e => {
            const err = this._player.error;
            const _ = this.invokeOnPlaybackEnded(err.code, err.message);
            this.logError(`playing error. code: ${err.code}, message: ${err.message}`);
        });
        this._player.addEventListener('timeupdate', e => {
            const time = this._player.currentTime;
            if (this._debugMode)
                this.log(`timeupdate: playing at: ${time}`);
            if (this._player.readyState !== this._lastReadyState) {
                if (this._debugMode)
                    this.log(`timeupdate: new _audio.readyState = ${this.getReadyState()}`);
            }
            this._lastReadyState = this._player.readyState;
            const _ = this.invokeOnPlaybackTimeChanged(time);
        });
        this._player.addEventListener('loadeddata', async _ => {
            // if (this._player.readyState >= 2) {
            //     await this._player.play();
            // }
        });
    }

    public static create(blazorRef: DotNet.DotNetObject, debugMode: boolean) {
        const player = new OgvAudioPlayer(blazorRef, debugMode);
        self["_player"] = player;
        return player;
    }

    public static isCompatible(): boolean {
        return self.OGVCompat.hasWebAudio() && self.OGVCompat.supported('OGVPlayer');
    }

    public async initialize(byteArray: Uint8Array): Promise<void> {
        if (this._debugMode)
            this.log(`initialize(header: ${byteArray.length} bytes)`);
        try {
            this._stream.write(byteArray);
        } catch (error) {
            this.logError(`initialize: error ${error} ${error.stack}`);
        }
    }

    public dispose(): void {
        if (this._debugMode)
            this.log(`dispose()`);
        this.stop(null);
    }

    public async appendAudioAsync(byteArray: Uint8Array, offset: number): Promise<void> {

        if (this._debugMode)
            this.log(`.appendAudio(size: ${byteArray.length}, offset: ${offset})`);
        try {
            this._stream.write(byteArray);
        }
        catch (error) {
            this.logError(`appendAudio: error ${error} ${error.stack}`);
            throw error;
        }
    }

    public endOfStream(): void {
        if (this._debugMode) {
            this.log(`endOfStream()`);
        }
        this._stream.endOfStream();
    }

    public stop(error: EndOfStreamError | null) {
        if (this._debugMode)
            this.log(`stop()`);
        this._player.pause();
        this._stream.abort();
    }

    private getReadyState(): string {
        switch (this._player.readyState) {
            case this._player.HAVE_CURRENT_DATA:
                return 'HAVE_CURRENT_DATA';
            case this._player.HAVE_ENOUGH_DATA:
                return 'HAVE_ENOUGH_DATA';
            case this._player.HAVE_FUTURE_DATA:
                return 'HAVE_FUTURE_DATA';
            case this._player.HAVE_METADATA:
                return 'HAVE_METADATA';
            case this._player.HAVE_NOTHING:
                return 'HAVE_NOTHING';
            default:
                return 'UNKNOWN:' + this._player.readyState;
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
        console.debug(`[${new Date(Date.now()).toISOString()}] OgvAudioPlayer: ${message}`);
    }

    private logWarn(message: string) {
        console.warn(`[${new Date(Date.now()).toISOString()}] OgvAudioPlayer: ${message}`);
    }

    private logError(message: string) {
        console.error(`[${new Date(Date.now()).toISOString()}] OgvAudioPlayer: ${message}`);
    }

}