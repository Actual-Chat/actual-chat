import { Log } from 'logging';
import { FeederAudioWorkletNode } from './worklets/feeder-audio-worklet-node';
import { DeviceInfo } from 'device-info';

const { debugLog, errorLog } = Log.get('FallbackPlayback');

export class FallbackPlayback {
    private readonly audio: HTMLAudioElement;
    private dest: MediaStreamAudioDestinationNode = null;
    public get isRequired() { return DeviceInfo.isIos && DeviceInfo.isSafari; }
    private attachedCount = 0;

    constructor() {
        if (!this.isRequired)
            return;

        this.audio = new Audio();
        this.audio.preload = "none";
        this.audio.loop = false;
        this.audio.hidden = true;
        this.audio.muted = true;
        document.body.append(this.audio);
    }

    public async attach(feederNode: FeederAudioWorkletNode, context: AudioContext) {
        if (!this.isRequired)
            return;

        debugLog?.log('-> attach(): ', Log.ref(context), Log.ref(feederNode), 'currentCount=', this.attachedCount);

        try {
            if (this.attachedCount++ <= 0) {
                this.audio.srcObject = this.dest.stream;
                this.audio.muted = false;
                if (this.audio.paused) {
                    await this.audio.play();
                    debugLog?.log('attach: successfully resumed');
                } else
                    debugLog?.log('attach: already playing');
                debugLog?.log('attach: success, newCount=', this.attachedCount)
            }

            feederNode.connect(this.dest);
        } catch (e) {
            this.attachedCount--;
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

    public onContextCreate(context: AudioContext) {
        if (!this.isRequired)
            return;

        debugLog?.log('-> onContextCreate()');
        try {
            this.dest = context.createMediaStreamDestination();
            this.audio.srcObject = this.dest.stream;
        } catch (e) {
            errorLog.log('onContextCreate: failed to create destination node', e)
        }
        debugLog?.log('<- onContextCreate()');
    }

    public onContextClose() {
        if (!this.isRequired)
            return;

        debugLog?.log('-> onContextClose()');
        try {
            this.audio.muted = true;
            this.audio.pause();
            this.audio.srcObject = null;
            this.dest = null;
        } catch (e) {
            errorLog?.log('onContextClose: failed to cleanup', e)
        }
        debugLog?.log('<- onContextClose()');
    }
}

export const fallbackPlayback = new FallbackPlayback();
