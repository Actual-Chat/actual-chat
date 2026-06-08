// Worker-side WebRTC tap. Installed in the recorder worker's global scope:
// `self.onrtctransform` fires once per tier transform main attaches. Each
// encoded RTCEncodedVideoFrame is mapped to the existing VideoStreamFrame wire
// DTO and pushed into the same `createSender` → push-to-pull-buffer → RpcStream
// path the WebCodecs pipeline uses. Frames are enqueued onward too, keeping the
// loopback PC's RTCP/BWE alive.

import { getLogs } from 'logging';
import { FloodGate } from '../../operators/flood-gate';
import type { StreamSenderLike, VideoStreamFrame } from '../../operators/wire-send';
import {
    isWebRtcTierTransformOptions,
    type WebRtcStartOptions,
} from './webrtc-contract';

const { infoLog, warnLog } = getLogs('VideoPipeline');

// Unconditional worker-realm console output for the debug harness — appears in
// DevTools under the recorder worker context.
const C = (...a: unknown[]): void => console.info('[voxtWebRtc/worker]', ...a);

const TICKS_PER_MICROSECOND = 10;

interface TierCounter {
    index: number;
    lastKfIndex: number;
}

interface TapState {
    sender: StreamSenderLike;
    floodGate: FloodGate;
    opts: WebRtcStartOptions;
    // performance.now() at the first encoded frame; anchors stream-relative offset.
    startMs: number;
    initSent: boolean;
    tiers: Map<number, TierCounter>;
    transformers: Map<number, RTCRtpScriptTransformer>;
}

let state: TapState | null = null;

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
    webRtcGenerateKeyFrame: (tier: number) => Promise<void>;
}

export function installWebRtcTap(deps: WebRtcTapDeps): WebRtcTapHandlers {
    const scope = self as unknown as DedicatedWorkerGlobalScope;
    scope.onrtctransform = (event): void => {
        const transformer = event.transformer;
        const opts = transformer.options;
        if (!isWebRtcTierTransformOptions(opts)) {
            // Not ours — pass frames through untouched.
            void pipePassthrough(transformer);
            return;
        }
        C(`onrtctransform: tier ${opts.tier} attached`);
        state?.transformers.set(opts.tier, transformer);
        void pumpTier(transformer, opts);
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
                tiers: new Map(),
                transformers: new Map(),
            };
            C(`webRtcStart: chatId=${opts.chatId} layers=${opts.layerCount} codec=${opts.format.codec}`);
            infoLog?.log(`webRtcStart: chatId=${opts.chatId} layers=${opts.layerCount} codec=${opts.format.codec}`);
        },
        webRtcStop(): void {
            stopTap();
        },
        async webRtcGenerateKeyFrame(tier: number): Promise<void> {
            const t = state?.transformers.get(tier);
            if (!t || typeof t.generateKeyFrame !== 'function') return;
            try { await t.generateKeyFrame(); }
            catch (e) { warnLog?.log(`generateKeyFrame(tier=${tier}) failed`, e); }
        },
    };
}

function stopTap(): void {
    const s = state;
    state = null;
    if (!s) return;
    try { s.sender.dispose?.(); } catch { /* ignore */ }
    s.tiers.clear();
    s.transformers.clear();
    infoLog?.log('webRtcStop: tap disposed');
}

async function pumpTier(
    transformer: RTCRtpScriptTransformer,
    opts: import('./webrtc-contract').WebRtcTierTransformOptions,
): Promise<void> {
    const reader = transformer.readable.getReader();
    const writer = transformer.writable.getWriter();
    try {
        for (;;) {
            const { value: frame, done } = await reader.read();
            if (done) break;
            try { onEncodedFrame(opts, frame); }
            catch (e) { warnLog?.log(`onEncodedFrame(tier=${opts.tier}) failed`, e); }
            // Keep the loopback alive (RTCP/BWE) — we copied the bytes above.
            await writer.write(frame);
        }
    } catch (e) {
        warnLog?.log(`pumpTier(tier=${opts.tier}) ended`, e);
    }
}

function onEncodedFrame(
    opts: import('./webrtc-contract').WebRtcTierTransformOptions,
    frame: RTCEncodedVideoFrame,
): void {
    const s = state;
    if (!s) return;

    const now = performance.now();
    if (Number.isNaN(s.startMs)) s.startMs = now;

    let tc = s.tiers.get(opts.tier);
    if (!tc) { tc = { index: 0, lastKfIndex: -1 }; s.tiers.set(opts.tier, tc); }

    const isKey = frame.type === 'key';
    const index = tc.index++;
    if (index === 0)
        C(`first encoded frame tapped: tier ${opts.tier} type=${frame.type} ${frame.data.byteLength}B`);
    if (isKey) tc.lastKfIndex = index;
    // -1 sentinel until this tier's first keyframe — server/receiver won't
    // misclassify a pre-keyframe delta as a keyframe.
    const keyFrameIndex = tc.lastKfIndex;

    const offsetMicros = Math.round((now - s.startMs) * 1000);
    const offset = offsetMicros * TICKS_PER_MICROSECOND;
    const duration = s.opts.frameDurationMicros * TICKS_PER_MICROSECOND;

    const src = new Uint8Array(frame.data);
    const data = new Uint8Array(src.byteLength);
    data.set(src);

    const meta = frame.getMetadata();
    const dto: VideoStreamFrame = {
        offset,
        offsetEpoch: 0,
        duration,
        keyFrameIndex,
        index,
        width: meta.width ?? opts.width,
        height: meta.height ?? opts.height,
        data,
        layerId: opts.layerId,
        layerCount: opts.layerCount,
    };
    if (isKey) dto.codec = opts.codec;

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
        C(`sender.init() fired (first keyframe, tier ${opts.tier}) — PushStream starting`);
    }
    if (s.initSent) {
        void s.sender.send({ layers: [dto] });
        if (index > 0 && index % 90 === 0)
            C(`tier ${opts.tier}: ${index} frames tapped & pushed`);
    }
}

async function pipePassthrough(transformer: RTCRtpScriptTransformer): Promise<void> {
    try { await transformer.readable.pipeTo(transformer.writable); }
    catch { /* transform ended */ }
}
