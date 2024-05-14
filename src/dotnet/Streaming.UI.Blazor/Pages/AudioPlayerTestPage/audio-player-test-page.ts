/// #if MEM_LEAK_DETECTION
import codec, { Decoder, Codec } from '@actual-chat/codec/codec.debug';
import codecWasm from '@actual-chat/codec/codec.debug.wasm';
import codecWasmMap from '@actual-chat/codec/codec.debug.wasm.map';
/// #else
/// #code import codec, { Decoder, Codec } from '@actual-chat/codec';
/// #code import codecWasm from '@actual-chat/codec/codec.wasm';
/// #endif
import { retry } from 'promises';
import { Versioning } from 'versioning';
import { AudioPlayer } from '../../Components/AudioPlayer/audio-player';
import { PlaybackState } from '../../Components/AudioPlayer/worklets/feeder-audio-worklet-contract';
import { SAMPLE_RATE } from '../../Components/AudioPlayer/constants';


export class AudioPlayerTestPage {
    private stats = {
        constructorStartTime: 0,
        constructorEndTime: 0,
        playingStartTime: 0,
    };
    private readonly player: AudioPlayer;
    private readonly codecModule: Codec;

    constructor(blazorRef: DotNet.DotNetObject, player: AudioPlayer, codecModule: Codec) {
        this.stats.constructorStartTime = new Date().getTime();
        this.player = player;
        this.codecModule = codecModule;
        this.player.onPlaybackStateChanged = (playbackState: PlaybackState) => {
            if (playbackState !== 'playing')
                return;

            this.stats.playingStartTime = new Date().getTime();
            console.warn('onPlaying, stats:', this.stats);
            void blazorRef.invokeMethodAsync('OnPlaying', this.getStats());
        };
        this.stats.constructorEndTime = new Date().getTime();
        this.stats.playingStartTime = 0;
    }

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
        const codecModule = await retry(3, () => codec(getEmscriptenLoaderOptions()));
        return new AudioPlayerTestPage(blazorRef, player, codecModule);
    }

    public getStats() {
        return this.stats;
    }

    public frame(byteArray: Uint8Array): Promise<void> {
        return this.player.frame(byteArray);
    }

    public end(): Promise<void> {
        return this.player.end(false);
    }

    public stop(): Promise<void> {
        return this.player.end(true);
    }

    public pause(): Promise<void> {
        return this.player.pause();
    }

    public resume(): Promise<void> {
        return this.player.resume();
    }

    public async testDecoder(): Promise<void> {
        const decoder = new this.codecModule.Decoder(SAMPLE_RATE);

        const buffer = new Uint8Array([150,1,128,192,179,74,83,46,82,101,99,101,105,118,101,66,121,116,101,65,114,114,97,121,146,2,196,125,248,127,170,45,133,199,226,240,252,202,237,186,114,234,199,198,191,64,0,244,89,219,79,39,238,236,39,238,43,136,241,177,18,144,215,230,28,17,107,239,210,50,161,182,56,125,156,43,70,10,54,60,7,183,196,0,123,237,133,169,38,242,254,167,250,205,83,153,190,172,179,164,213,182,74,70,148,46,102,63,178,102,144,255,65,115,197,38,201,92,149,109,89,24,179,68,205,86,214,24,168,93,197,33,67,74,156,242,38,203,176,67,130,6,198,12,91,213,76,248,43,76,149,22,247,144]).buffer;
        const chunk = new Uint8Array(buffer,28, 125);

        console.log('Starting decoder test...');
        for (let i = 0; i < 20000; i++) {
            decoder.decode(chunk);
        }
        console.log('Decoder test completed.');
        decoder.delete();
    }
}

function getEmscriptenLoaderOptions(): EmscriptenLoaderOptions {
    return {
        locateFile: (filename: string) => {
            const codecWasmPath = Versioning.mapPath(codecWasm);
            if (filename.slice(-4) === 'wasm')
                return codecWasmPath;
            /// #if MEM_LEAK_DETECTION
            else if (filename.slice(-3) === 'map')
                return codecWasmMap;
                /// #endif
                // Allow secondary resources like the .wasm payload to be loaded by the emscripten code.
            // emscripten 1.37.25 loads memory initializers as data: URI
            else if (filename.slice(0, 5) === 'data:')
                return filename;
            else throw new Error(`Emscripten module tried to load an unknown file: "${filename}"`);
        },
    };
}

