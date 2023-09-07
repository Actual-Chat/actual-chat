import { Log } from 'logging';
import { FeederAudioWorkletNode } from './worklets/feeder-audio-worklet-node';
import { DeviceInfo } from 'device-info';
import { audioContextSource } from '../../Services/audio-context-source';
import { PromiseSource } from 'promises';

const { debugLog, errorLog } = Log.get('FallbackPlayback');

export class FallbackPlayback {
    private readonly audio: HTMLAudioElement;
    private dest: MediaStreamAudioDestinationNode = null;
    private attachedCount = 0;
    private whenReady: PromiseSource<void> = new PromiseSource<void>();

    public get isRequired() { return DeviceInfo.isIos && DeviceInfo.isWebKit; }

    constructor() {
        if (!this.isRequired)
            return;

        this.audio = new Audio();
        this.audio.preload = "none";
        this.audio.loop = false;
        this.audio.hidden = true;
        this.audio.muted = true;
        document.body.append(this.audio);
        audioContextSource.contextCreated$.subscribe(x => this.onContextCreated(x));
        audioContextSource.contextClosing$.subscribe(() => this.onContextClosing());

        document.body.addEventListener(
            'click',
            (e) => {
                if (!e.isTrusted)
                    return; // trigger on user action only!

                void this.warmup();
            },
            { capture: true, passive: false, once: true });
    }

    public async attach(feederNode: FeederAudioWorkletNode, context: AudioContext) {
        if (!this.isRequired)
            return;

        debugLog?.log('-> attach(): ', Log.ref(context), Log.ref(feederNode), 'currentCount=', this.attachedCount);

        try {
            if (this.attachedCount++ <= 0) {
                this.audio.srcObject = this.dest.stream;
                this.audio.muted = false;
                await this.play();
                debugLog?.log('attach: success, newCount=', this.attachedCount)
            }

            feederNode.connect(this.dest);
        } catch (e) {
            errorLog?.log('attach: failed to connect feeder node to fallback output', e);
        }
        debugLog?.log('<- attach()');
    }

    public detach() {
        if (!this.isRequired)
            return;

        debugLog?.log('-> detach()');
        try {
            if (--this.attachedCount <= 0) {
                debugLog?.log('removing audio.srcObject because attachedCount=', this.attachedCount);
                this.audio.muted = true;
                this.audio.pause();
                this.audio.srcObject = null;
            }
        } catch (e) {
            errorLog?.log('detach: failed to disconnect feeder node from fallback output', e);
        }
        debugLog?.log('<- detach()');
    }

    private async play(){
        try {
            if (this.audio.paused) {
                await this.whenReady;
                await this.audio.play();
                debugLog?.log('play: successfully resumed');
            } else
                debugLog?.log('play: already playing');
        } catch (e) {
            errorLog?.log('play: failed to resume:')
        }
    }

    private onContextCreated(context: AudioContext) {
        if (!this.isRequired)
            return;

        debugLog?.log('-> onContextCreated()');
        try {
            this.dest = context.createMediaStreamDestination();
            this.dest.channelCountMode = 'max';
            this.dest.channelCount = 2;
            this.dest.channelInterpretation = 'speakers';
            this.audio.srcObject = this.dest.stream;
        } catch (e) {
            errorLog?.log('onContextCreated: failed to create destination node', e)
        }
        debugLog?.log('<- onContextCreated()');
    }

    private onContextClosing() {
        if (!this.isRequired)
            return;

        debugLog?.log('-> onContextClosing()');
        try {
            this.audio.muted = true;
            this.audio.pause();
            this.audio.srcObject = null;
            this.dest.stream.getAudioTracks().forEach(x => x.stop());
            this.dest.stream.getVideoTracks().forEach(x => x.stop());
            this.dest = null;
        } catch (e) {
            errorLog?.log('onContextClosing: failed to cleanup', e)
        }
        debugLog?.log('<- onContextClosing()');
    }

    private async warmup() {
        debugLog?.log('-> warmup()');
        try {
            const warmupTask = this.audio.play().then(() => this.audio.pause());
            await warmupTask;
            this.whenReady.resolve(undefined);
        } catch (e) {
            errorLog?.log('warmup: failed', e)
        }
        debugLog?.log('<- warmup()');
    }
}

export const fallbackPlayback = new FallbackPlayback();
