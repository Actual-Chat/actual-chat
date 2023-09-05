import { AsyncDisposable } from 'disposable';
import { AudioContextRef } from '../../../Audio.UI.Blazor/Services/audio-context-ref';
import { audioContextSource } from '../../../Audio.UI.Blazor/Services/audio-context-source';
import { Log } from 'logging';
import { PromiseSourceWithTimeout } from 'promises';

const { debugLog, warnLog } = Log.get('SoundsPlayer');

export class SoundsPlayer implements AsyncDisposable {
    private context?: AudioContext;
    private ref?: AudioContextRef;

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
        const buffer = await this.getSound(url);

        if (!this.context) {
            warnLog?.log('play: failed to play sound: audioContext became unavailable')
            return;
        }

        const source = this.context.createBufferSource();
        try {
            source.buffer = buffer;
            source.connect(this.context.destination);
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
        debugLog?.log('<- play', url);
    }

    private async onAttach(context: AudioContext) {
        this.context = context;
    }

    private onDetach(context: AudioContext) {
        this.context = null;
    }

    private async getSound(url: string) {
        debugLog?.log('-> getSound', url);
        try {
            const resp = await fetch(url);
            const soundBytes = await resp.arrayBuffer();
            if (!this.context) {
                warnLog?.log('getSound: failed to prepare sound: audioContext became unavailable')
                return;
            }

            const sound = await this.context.decodeAudioData(soundBytes);
            debugLog?.log('<-> getSound', url);
            return sound;
        } catch (e) {
            warnLog?.log('getSound: failed', e);
        }
    }
}

export const soundsPlayer = new SoundsPlayer();
