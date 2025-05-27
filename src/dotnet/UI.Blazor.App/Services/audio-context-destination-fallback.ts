import { Log } from 'logging';
import { DeviceInfo } from 'device-info';
import { createWebRtcAecStream, isWebRtcAecRequired } from './web-rtc-aec';
import { Disposable } from 'disposable';
import { resetMediaSessionMetadata } from './audio-context-source';

const { debugLog, errorLog } = Log.get('FallbackPlayback');

export class AudioContextDestinationFallback implements Disposable {
    private readonly audio: HTMLAudioElement;
    private destinationNode: MediaStreamAudioDestinationNode;
    private aecStream: MediaStream & Disposable | null = null;

    // Allows to expose mediaSession metadata at the lock screen
    public static get isRequired() { return false/*DeviceInfo.isIos*/; }

    public get destination() { return this.destinationNode; }

    private get audioStream() { return this.aecStream ?? this.destinationNode.stream; }

    constructor(context: AudioContext) {
        this.audio = new Audio();
        this.audio.id = 'audio-context-destination';
        this.audio.preload = "none";
        this.audio.loop = false;
        this.audio.hidden = true;
        this.audio.muted = false;
        this.audio.controls = false;
        document.body.append(this.audio);

        this.destinationNode = context.createMediaStreamDestination();
        this.destinationNode.channelInterpretation = 'speakers';

        resetMediaSessionMetadata();
        if (isWebRtcAecRequired)
            void createWebRtcAecStream(this.destinationNode.stream)
                .then(x => this.aecStream = x);
    }

    public async play(): Promise<void> {
        debugLog?.log('-> play()', this.audio?.paused);
        try {
            this.audio.srcObject = this.audioStream;
            this.audio.muted = false;
            if (this.audio.paused)
                await this.audio.play();
        } catch (e) {
            errorLog?.log('play(): failed to resume:', e);
        }
        debugLog?.log('<- play()', this.audio?.paused);
    }

    public pause(): void {
        debugLog?.log('-> pause()', this.audio?.paused);
        try {
            this.audio.muted = true;
            this.audio.pause();
        } catch (e) {
            errorLog?.log('pause(): failed to pause:', e);
        }
        debugLog?.log('<- pause()', this.audio?.paused);
    }

    public dispose(): void {
        this.audio.pause();
        this.audio.srcObject = undefined;
        this.audio.src = undefined;
        document.body.removeChild(this.audio);

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
    }
}
