import { Log } from 'logging';
import { FeederAudioWorkletNode } from './worklets/feeder-audio-worklet-node';
import { DeviceInfo } from 'device-info';
import { PromiseSource } from 'promises';
import { createWebRtcAecStream, isWebRtcAecRequired } from './web-rtc-aec';
import { Disposable } from 'disposable';

const { debugLog, errorLog } = Log.get('FallbackPlayback');

export class FallbackPlayback {
    private readonly audio: HTMLAudioElement;
    private destinationNode: MediaStreamAudioDestinationNode = null;
    private aecStream: MediaStream & Disposable = null;
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
        this.audio.muted = false;
        document.body.append(this.audio);

        document.body.addEventListener(
            'click',
            (e) => {
                if (!e.isTrusted)
                    return; // trigger on user action only!

                void this.warmup();
            },
            { capture: true, passive: false, once: true });
    }

    public async attach(context: AudioContext): Promise<void> {
        if (!this.isRequired)
            return;

        debugLog?.log('-> attach(): ', Log.ref(context));

        try {
            if (!this.destinationNode || this.destinationNode.context !== context) {
                this.destinationNode = context.createMediaStreamDestination();
                this.destinationNode.channelCountMode = 'max';
                this.destinationNode.channelCount = 2;
                this.destinationNode.channelInterpretation = 'speakers';
                if (isWebRtcAecRequired)
                    this.aecStream = await createWebRtcAecStream(this.destinationNode.stream);
                this.audio.srcObject = this.audioStream;
            }
            debugLog?.log('attach(): success')
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
            debugLog?.log('detach(): removing audio.srcObject');
            this.audio.pause();
            this.audio.srcObject = null;
            this.destinationNode.stream.getAudioTracks().forEach(x => x.stop());
            this.destinationNode.stream.getVideoTracks().forEach(x => x.stop());
            this.destinationNode.disconnect();
            this.destinationNode = null;
            if (this.aecStream) {
                this.aecStream.dispose();
                this.aecStream = null;
            }
        } catch (e) {
            errorLog?.log('detach(): failed to disconnect feeder node from fallback output:', e);
        }
        debugLog?.log('<- detach()');
    }

    public async play(feederNode: FeederAudioWorkletNode): Promise<void> {
        try {
            feederNode.connect(this.destinationNode);
            this.audio.muted = false;
            await this.audio.play();
        } catch (e) {
            errorLog?.log('play(): failed to resume:', e);
        }
    }

    private async warmup(): Promise<void> {
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
