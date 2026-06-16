// One loopback RTCPeerConnection pair running native simulcast: a single
// sender encodes the whole ladder (one encoding per tier via `sendEncodings`)
// straight from GPU textures (zero-copy on platforms with a GPU→HW-encoder
// path), and ALL layers are tapped through one `RTCRtpScriptTransform` whose
// target is the recorder worker. The encoded frames are demuxed back to tiers
// by SSRC in the worker tap — `metadata.rid` is absent on Chrome, but every
// layer reaches the transform, so a single PC replaces the former per-tier PCs.
//
// The loopback's inbound PC exists only so the encoder runs; its frames are
// dropped pre-decode via a receiver-side transform. The answer SDP is munged so
// simulcast survives the loopback (see `mungeAnswerSimulcast`).

import { getLogs } from 'logging';
import { PromiseSource } from 'actuallab-core';
import { mungeAnswerSimulcast } from './sdp-munge';
import type {
    WebRtcSimulcastTransformOptions,
    WebRtcDropTransformOptions,
} from './webrtc-contract';

const { infoLog, warnLog } = getLogs('VideoPipeline');

// One ladder tier's send encoding. `layerId` is the bottom-first wire index;
// `rid` is the RTP simulcast id (encoding order in getParameters matches this).
export interface LoopbackLayer {
    layerId: number;
    rid: string;
    scaleResolutionDownBy: number;
    maxBitrate: number;
    maxFramerate?: number;
}

export interface LoopbackLayerStats {
    layerId: number;
    rid: string;
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

// Per-tier hot-apply update; undefined fields keep their current value.
export interface LayerUpdate {
    maxBitrate?: number;
    scaleResolutionDownBy?: number;
    maxFramerate?: number;
    active?: boolean;
}

export interface LoopbackConnectionOptions {
    track: MediaStreamTrack;
    // Transform target — the recorder worker, which hosts onrtctransform.
    worker: Worker;
    transformOptions: WebRtcSimulcastTransformOptions;
    layers: LoopbackLayer[];
    // Pushed to the top of setCodecPreferences so the negotiated codec is ours.
    codecPreferences?: RTCRtpCodecCapability[];
}

export class LoopbackConnection {
    private outbound: RTCPeerConnection | null = null;
    private inbound: RTCPeerConnection | null = null;
    private sender: RTCRtpSender | null = null;
    private closed = false;

    constructor(private readonly opts: LoopbackConnectionOptions) {}

    getState(): { connectionState: string; encodings: unknown[] } {
        return {
            connectionState: this.outbound?.connectionState ?? 'closed',
            encodings: this.sender?.getParameters().encodings ?? [],
        };
    }

    // Per-tier (= per simulcast layer) encoder stats — one outbound-rtp report
    // per rid. `encoderImplementation`/`powerEfficientEncoder` reveal HW vs SW.
    async getStats(): Promise<LoopbackLayerStats[]> {
        const sender = this.sender;
        if (!sender)
            return [];

        const report = await sender.getStats();
        const out: LoopbackLayerStats[] = [];
        const numOf = (v: unknown): number => typeof v === 'number' ? v : 0;
        report.forEach((s: unknown) => {
            const r = s as Record<string, unknown>;
            if (r.type !== 'outbound-rtp' || r.kind !== 'video')
                return;

            const rid = typeof r.rid === 'string' ? r.rid : '';
            const layer = this.opts.layers.find(l => l.rid === rid) ?? this.opts.layers[out.length];
            out.push({
                layerId: layer.layerId,
                rid,
                width: numOf(r.frameWidth),
                height: numOf(r.frameHeight),
                framesEncoded: numOf(r.framesEncoded),
                keyFramesEncoded: numOf(r.keyFramesEncoded),
                bytesSent: numOf(r.bytesSent),
                targetBitrate: numOf(r.targetBitrate),
                fps: numOf(r.framesPerSecond),
                encoderImplementation: typeof r.encoderImplementation === 'string' ? r.encoderImplementation : '',
                powerEfficient: typeof r.powerEfficientEncoder === 'boolean' ? r.powerEfficientEncoder : undefined,
                active: typeof r.active === 'boolean' ? r.active : true,
            });
        });
        out.sort((a, b) => a.layerId - b.layerId);

        return out;
    }

    async connect(): Promise<void> {
        const outbound = new RTCPeerConnection();
        const inbound = new RTCPeerConnection();
        this.outbound = outbound;
        this.inbound = inbound;

        outbound.onicecandidate = e => e.candidate && void inbound.addIceCandidate(e.candidate);
        inbound.onicecandidate = e => e.candidate && void outbound.addIceCandidate(e.candidate);

        // Drop the echoed stream before it's decoded — the loopback exists only
        // to run the encoder. Set on `ontrack` so it's in place before any frame
        // reaches the decoder; RTP still flows so RTCP/transport-cc feedback
        // keeps the sender's BWE healthy.
        inbound.ontrack = e => {
            const dropOptions: WebRtcDropTransformOptions = { kind: 'webrtc-drop', tier: 0 };
            try {
                e.receiver.transform = new RTCRtpScriptTransform(this.opts.worker, dropOptions);
            } catch (err) {
                warnLog?.log('attach receiver drop-transform failed', err);
            }
        };
        const whenConnected = new PromiseSource<void>();
        outbound.onconnectionstatechange = () => {
            console.info(`[voxtWebRtc] simulcast PC state=${outbound.connectionState}`);
            if (outbound.connectionState === 'connected')
                whenConnected.resolve();
            else if (outbound.connectionState === 'failed')
                whenConnected.reject(new Error('simulcast PC failed'));
        };

        // Declare encodings full-resolution FIRST. On Chrome's HEVC
        // SimulcastEncoderAdapter, a bottom-first (smallest-first) order flakily
        // drops the top layer; full-res-first reliably brings up all tiers
        // (verified against the PoC). Send order is decoupled from layerId — the
        // worker tap demuxes by SSRC/resolution, and applyLayers maps by rid.
        const sendOrder = [...this.opts.layers].sort((a, b) => a.scaleResolutionDownBy - b.scaleResolutionDownBy);
        const tx = outbound.addTransceiver(this.opts.track, {
            direction: 'sendonly',
            streams: [new MediaStream([this.opts.track])],
            sendEncodings: sendOrder.map(l => ({
                rid: l.rid,
                scaleResolutionDownBy: l.scaleResolutionDownBy,
                maxBitrate: l.maxBitrate,
                ...(l.maxFramerate ? { maxFramerate: l.maxFramerate } : {}),
            })),
        });
        this.sender = tx.sender;

        if (this.opts.codecPreferences?.length && 'setCodecPreferences' in tx) {
            try {
                tx.setCodecPreferences(this.opts.codecPreferences);
            } catch (e) {
                warnLog?.log('setCodecPreferences failed', e);
            }
        }

        try {
            tx.sender.transform = new RTCRtpScriptTransform(this.opts.worker, this.opts.transformOptions);
        } catch (e) {
            warnLog?.log('attach simulcast transform failed', e);
            throw e;
        }

        const offer = await outbound.createOffer();
        await outbound.setLocalDescription(offer);
        await inbound.setRemoteDescription(offer);
        const answer = await inbound.createAnswer();
        await inbound.setLocalDescription(answer);
        // Without the munge the answerer omits the rids and the offerer drops
        // all but one encoding — simulcast collapses to a single layer.
        const mungedSdp = mungeAnswerSimulcast(inbound.localDescription?.sdp ?? answer.sdp ?? '', offer.sdp ?? '');
        await outbound.setRemoteDescription({ type: 'answer', sdp: mungedSdp });

        // Pin rate control: hold resolution, don't let WebRTC BWE/CPU adaptation
        // drop res/fps (our QC owns the ladder). Applied post-negotiation so the
        // encoding objects exist.
        this.applyLayers(this.opts.layers.map(() => ({})));

        await whenConnected;
        infoLog?.log(`simulcast loopback connected (${this.opts.layers.length} layers)`);
    }

    // Hot-apply per-tier encoding params in one setParameters round-trip.
    // `updates[layerId]` targets the bottom-first tier; null skips it. Encodings
    // are matched by rid because the send order is full-res-first, NOT layerId
    // order (see connect()).
    applyLayers(updates: readonly (LayerUpdate | null)[]): void {
        const sender = this.sender;
        if (!sender)
            return;

        const params = sender.getParameters();
        params.degradationPreference = 'maintain-resolution';
        for (let layerId = 0; layerId < updates.length && layerId < this.opts.layers.length; layerId++) {
            const u = updates[layerId];
            if (!u)
                continue;

            const rid = this.opts.layers[layerId].rid;
            const enc = params.encodings.find(e => e.rid === rid);
            if (!enc)
                continue;

            if (u.maxBitrate !== undefined) enc.maxBitrate = u.maxBitrate;
            if (u.scaleResolutionDownBy !== undefined) enc.scaleResolutionDownBy = u.scaleResolutionDownBy;
            if (u.maxFramerate !== undefined) enc.maxFramerate = u.maxFramerate;
            if (u.active !== undefined) enc.active = u.active;
        }
        void sender.setParameters(params).catch((e: unknown) => warnLog?.log('setParameters failed', e));
    }

    close(): void {
        if (this.closed)
            return;

        this.closed = true;
        for (const pc of [this.outbound, this.inbound]) {
            if (!pc)
                continue;

            try {
                pc.onicecandidate = null;
                pc.onconnectionstatechange = null;
                pc.ontrack = null;
                pc.close();
            } catch { /* ignore */ }
        }
        this.outbound = null;
        this.inbound = null;
        this.sender = null;
    }
}
