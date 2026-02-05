import { PromiseSource, PromiseSourceWithTimeout } from 'promises';
import { audioContextSource, AppAudioContext, AudioContextAction } from '../../../UI.Blazor.App/Services/audio-context-source';
import { DestinationFallbackTrait } from '../../../UI.Blazor.App/Services/audio-context-traits';
import { Log } from 'logging';
import { AUDIO_PLAY as AP } from '_constants';

const { debugLog, warnLog } = Log.get('SoundsPlayer');
const DEFAULT_COOLDOWN = 3; // 3s
const SILENCE_URL = 'data:audio/wav;base64,UklGRiQAAABXQVZFZm10IBAAAAABAAEARKwAAIhYAQACABAAZGF0YQAAAAA=';

export class SoundPlayer {
    private readonly buffers = new Map<string, AudioBuffer>();
    private readonly offlineContext = new OfflineAudioContext(1, 5000 * AP.SAMPLES_PER_MS, AP.SAMPLE_RATE);
    private readonly recentlyPlayedMap = new Map<string, number>;
    private static _instance?: SoundPlayer;

    constructor() {
    }

    public static get instance(): SoundPlayer {
        return this._instance ??= new SoundPlayer();
    }

    public async play(url: string, cooldown?: number): Promise<void> {
        debugLog?.log('-> play', url);
        const lastPlayedAt = this.recentlyPlayedMap.get(url) ?? 0;
        const playedAt = Date.now();
        this.recentlyPlayedMap.set(url, playedAt);
        if (playedAt - lastPlayedAt <= (cooldown ?? DEFAULT_COOLDOWN) * 1000)
            return; // do not play same sound too often

        let action: AudioContextAction | null = null;
        try {
            const buffer = await this.getSound(url);

            // Use the run() API directly - no need to maintain a persistent ref
            const whilePlaying = new PromiseSource<void>();
            action = audioContextSource.run(async (context) => {
                const source = context.createBufferSource();
                let isEnded = false;
                try {
                    source.buffer = buffer;
                    const destination = DestinationFallbackTrait.getDestination(context as AppAudioContext);
                    source.connect(destination);
                    source.start();
                    source.stop(context.currentTime + 5);
                    const playTask = new PromiseSourceWithTimeout<boolean>();
                    playTask.setTimeout(5000);
                    source.onended = () => playTask.resolve(true);
                    isEnded = await playTask;
                } catch (e) {
                    warnLog?.log('play: failed to play sound', url);
                } finally {
                    whilePlaying.resolve(undefined);
                    if (!isEnded) {
                        try {
                            source.stop();
                        } catch (e) {
                            // Ignore stop errors on already stopped sources
                        }
                    }
                    try {
                        source.disconnect();
                    } catch (e) {
                        // Ignore disconnect errors
                    }
                }
            });
            await whilePlaying;
        }
        finally {
            action?.dispose();
        }
        debugLog?.log('<- play', url);
    }

    private async getSound(url: string): Promise<AudioBuffer> {
        debugLog?.log('-> getSound', url);
        try {
            if (this.buffers.has(url))
                return this.buffers.get(url)!;

            if (url === SILENCE_URL) {
                // Avoid issues with CSP
                const buffer = new AudioBuffer({ length: AP.SAMPLE_RATE, sampleRate: AP.SAMPLE_RATE });
                this.buffers.set(url, buffer);
                return buffer;
            }

            const resp = await fetch(url);
            const soundBytes = await resp.arrayBuffer();
            const context = this.offlineContext;
            const buffer = await context.decodeAudioData(soundBytes);
            this.buffers.set(url, buffer);
            debugLog?.log('<- getSound', url);
            return buffer;
        } catch (e) {
            warnLog?.log('getSound: failed', e);
        }
        return new AudioBuffer({ length: 0, sampleRate: AP.SAMPLE_RATE });
    }
}
