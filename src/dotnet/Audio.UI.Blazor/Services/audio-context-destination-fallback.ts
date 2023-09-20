import { Log } from 'logging';
import { DeviceInfo } from 'device-info';
import { PromiseSource } from 'promises';
import { createWebRtcAecStream, isWebRtcAecRequired } from './web-rtc-aec';
import { Disposable } from 'disposable';

const { debugLog, errorLog } = Log.get('FallbackPlayback');

export class AudioContextDestinationFallback {
    private readonly audio: HTMLAudioElement;
    private destinationNode?: MediaStreamAudioDestinationNode = null;
    private aecStream: MediaStream & Disposable = null;

    public static get isRequired() { return isWebRtcAecRequired || DeviceInfo.isIos && DeviceInfo.isWebKit; }

    public get destination() { return this.destinationNode; }

    private get audioStream() { return this.aecStream ?? this.destinationNode.stream; }

    constructor() {
        if (!AudioContextDestinationFallback.isRequired)
            return;

        this.audio = new Audio();
        this.audio.preload = "none";
        this.audio.loop = false;
        this.audio.hidden = true;
        this.audio.muted = false;
        document.body.append(this.audio);
    }

    public async attach(context: AudioContext): Promise<void> {
        if (!AudioContextDestinationFallback.isRequired)
            return;

        debugLog?.log('-> attach(): ', Log.ref(context));

        try {
            if (!this.destinationNode || this.destinationNode.context !== context) {
                this.destinationNode = context.createMediaStreamDestination();
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
        if (!AudioContextDestinationFallback.isRequired)
            return;

        debugLog?.log('-> detach()');
        try {
            debugLog?.log('detach(): removing audio.srcObject');
            this.audio.pause();
            this.audio.srcObject = null;
            if (this.destinationNode) {
                this.destinationNode.stream.getAudioTracks().forEach(x => x.stop());
                this.destinationNode.stream.getVideoTracks().forEach(x => x.stop());
                this.destinationNode.disconnect();
                this.destinationNode = null;
            }
            if (this.aecStream) {
                this.aecStream.dispose();
                this.aecStream = null;
            }
        } catch (e) {
            errorLog?.log('detach(): failed to disconnect feeder node from fallback output:', e);
        }
        debugLog?.log('<- detach()');
    }

    public async play(): Promise<void> {
        debugLog?.log('-> play()', this.audio.paused);
        try {
            this.audio.muted = false;
            if (this.audio.paused)
                await this.audio.play();
        } catch (e) {
            errorLog?.log('play(): failed to resume:', e);
        }
        debugLog?.log('<- play()', this.audio.paused);
    }
}
