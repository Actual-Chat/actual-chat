import { Log } from 'logging';
import { FeederAudioWorkletNode } from './worklets/feeder-audio-worklet-node';
import { DeviceInfo } from 'device-info';
import { audioContextSource } from '../../Services/audio-context-source';
import { PromiseSource } from 'promises';
import { createWebRtcAecStream, isWebRtcAecRequired } from './web-rtc-aec';
import { Disposable } from 'disposable';

const { debugLog, errorLog } = Log.get('FallbackPlayback');

export class FallbackPlayback {
    private readonly audio: HTMLAudioElement;
    private destinationNode: MediaStreamAudioDestinationNode = null;
    private aecStream: MediaStream & Disposable = null;
    private attachedCount = 0;
    private whenReady: PromiseSource<void> = new PromiseSource<void>();

    private get audioStream() { return this.aecStream ?? this.destinationNode.stream; }

    public get isRequired() { return isWebRtcAecRequired || DeviceInfo.isIos && DeviceInfo.isWebKit; }

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

        debugLog?.log('-> attach(): attachedCount:', this.attachedCount, Log.ref(context), Log.ref(feederNode));

        try {
            if (this.attachedCount++ <= 0) {
                this.audio.srcObject = this.audioStream;
                this.audio.muted = false;
                await this.play();
                debugLog?.log('attach(): success')
            }

            feederNode.connect(this.destinationNode);
        } catch (e) {
            errorLog?.log('attach(): failed to connect feeder node to fallback output:', e);
        }
        debugLog?.log('<- attach()');
    }

    public detach() {
        if (!this.isRequired)
            return;

        debugLog?.log('-> detach()');
        try {
            if (--this.attachedCount <= 0) {
                debugLog?.log('detach(): removing audio.srcObject');
                this.audio.muted = true;
                this.audio.pause();
                this.audio.srcObject = null;
            }
        } catch (e) {
            errorLog?.log('detach(): failed to disconnect feeder node from fallback output:', e);
        }
        debugLog?.log('<- detach()');
    }

    private async play() {
        try {
            if (this.audio.paused) {
                await this.whenReady;
                await this.audio.play();
                debugLog?.log('play(): successfully resumed');
            } else
                debugLog?.log('play(): already playing');
        } catch (e) {
            errorLog?.log('play(): failed to resume:', e);
        }
    }

    private async onContextCreated(context: AudioContext): Promise<void> {
        if (!this.isRequired)
            return;

        debugLog?.log('-> onContextCreated()');
        try {
            this.destinationNode = null;
            this.aecStream = null;

            const destinationNode = this.destinationNode = context.createMediaStreamDestination();
            this.destinationNode.channelCountMode = 'max';
            this.destinationNode.channelCount = 2;
            this.destinationNode.channelInterpretation = 'speakers';
            if (isWebRtcAecRequired) {
                const aecStream = await createWebRtcAecStream(this.destinationNode.stream);
                if (destinationNode !== this.destinationNode) {
                    // Concurrent onContextCreated is already creating aecStream for a newer destinationNode
                    aecStream?.dispose();
                    return;
                }
                this.aecStream = aecStream;
            }
            if (this.attachedCount > 0) {
                debugLog?.log('onContextCreated: replacing audio.srcObject with:', this.audioStream);
                this.audio.srcObject = this.audioStream;
            }
        } catch (e) {
            errorLog?.log('onContextCreated(): failed to create destination node:', e)
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
            this.aecStream = null;
            this.destinationNode.stream.getAudioTracks().forEach(x => x.stop());
            this.destinationNode.stream.getVideoTracks().forEach(x => x.stop());
            this.destinationNode = null;
            if (this.aecStream) {
                this.aecStream.dispose();
                this.aecStream = null;
            }
        } catch (e) {
            errorLog?.log('onContextClosing: failed to cleanup:', e)
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
            errorLog?.log('warmup() failed:', e)
        }
        debugLog?.log('<- warmup()');
    }
}

export const fallbackPlayback = new FallbackPlayback();
