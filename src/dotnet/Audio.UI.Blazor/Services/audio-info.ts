import { AudioPlayer } from '../Components/AudioPlayer/audio-player';
import { opusMediaRecorder } from '../Components/AudioRecorder/opus-media-recorder';
import { PromiseSource } from 'promises';
import { Log } from 'logging';

const { infoLog } = Log.get('AudioInfo');

export class AudioInfo {
    private static backendRef: DotNet.DotNetObject = null;

    public static whenReady: PromiseSource<void> = new PromiseSource<void>();

    public static async init(backendRef1: DotNet.DotNetObject, baseUri: string): Promise<void> {
        this.backendRef = backendRef1;
        infoLog?.log(`init`);

        await opusMediaRecorder.init(baseUri);
        await AudioPlayer.init();
        this.whenReady.resolve(undefined);
        globalThis["audioInfo"] = this;
        infoLog?.log(`ready`);
    }
}

