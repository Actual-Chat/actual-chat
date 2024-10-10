import { PromiseSourceWithTimeout } from 'promises';
import { audioContextSource } from '../../../UI.Blazor.App/Services/audio-context-source';
import { Log } from 'logging';
import { AUDIO_PLAY as AP } from '_constants';

const { debugLog, warnLog } = Log.get('SoundsPlayer');
const DEFAULT_COOLDOWN = 3; // 3s

export class SoundPlayer {
    private readonly buffers = new Map<string, AudioBuffer>();
    private readonly offlineContext = new OfflineAudioContext(1, 5000 * AP.SAMPLES_PER_MS, AP.SAMPLE_RATE);
    private readonly recentlyPlayedMap = new Map<string, number>;

    public async play(url: string, cooldown?: number): Promise<void> {
        debugLog?.log('-> play', url);
        const lastPlayedAt = this.recentlyPlayedMap.get(url) ?? 0;
        const playedAt = Date.now();
        this.recentlyPlayedMap.set(url, playedAt);
        if (playedAt - lastPlayedAt <= (cooldown ?? DEFAULT_COOLDOWN) * 1000)
            return; // do not play same sound too often

        const contextRef = audioContextSource.getRef('play-tunes', {
            attach: () => { },
            detach: () => { },
        });
        try {
            const buffer = await this.getSound(url);

            await contextRef.use(async context => {
                const source = context.createBufferSource();
                try {
                    source.buffer = buffer;
                    source.connect(context.destination);
                    source.start();
                    const playTask = new PromiseSourceWithTimeout();
                    playTask.setTimeout(5000);
                    source.onended = () => playTask.resolve(null);
                    await playTask;
                } catch (e) {
                    warnLog?.log('play: failed to play sound', url);
                } finally {
                    source.stop();
                    source.disconnect();
                }
            });
        }
        finally {
            await contextRef.disposeAsync();
        }
        debugLog?.log('<- play', url);
    }

    private async getSound(url: string): Promise<AudioBuffer> {
        debugLog?.log('-> getSound', url);
        try {
            if (this.buffers.has(url))
                return this.buffers.get(url);

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
    }
}

export const soundPlayer = new SoundPlayer();
