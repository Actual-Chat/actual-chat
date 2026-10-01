import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

interface FakeContextSource {
    isContextRunning: boolean;
    run: ReturnType<typeof vi.fn>;
}

const mocks = vi.hoisted(() => ({
    browserInfo: { useWebAudio: false },
    source: null as FakeContextSource | null,
}));
vi.mock('../../../src/dotnet/UI.Blazor/Services/BrowserInfo/browser-info', () => ({ BrowserInfo: mocks.browserInfo }));
vi.mock('../../../src/dotnet/UI.Blazor.App/Services/audio-context-source', () => ({
    get audioContextSource() {
        return mocks.source;
    },
}));
vi.mock('../../../src/dotnet/UI.Blazor.App/Services/audio-context-traits', () => ({ DestinationFallbackTrait: {} }));
vi.mock('app-constants', () => ({ AUDIO: { play: { samplesPerMs: 48, sampleRate: 48000 } } }));

import { SoundPlayer } from '../../../src/dotnet/UI.Blazor/Services/TuneUI/sound-player';

class FakeAudio {
    public static instances: FakeAudio[] = [];
    public static playResult: () => Promise<void> = () => Promise.resolve();

    public loop = false;
    public isPlaying = false;
    public isReleased = false;
    public isLoaded = false;

    constructor(public readonly src: string) {
        FakeAudio.instances.push(this);
    }

    public play(): Promise<void> {
        this.isPlaying = true;
        return FakeAudio.playResult();
    }

    public pause(): void {
        this.isPlaying = false;
    }

    public removeAttribute(): void {
        this.isReleased = true;
    }

    public load(): void {
        this.isLoaded = true;
    }
}

describe('SoundPlayer.loop', () => {
    const url = 'dist/sounds/attention_ringtone.webm';
    let contextAction: { dispose: ReturnType<typeof vi.fn> };

    beforeEach(() => {
        FakeAudio.instances = [];
        FakeAudio.playResult = () => Promise.resolve();
        contextAction = { dispose: vi.fn() };
        mocks.browserInfo.useWebAudio = false;
        mocks.source = null;
        vi.stubGlobal('Audio', FakeAudio);
        vi.stubGlobal('OfflineAudioContext', vi.fn());
    });

    afterEach(() => {
        vi.unstubAllGlobals();
    });

    function useWebAudio(isContextRunning: boolean): FakeContextSource {
        mocks.browserInfo.useWebAudio = true;
        mocks.source = { isContextRunning, run: vi.fn(() => contextAction) };
        return mocks.source;
    }

    it('should loop an audio element in a host without Web Audio', () => {
        // act
        const loop = new SoundPlayer().loop(url);

        // assert
        const [audio] = FakeAudio.instances;
        expect(audio.src).toBe(url);
        expect(audio.loop).toBe(true);
        expect(audio.isPlaying).toBe(true);

        loop.dispose();
        expect(audio.isPlaying).toBe(false);
        expect(audio.isReleased).toBe(true);
    });

    it('should loop through the context while it is running', () => {
        // arrange
        const source = useWebAudio(true);

        // act
        const loop = new SoundPlayer().loop(url);

        // assert
        expect(source.run).toHaveBeenCalledOnce();
        expect(FakeAudio.instances).toHaveLength(0);

        loop.dispose();
        expect(contextAction.dispose).toHaveBeenCalledOnce();
    });

    it('should loop an audio element while the context is suspended', () => {
        // arrange
        const source = useWebAudio(false);

        // act
        new SoundPlayer().loop(url);

        // assert
        expect(FakeAudio.instances[0].isPlaying).toBe(true);
        expect(source.run).not.toHaveBeenCalled();
    });

    it('should fall back to the context when autoplay is blocked', async () => {
        // arrange
        const source = useWebAudio(false);
        FakeAudio.playResult = () => Promise.reject(new Error('NotAllowedError'));

        // act
        const loop = new SoundPlayer().loop(url);
        await vi.waitFor(() => expect(source.run).toHaveBeenCalledOnce());

        // assert
        loop.dispose();
        expect(contextAction.dispose).toHaveBeenCalledOnce();
    });

    it('should not fall back to the context once stopped', async () => {
        // arrange
        const source = useWebAudio(false);
        let rejectPlay: ((e: Error) => void) | null = null;
        FakeAudio.playResult = () => new Promise<void>((_, reject) => rejectPlay = reject);

        // act
        new SoundPlayer().loop(url).dispose();
        rejectPlay!(new Error('AbortError'));
        await Promise.resolve();
        await Promise.resolve();

        // assert
        expect(source.run).not.toHaveBeenCalled();
    });
});
