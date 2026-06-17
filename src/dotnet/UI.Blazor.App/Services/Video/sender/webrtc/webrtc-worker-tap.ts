// Worker-side WebRTC tap. Installed in the recorder worker's global scope:
// `self.onrtctransform` fires once for the sender's single simulcast transform.
// That one transform carries ALL ladder layers interleaved; we demux them to
// tiers by SSRC (`metadata.rid` is absent on Chrome) and map each frame to the
// existing VideoStreamFrame wire DTO, pushed into the same `createSender` →
// push-to-pull-buffer → RpcStream path the WebCodecs pipeline uses. Frames are
// enqueued onward too, keeping the loopback PC's RTCP/BWE alive.

import { getLogs } from 'logging';
import { FloodGate } from '../../operators/flood-gate';
import type { StreamSenderLike, VideoStreamFrame } from '../../operators/wire-send';
import {
    isWebRtcSimulcastTransformOptions,
    isWebRtcDropTransformOptions,
    type WebRtcStartOptions,
    type WebRtcSimulcastTransformOptions,
} from './webrtc-contract';

const { infoLog, warnLog } = getLogs('VideoPipeline');

// Unconditional worker-realm console output for the debug harness — appears in
// DevTools under the recorder worker context.
const C = (...a: unknown[]): void => console.info('[voxtWebRtc/worker]', ...a);

const TICKS_PER_MICROSECOND = 10;
const RTP_CLOCK_HZ = 90000;

interface TierCounter {
    index: number;
    lastKfIndex: number;
    // False until this tier's wire stream (re)starts at a keyframe. A WebRTC tap
    // can't drop individual deltas (it breaks the decoder reference chain), so a
    // tier resumes forwarding only on a keyframe after the gate (re)opens.
    forwarding: boolean;
}

// Per-SSRC layer assignment + timestamp anchor. `baseOffsetMicros` seeds the
// tier's stream-relative start from arrival (layers begin within a few ms of
// each other); per-frame offset then advances on the RTP clock, so intra-layer
// cadence is jitter-free and all tiers share one capture timeline.
interface SsrcAnchor {
    layerId: number;
    baseRtp: number;
    baseOffsetMicros: number;
}

interface TapState {
    sender: StreamSenderLike;
    floodGate: FloodGate;
    opts: WebRtcStartOptions;
    // performance.now() at the first encoded frame; anchors stream-relative offset.
    startMs: number;
    initSent: boolean;
    // Wire gate: when false the encoder keeps running (loopback fed) but no frame
    // reaches our wire sender — warmup semantics, mirrors the WebCodecs wireGate.
    gateOpen: boolean;
    tiers: Map<number, TierCounter>;
    ssrcAnchors: Map<number, SsrcAnchor>;
    transformer: RTCRtpScriptTransformer | null;
    // Cumulative top-tier frames actually pushed to the wire sender.
    topTierFramesSent: number;
}

let state: TapState | null = null;

// Loopback-receiver (drop) transforms, one per simulcast tier. Chrome's send
// transform has no generateKeyFrame, but the RECEIVER transform's
// sendKeyFrameRequest() emits an RTCP PLI → the loopback encoder produces a real
// keyframe (no active-toggle, so no HEVC collapse). Keyed by tier so a keyframe
// request can target a single layer.
const dropTransformers = new Map<number, RTCRtpScriptTransformer>();

export interface WebRtcTapDeps {
    createSender: (chatId: string, floodGate: FloodGate) => StreamSenderLike;
    configureStreaming: (opts: {
        chatId: string;
        apiUrl: string;
        sourceKind?: number;
        serverClockOffsetMs?: number;
    }) => void;
}

export interface WebRtcTapHandlers {
    webRtcStart: (opts: WebRtcStartOptions) => void;
    webRtcStop: () => void;
    webRtcSetGateOpen: (open: boolean) => void;
    webRtcGenerateKeyFrame: () => Promise<void>;
    webRtcGetSentFrameCount: () => number;
}

export function installWebRtcTap(deps: WebRtcTapDeps): WebRtcTapHandlers {
    const scope = self as unknown as DedicatedWorkerGlobalScope;
    scope.onrtctransform = (event): void => {
        const transformer = event.transformer;
        const opts = transformer.options;
        if (isWebRtcDropTransformOptions(opts)) {
            // Loopback receiver: discard frames before the decoder (no decode).
            // Keep the transformer so a keyframe request can PLI this tier.
            C(`onrtctransform: receiver drop attached (tier ${opts.tier})`);
            dropTransformers.set(opts.tier, transformer);
            void pumpDrop(transformer);

            return;
        }
        if (!isWebRtcSimulcastTransformOptions(opts)) {
            // Not ours — pass frames through untouched.
            void pipePassthrough(transformer);

            return;
        }
        C(`onrtctransform: simulcast transform attached (${opts.ladder.length} layers)`);
        if (state)
            state.transformer = transformer;

        void pumpSimulcast(transformer, opts);
    };

    return {
        webRtcStart(opts: WebRtcStartOptions): void {
            if (state) {
                warnLog?.log('webRtcStart: already running — stopping previous');
                stopTap();
            }
            deps.configureStreaming({
                chatId: opts.chatId,
                apiUrl: opts.apiUrl,
                sourceKind: opts.sourceKind,
                serverClockOffsetMs: opts.serverClockOffsetMs,
            });
            const floodGate = new FloodGate();
            const sender = deps.createSender(opts.chatId, floodGate);
            state = {
                sender,
                floodGate,
                opts,
                startMs: Number.NaN,
                initSent: false,
                gateOpen: opts.initialGateOpen ?? true,
                tiers: new Map(),
                ssrcAnchors: new Map(),
                transformer: null,
                topTierFramesSent: 0,
            };
            C(`webRtcStart: chatId=${opts.chatId} layers=${opts.layerCount} codec=${opts.format.codec}`);
            infoLog?.log(`webRtcStart: chatId=${opts.chatId} layers=${opts.layerCount} codec=${opts.format.codec}`);
        },
        webRtcStop(): void {
            stopTap();
        },
        webRtcSetGateOpen(open: boolean): void {
            if (!state)
                return;

            state.gateOpen = open;
            C(`webRtcSetGateOpen: ${open}`);
        },
        async webRtcGenerateKeyFrame(): Promise<void> {
            // Safari: the send transform can ask its own encoder directly.
            const sendT = state?.transformer as (RTCRtpScriptTransformer & {
                generateKeyFrame?: () => Promise<void>;
            }) | null | undefined;
            if (sendT && typeof sendT.generateKeyFrame === 'function') {
                try {
                    await sendT.generateKeyFrame();
                    return;
                } catch (e) {
                    warnLog?.log('generateKeyFrame failed', e);
                }
            }
            // Chrome: PLI each tier's loopback receiver → encoder re-keys that layer.
            const requests: Promise<void>[] = [];
            for (const t of dropTransformers.values()) {
                const recvT = t as RTCRtpScriptTransformer & {
                    sendKeyFrameRequest?: () => Promise<void> | void;
                };
                if (typeof recvT.sendKeyFrameRequest !== 'function')
                    continue;

                try {
                    requests.push(Promise.resolve(recvT.sendKeyFrameRequest()));
                } catch (e) {
                    warnLog?.log('sendKeyFrameRequest failed', e);
                }
            }
            await Promise.allSettled(requests);
        },
        webRtcGetSentFrameCount(): number {
            return state?.topTierFramesSent ?? 0;
        },
    };
}

// Private methods

function stopTap(): void {
    const s = state;
    state = null;
    if (!s)
        return;

    try {
        s.sender.dispose?.();
    } catch { /* ignore */ }
    s.tiers.clear();
    s.ssrcAnchors.clear();
    dropTransformers.clear();
    infoLog?.log('webRtcStop: tap disposed');
}

async function pumpSimulcast(
    transformer: RTCRtpScriptTransformer,
    opts: WebRtcSimulcastTransformOptions,
): Promise<void> {
    const reader = transformer.readable.getReader();
    const writer = transformer.writable.getWriter();
    try {
        for (;;) {
            const { value: frame, done } = await reader.read();
            if (done)
                break;

            try {
                onEncodedFrame(opts, frame);
            } catch (e) {
                warnLog?.log('onEncodedFrame failed', e);
            }
            // Keep the loopback alive (RTCP/BWE) — we copied the bytes above.
            await writer.write(frame);
        }
    } catch (e) {
        warnLog?.log('pumpSimulcast ended', e);
    }
}

// Maps an encoded frame's resolution to a bottom-first ladder layerId. Chrome
// omits `rid` from the frame metadata, so resolution is the demux key (paired
// with the SSRC anchor that caches the result per substream).
function layerIdForWidth(opts: WebRtcSimulcastTransformOptions, width: number | undefined): number | null {
    if (width == null)
        return null;

    let best: number | null = null;
    let bestDiff = Number.POSITIVE_INFINITY;
    for (const l of opts.ladder) {
        const diff = Math.abs(l.width - width);
        if (diff < bestDiff) {
            bestDiff = diff;
            best = l.layerId;
        }
    }

    return best;
}

function onEncodedFrame(
    opts: WebRtcSimulcastTransformOptions,
    frame: RTCEncodedVideoFrame,
): void {
    const s = state;
    if (!s)
        return;

    const now = performance.now();
    if (Number.isNaN(s.startMs))
        s.startMs = now;

    const meta = frame.getMetadata();
    const ssrc = meta.synchronizationSource ?? 0;
    const rtpTs = meta.rtpTimestamp ?? frame.timestamp;

    let anchor = s.ssrcAnchors.get(ssrc);
    if (!anchor) {
        const layerId = layerIdForWidth(opts, meta.width);
        if (layerId == null)
            // No resolution yet (rare leading delta) — can't classify; drop it.
            return;

        anchor = { layerId, baseRtp: rtpTs, baseOffsetMicros: (now - s.startMs) * 1000 };
        s.ssrcAnchors.set(ssrc, anchor);
        C(`layer ${layerId} ← ssrc ${ssrc} (${meta.width}x${meta.height})`);
    }
    const layerId = anchor.layerId;

    let tc = s.tiers.get(layerId);
    if (!tc) {
        tc = { index: 0, lastKfIndex: -1, forwarding: false };
        s.tiers.set(layerId, tc);
    }

    const isKey = frame.type === 'key';
    const index = tc.index++;
    if (isKey)
        tc.lastKfIndex = index;

    // -1 sentinel until this tier's first keyframe — server/receiver won't
    // misclassify a pre-keyframe delta as a keyframe.
    const keyFrameIndex = tc.lastKfIndex;

    // Wire gate (checked before the byte copy so a closed gate is cheap). When
    // closed the encoder keeps running but nothing reaches our sender; on reopen
    // a tier resumes forwarding only at a keyframe (deltas before it are
    // undecodable on their own).
    if (!s.gateOpen) {
        tc.forwarding = false;

        return;
    }
    if (!tc.forwarding) {
        if (!isKey)
            return;

        tc.forwarding = true;
    }

    const offsetMicros = anchor.baseOffsetMicros + ((rtpTs - anchor.baseRtp) / RTP_CLOCK_HZ) * 1e6;
    const offset = Math.round(offsetMicros) * TICKS_PER_MICROSECOND;
    const duration = s.opts.frameDurationMicros * TICKS_PER_MICROSECOND;

    const src = new Uint8Array(frame.data);
    const data = new Uint8Array(src.byteLength);
    data.set(src);

    const dto: VideoStreamFrame = {
        offset,
        offsetEpoch: 0,
        duration,
        keyFrameIndex,
        index,
        width: meta.width ?? 0,
        height: meta.height ?? 0,
        data,
        layerId,
        layerCount: s.opts.layerCount,
    };
    if (isKey)
        dto.codec = opts.codec;

    // First keyframe on any tier announces the stream (sender.init → PushStream).
    if (!s.initSent && isKey && s.sender.init) {
        s.sender.init({
            codec: s.opts.format.codec,
            width: s.opts.format.width,
            height: s.opts.format.height,
            sourceWidth: s.opts.format.sourceWidth,
            sourceHeight: s.opts.format.sourceHeight,
            codecSettings: s.opts.format.codecSettings,
        });
        s.initSent = true;
        C(`sender.init() fired (first keyframe, layer ${layerId}) — PushStream starting`);
    }
    if (s.initSent) {
        void s.sender.send({ layers: [dto] });
        // Top tier encodes every source moment → its count is the send fps.
        if (layerId === s.opts.layerCount - 1)
            s.topTierFramesSent++;
    }
}

async function pipePassthrough(transformer: RTCRtpScriptTransformer): Promise<void> {
    try {
        await transformer.readable.pipeTo(transformer.writable);
    } catch { /* transform ended */ }
}

// Receiver-side: drain the readable and DROP every frame (never enqueue to the
// writable) so the decoder downstream never runs. We keep reading so RTP
// processing/feedback isn't back-pressured. The writable is left unwritten.
async function pumpDrop(transformer: RTCRtpScriptTransformer): Promise<void> {
    const reader = transformer.readable.getReader();
    try {
        for (;;) {
            const { done } = await reader.read();
            if (done)
                break;
            // frame intentionally discarded
        }
    } catch { /* transform ended */ }
}
