// One ladder tier = one loopback RTCPeerConnection pair with a single encoding.
// WebRTC encodes the camera track (zero-copy on platforms with a GPU→HW-encoder
// path) and we tap the encoded frames via RTCRtpScriptTransform — the transform
// runs in the recorder worker (passed as `worker`). Native multi-encoding
// simulcast is NOT used: the encoded transform only delivers the base layer
// (proven in the Phase-1 spike), so each tier is its own single-encoding PC.

import { getLogs } from 'logging';
import { PromiseSource } from 'actuallab-core';
import type { WebRtcTierTransformOptions, WebRtcDropTransformOptions } from './webrtc-contract';

const { infoLog, warnLog } = getLogs('VideoPipeline');

export interface LoopbackTierStats {
    tier: number;
    width: number;
    height: number;
    framesEncoded: number;
    keyFramesEncoded: number;
    bytesSent: number;
    targetBitrate: number;
    fps: number;
    encoderImplementation: string;
    powerEfficient: boolean | undefined;
    active: boolean;
}

export interface LoopbackTierOptions {
    track: MediaStreamTrack;
    // Transform target — the recorder worker, which hosts onrtctransform.
    worker: Worker;
    transformOptions: WebRtcTierTransformOptions;
    scaleResolutionDownBy: number;
    maxBitrate: number;
    maxFramerate?: number;
    // Pushed to the top of setCodecPreferences so the negotiated codec is ours.
    codecPreferences?: RTCRtpCodecCapability[];
}

export class LoopbackTier {
    private outbound: RTCPeerConnection | null = null;
    private inbound: RTCPeerConnection | null = null;
    private sender: RTCRtpSender | null = null;
    private closed = false;

    constructor(private readonly opts: LoopbackTierOptions) {}

    get tier(): number { return this.opts.transformOptions.tier; }

    getState(): { tier: number; connectionState: string; encodings: unknown[] } {
        return {
            tier: this.tier,
            connectionState: this.outbound?.connectionState ?? 'closed',
            encodings: this.sender?.getParameters().encodings ?? [],
        };
    }

    // Per-tier (= per simulcast layer) encoder stats from the outbound-rtp report.
    // `encoderImplementation`/`powerEfficientEncoder` reveal HW vs SW directly.
    async getStats(): Promise<LoopbackTierStats | null> {
        const sender = this.sender;
        if (!sender) return null;
        const report = await sender.getStats();
        const stats: Record<string, unknown>[] = [];
        report.forEach((s: unknown) => stats.push(s as Record<string, unknown>));
        const ob = stats.find(r => r.type === 'outbound-rtp' && r.kind === 'video');
        if (!ob) return null;
        const numOf = (v: unknown): number => typeof v === 'number' ? v : 0;
        return {
            tier: this.tier,
            width: numOf(ob.frameWidth),
            height: numOf(ob.frameHeight),
            framesEncoded: numOf(ob.framesEncoded),
            keyFramesEncoded: numOf(ob.keyFramesEncoded),
            bytesSent: numOf(ob.bytesSent),
            targetBitrate: numOf(ob.targetBitrate),
            fps: numOf(ob.framesPerSecond),
            encoderImplementation: typeof ob.encoderImplementation === 'string' ? ob.encoderImplementation : '',
            powerEfficient: typeof ob.powerEfficientEncoder === 'boolean' ? ob.powerEfficientEncoder : undefined,
            active: typeof ob.active === 'boolean' ? ob.active : true,
        };
    }

    async connect(): Promise<void> {
        const outbound = new RTCPeerConnection();
        const inbound = new RTCPeerConnection();
        this.outbound = outbound;
        this.inbound = inbound;

        // Loopback ICE: host candidates only, exchanged in-process.
        outbound.onicecandidate = e => e.candidate && void inbound.addIceCandidate(e.candidate);
        inbound.onicecandidate = e => e.candidate && void outbound.addIceCandidate(e.candidate);

        // Drop the echoed stream before it's decoded: attach a receiver-side
        // transform that discards frames (the loopback only exists to run the
        // encoder). Set on `ontrack` (fires during setRemoteDescription) so it's
        // in place before any frame reaches the decoder. RTP still flows →
        // RTCP/transport-cc feedback keeps the sender's BWE healthy.
        inbound.ontrack = e => {
            const dropOptions: WebRtcDropTransformOptions = { kind: 'webrtc-drop', tier: this.tier };
            try { e.receiver.transform = new RTCRtpScriptTransform(this.opts.worker, dropOptions); }
            catch (err) { warnLog?.log(`tier ${this.tier}: attach receiver drop-transform failed`, err); }
        };
        const whenConnected = new PromiseSource<void>();
        outbound.onconnectionstatechange = () => {
            console.info(`[voxtWebRtc] tier ${this.tier} PC state=${outbound.connectionState}`);
            if (outbound.connectionState === 'connected') whenConnected.resolve();
            else if (outbound.connectionState === 'failed')
                whenConnected.reject(new Error(`tier ${this.tier} PC failed`));
        };

        const tx = outbound.addTransceiver(this.opts.track, {
            direction: 'sendonly',
            sendEncodings: [{
                scaleResolutionDownBy: this.opts.scaleResolutionDownBy,
                maxBitrate: this.opts.maxBitrate,
                ...(this.opts.maxFramerate ? { maxFramerate: this.opts.maxFramerate } : {}),
            }],
        });
        this.sender = tx.sender;

        if (this.opts.codecPreferences?.length && 'setCodecPreferences' in tx) {
            try { tx.setCodecPreferences(this.opts.codecPreferences); }
            catch (e) { warnLog?.log(`tier ${this.tier}: setCodecPreferences failed`, e); }
        }

        // Attach the encoded-frame tap. The recorder worker's onrtctransform
        // reads `transformer.options` to learn which tier this is.
        try {
            tx.sender.transform = new RTCRtpScriptTransform(this.opts.worker, this.opts.transformOptions);
        } catch (e) {
            warnLog?.log(`tier ${this.tier}: attach transform failed`, e);
            throw e;
        }

        const offer = await outbound.createOffer();
        await outbound.setLocalDescription(offer);
        await inbound.setRemoteDescription(offer);
        const answer = await inbound.createAnswer();
        await inbound.setLocalDescription(answer);
        await outbound.setRemoteDescription(answer);

        // Pin rate control: hold resolution, don't let WebRTC BWE/CPU adaptation
        // drop res/fps (our QC owns the ladder). Applied post-negotiation so the
        // encoding object exists.
        this.applyParameters({});

        await whenConnected;
        infoLog?.log(`tier ${this.tier}: connected (scale=${this.opts.scaleResolutionDownBy}, maxBitrate=${this.opts.maxBitrate})`);
    }

    // Hot-apply encoding params. Undefined fields keep their current value.
    applyParameters(p: { maxBitrate?: number; scaleResolutionDownBy?: number; maxFramerate?: number; active?: boolean }): void {
        const sender = this.sender;
        if (!sender) return;
        const params = sender.getParameters();
        params.degradationPreference = 'maintain-resolution';
        if (params.encodings.length > 0) {
            const enc = params.encodings[0];
            if (p.maxBitrate !== undefined) enc.maxBitrate = p.maxBitrate;
            if (p.scaleResolutionDownBy !== undefined) enc.scaleResolutionDownBy = p.scaleResolutionDownBy;
            if (p.maxFramerate !== undefined) enc.maxFramerate = p.maxFramerate;
            if (p.active !== undefined) enc.active = p.active;
        }
        void sender.setParameters(params).catch((e: unknown) => warnLog?.log(`tier ${this.tier}: setParameters failed`, e));
    }

    // Force a keyframe by toggling the encoding active off→on. Proven on Chrome
    // (which lacks RTCRtpScriptTransformer.generateKeyFrame); the worker also
    // calls generateKeyFrame for Safari. Either path re-keys this tier.
    async forceKeyFrame(): Promise<void> {
        const sender = this.sender;
        if (!sender) return;
        const off = sender.getParameters();
        if (off.encodings.length > 0) off.encodings[0].active = false;
        try {
            await sender.setParameters(off);
            await new Promise<void>(r => setTimeout(r, 60));
            const on = sender.getParameters();
            if (on.encodings.length > 0) on.encodings[0].active = true;
            await sender.setParameters(on);
        } catch (e) {
            warnLog?.log(`tier ${this.tier}: forceKeyFrame toggle failed`, e);
        }
    }

    close(): void {
        if (this.closed) return;
        this.closed = true;
        for (const pc of [this.outbound, this.inbound]) {
            if (!pc) continue;
            try {
                pc.onicecandidate = null;
                pc.onconnectionstatechange = null;
                pc.close();
            } catch { /* ignore */ }
        }
        this.outbound = null;
        this.inbound = null;
        this.sender = null;
    }
}
