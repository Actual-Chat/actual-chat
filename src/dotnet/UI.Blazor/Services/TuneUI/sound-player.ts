import { AsyncDisposable } from 'disposable';
import { AudioContextRef } from '../../../Audio.UI.Blazor/Services/audio-context-ref';
import { audioContextSource } from '../../../Audio.UI.Blazor/Services/audio-context-source';
import { Log } from 'logging';
import {delayAsync, PromiseSourceWithTimeout} from 'promises';

const { debugLog, warnLog } = Log.get('SoundsPlayer');

export class SoundPlayer implements AsyncDisposable {
    private readonly buffers = new Map<string, AudioBuffer>();
    private context?: AudioContext = null;
    private ref?: AudioContextRef = null;

    constructor() {
        this.ref = audioContextSource.getRef('play-tunes', {
            attach: context => this.onAttach(context),
            detach: context => this.onDetach(context),
            dispose: () => this.disposeAsync(),
        });
    }

    public async disposeAsync(): Promise<void> {
        this.context = null;
        await this.ref.disposeAsync()
    }

    public async play(url: string){
        debugLog?.log('-> play', url);
        const { context} = this;
        const buffer = await this.getSound(url);
        if (!context) {
            warnLog?.log('play: failed to play sound: audioContext became unavailable')
            return;
        }

        const source = context.createBufferSource();
        try {
            source.buffer = buffer;
            source.connect(this.context.destination);
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

    private async getSound(url: string) {
        debugLog?.log('-> getSound', url);
        try {
            if (this.buffers.has(url))
                return this.buffers.get(url);

            const resp = await fetch(url);
            const soundBytes = await resp.arrayBuffer();
            if (!this.context) {
                warnLog?.log('getSound: failed to prepare sound: audioContext became unavailable')
                return;
            }

            const buffer = await this.context.decodeAudioData(soundBytes);
            this.buffers.set(url, buffer);
            debugLog?.log('<- getSound', url);
            return buffer;
        } catch (e) {
            warnLog?.log('getSound: failed', e);
        }
    }
}

export const soundPlayer = new SoundPlayer();
