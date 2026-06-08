// One ladder tier = one loopback RTCPeerConnection pair with a single encoding.
// WebRTC encodes the camera track (zero-copy on platforms with a GPU→HW-encoder
// path) and we tap the encoded frames via RTCRtpScriptTransform — the transform
// runs in the recorder worker (passed as `worker`). Native multi-encoding
// simulcast is NOT used: the encoded transform only delivers the base layer
// (proven in the Phase-1 spike), so each tier is its own single-encoding PC.

import { getLogs } from 'logging';
import { PromiseSource } from 'actuallab-core';
import type { WebRtcTierTransformOptions } from './webrtc-contract';

const { infoLog, warnLog } = getLogs('VideoPipeline');

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

    async connect(): Promise<void> {
        const outbound = new RTCPeerConnection();
        const inbound = new RTCPeerConnection();
        this.outbound = outbound;
        this.inbound = inbound;

        // Loopback ICE: host candidates only, exchanged in-process.
        outbound.onicecandidate = e => e.candidate && void inbound.addIceCandidate(e.candidate);
        inbound.onicecandidate = e => e.candidate && void outbound.addIceCandidate(e.candidate);
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
