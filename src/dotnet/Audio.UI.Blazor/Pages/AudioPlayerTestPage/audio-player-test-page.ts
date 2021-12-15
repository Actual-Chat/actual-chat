import { OGVPlayer, OGVLoader, OGVCompat } from "ogv";

export class AudioPlayerTestPage {

    private _ogvPlayer: OGVPlayer;

    constructor() {
        this._ogvPlayer = new OGVPlayer();
    }

    public static isOgvCompatible(): boolean {
        return self.OGVCompat.hasWebAudio() && self.OGVCompat.supported('OGVPlayer');
    }

    public static create() {
        return new AudioPlayerTestPage();
    }
}

