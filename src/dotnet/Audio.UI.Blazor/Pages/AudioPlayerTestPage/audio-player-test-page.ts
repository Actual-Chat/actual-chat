import { OGVPlayer, OGVLoader, OGVCompat } from 'ogv';
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

export class AudioPlayerTestPage {

    private _ogvPlayer: OGVPlayer;
    private readonly _debugMode: boolean;
    private readonly _blazorRef: DotNet.DotNetObject;

    constructor(blazorRef: DotNet.DotNetObject, debugMode: boolean) {
        this._debugMode = debugMode;
        this._blazorRef = blazorRef;
        this._ogvPlayer = new OGVPlayer({
            debug: true,
            worker: false,
            simd: false,
            threading: false
        });
        const _ = OpusWasm;
        window["ogvPlayer"] = this._ogvPlayer;
    }

    public static isOgvCompatible(): boolean {
        return self.OGVCompat.hasWebAudio() && self.OGVCompat.supported('OGVPlayer');
    }

    public static create(blazorRef: DotNet.DotNetObject, debugMode: boolean) {
        return new AudioPlayerTestPage(blazorRef, debugMode);
    }

    public ogvPlay(url: string) {
        this._ogvPlayer.src = url;
        this._ogvPlayer.play();
    }
}

