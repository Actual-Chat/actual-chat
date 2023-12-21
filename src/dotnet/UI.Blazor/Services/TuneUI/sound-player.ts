import { AsyncDisposable } from 'disposable';
import { AudioContextRef } from '../../../Audio.UI.Blazor/Services/audio-context-ref';
import { audioContextSource } from '../../../Audio.UI.Blazor/Services/audio-context-source';
import { Log } from 'logging';
import {delayAsync, PromiseSourceWithTimeout} from 'promises';

const { debugLog, warnLog } = Log.get('SoundsPlayer');

export class SoundPlayer {
    private readonly buffers = new Map<string, AudioBuffer>();
    private context?: AudioContext;

    public async play(url: string): Promise<void>{
        debugLog?.log('-> play', url);
        const audioContextRef = audioContextSource.getRef('play-tunes', {
            attach: context => this.onAttach(context),
            detach: context => this.onDetach(context),
        });
        const context = await audioContextRef.whenFirstTimeReady();
        const pause = audioContextRef.use();
        const buffer = await this.getSound(context, url);
        if (!context) {
            warnLog?.log('play: failed to play sound: audioContext became unavailable')
            return;
        }

        const source = context.createBufferSource();
        try {
            source.buffer = buffer;
            source.connect(context.destination);
            source.start();
            const playTask = new PromiseSourceWithTimeout();
            playTask.setTimeout(5000);
            source.onended = () => playTask.resolve(null);
            await playTask;
            await delayAsync(500);
        } catch (e) {
            warnLog?.log('play: failed to play sound', url);
        } finally {
            source.stop();
            source.disconnect();
            pause();
            await audioContextRef.disposeAsync();
        }
        debugLog?.log('<- play', url);
    }

    private async onAttach(context: AudioContext) {
        if (this.context !== context)
            this.buffers.clear();

        this.context = context;
    }

    private onDetach(context: AudioContext) {
        this.context = null;
        this.buffers.clear();
    }

    private async getSound(context: AudioContext, url: string) {
        debugLog?.log('-> getSound', url);
        try {
            if (this.buffers.has(url))
                return this.buffers.get(url);

            const resp = await fetch(url);
            const soundBytes = await resp.arrayBuffer();
            if (!context) {
                warnLog?.log('getSound: failed to prepare sound: audioContext became unavailable')
                return;
            }

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
