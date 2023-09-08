import { AsyncDisposable } from 'disposable';
import { AudioContextRef } from '../../../Audio.UI.Blazor/Services/audio-context-ref';
import { audioContextSource } from '../../../Audio.UI.Blazor/Services/audio-context-source';
import { Log } from 'logging';
import {delayAsync, PromiseSourceWithTimeout} from 'promises';

const { debugLog, warnLog } = Log.get('SoundsPlayer');

export class SoundPlayer implements AsyncDisposable {
    private context?: AudioContext = null;
    private ref?: AudioContextRef = null;
    private gainNodeL?: GainNode = null;
    private gainNodeR?: GainNode = null;
    private channelMerger?: ChannelMergerNode = null;

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
        const { context, gainNodeL, gainNodeR} = this;
        const buffer = await this.getSound(url);
        if (!context) {
            warnLog?.log('play: failed to play sound: audioContext became unavailable')
            return;
        }

        const source = context.createBufferSource();
        try {
            source.buffer = buffer;
            source.connect(gainNodeL);
            source.connect(gainNodeR);
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
        this.context = context;
        this.gainNodeL = context.createGain();
        this.gainNodeR = context.createGain();
        this.channelMerger = context.createChannelMerger(2);
        this.gainNodeL.connect(this.channelMerger, 0, 0);
        this.gainNodeR.connect(this.channelMerger, 0, 1);
        this.channelMerger.connect(context.destination);
    }

    private onDetach(context: AudioContext) {
        this.gainNodeL?.disconnect();
        this.gainNodeR?.disconnect();
        this.channelMerger?.disconnect();
        this.gainNodeL = null;
        this.gainNodeR = null;
        this.channelMerger = null;
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

export const soundPlayer = new SoundPlayer();
