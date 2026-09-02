import { getLogs } from 'logging';
import { kbpsToBitsPerSecond } from 'app-constants';
import { DeviceInfo } from 'device-info';
import { WebCodecsCompat } from 'web-codecs-compat/init';
import { isDecoderCodecProven, isEncoderCodecProven } from './codec-proof';
import { closeEncodedChunk } from './frame-envelopes';

export {
    getProvenDecoderCodecs,
    getProvenEncoderCodecs,
    isDecoderCodecProven,
    isEncoderCodecProven,
    markDecoderCodecProven,
    markEncoderCodecProven,
} from './codec-proof';

const { debugLog, infoLog, warnLog, errorLog } = getLogs('VideoPipeline');

export type CodecCategory = 'h264' | 'hevc' | 'av1' | 'vp9';

export interface CodecInfo {
    name: string;
    codec: string;
    category: 'h264' | 'hevc' | 'av1' | 'vp9';
    supported: boolean;
    // Probed independently, one acceleration mode at a time. `isConfigSupported`
    // echoes back whatever hardwareAcceleration was asked for, so the echo says
    // nothing; what carries information is that a mode can come back
    // unsupported — VP9 has no hardware encoder on plenty of machines, HEVC no
    // software one in Chromium.
    hardwareSupported: boolean;
    softwareSupported: boolean;
    // True when a hardware encoder exists. Kept as the name older call sites use.
    hardwareAccelerated: boolean;
    // False when the encoder buffers so many frames before its first output
    // that it can't be used for a call. Undefined when not measured.
    realtime?: boolean;
}

const CODEC_PROFILES = {
    h264: [
        { name: 'H.264 High 5.2', codec: 'avc1.640034' },
        { name: 'H.264 High 5.1', codec: 'avc1.640033' },
        { name: 'H.264 High 4.1', codec: 'avc1.640029' },
        { name: 'H.264 High 4.0', codec: 'avc1.640028' },
        { name: 'H.264 High 3.1', codec: 'avc1.64001F' },
        { name: 'H.264 High 3.0', codec: 'avc1.64001E' },
        { name: 'H.264 Main 5.2', codec: 'avc1.4D4034' },
        { name: 'H.264 Main 4.1', codec: 'avc1.4D4029' },
        { name: 'H.264 Main 3.1', codec: 'avc1.4D401F' },
        { name: 'H.264 Main 3.0', codec: 'avc1.4D401E' },
        { name: 'H.264 Baseline 3.1', codec: 'avc1.42E01F' },
        { name: 'H.264 Baseline 3.0', codec: 'avc1.42E01E' },
    ],
    hevc: [
        { name: 'HEVC Main, Level 4.1', codec: 'hev1.1.6.L123.B0' },
        { name: 'HEVC Main, Level 4.0', codec: 'hev1.1.6.L120.B0' },
        { name: 'HEVC Main, Level 3.1', codec: 'hev1.1.6.L93.B0' },
        { name: 'HEVC Main, Level 3.0', codec: 'hev1.1.6.L90.B0' },
        { name: 'HEVC Main 10, Level 4.0', codec: 'hev1.2.4.L120.B0' },
        { name: 'HEVC Main 10, Level 4.1', codec: 'hev1.2.4.L123.B0' },
    ],
    vp9: [
        { name: 'VP9 Profile 0, Level 4.1', codec: 'vp09.00.41.08' },
        { name: 'VP9 Profile 0, Level 4.0', codec: 'vp09.00.40.08' },
        { name: 'VP9 Profile 0, Level 3.1', codec: 'vp09.00.31.08' },
        { name: 'VP9 Profile 0, Level 3.0', codec: 'vp09.00.30.08' },
    ],
    av1: [
        { name: 'AV1 Main, Level 3.0', codec: 'av01.0.05M.08' },
        { name: 'AV1 Main, Level 4.0', codec: 'av01.0.08M.08' },
        { name: 'AV1 Main, Level 4.1', codec: 'av01.0.09M.08' },
        { name: 'AV1 Main, Level 5.0', codec: 'av01.0.12M.08' },
        { name: 'AV1 High, Level 4.0', codec: 'av01.1.08M.08' },
        { name: 'AV1 High, Level 4.1', codec: 'av01.1.09M.08' },
    ],
};

// The one codec every client is required to speak, and so the only one no
// exclusion path may drop. It is VP9 rather than H.264 on measurement — see
// docs/live-video/03-codecs-and-layers.md for the numbers.
export const FLOOR_CATEGORY = 'vp9';

// Advertised ahead of a forced codec. Not a codec — a marker saying "this list
// is a pin". The server honours it only for admins; for everyone else it is
// stripped and the list is treated as an ordinary capability report.
export const FORCED_CODEC_MARKER = 'forced';

// Codec support is per UA+OS for the page lifetime, so each (W,H) probes once.
// In-flight Promise stored so concurrent callers share work.
const encoderCodecCache = new Map<string, Promise<CodecInfo[]>>();

// Debug overrides set from VideoDiagnosticsModal, both persisted. Note the
// asymmetry in blast radius: the decode force narrows what this client
// advertises, so it changes the codec for EVERY member of the call, while the
// encode preference only reorders this client's own candidates.
const FORCE_DECODE_CODEC_KEY = 'video.debug.forceDecodeCodec';
const PREFERRED_ENCODE_CODEC_KEY = 'video.debug.preferredEncodeCodec';

const CODEC_CATEGORIES: readonly CodecCategory[] = ['h264', 'hevc', 'vp9', 'av1'];

// `globalThis.localStorage` is read inside the try on purpose: the property
// access itself throws SecurityError when site data is blocked, so passing the
// store in as an argument would put the throw outside the guard.
function readCategory(key: string): CodecCategory | null {
    try {
        const value = globalThis.localStorage.getItem(key) as CodecCategory | null;
        return value && CODEC_CATEGORIES.includes(value) ? value : null;
    } catch {
        return null;
    }
}

function writeCategory(key: string, value: CodecCategory | null): void {
    try {
        if (value) globalThis.localStorage.setItem(key, value);
        else globalThis.localStorage.removeItem(key);
    } catch (e) {
        warnLog?.log(`codec debug override write failed for ${key}: ${String(e)}`);
    }
}

export function getForceDecodeCodec(): CodecCategory | null {
    return readCategory(FORCE_DECODE_CODEC_KEY);
}

// Advertised as a pin rather than a capability report, so the server neither
// intersects it with the other members nor pads it with the floor.
export function setForceDecodeCodec(codec: CodecCategory | null): void {
    writeCategory(FORCE_DECODE_CODEC_KEY, codec);
    decoderCodecCache = null;
    infoLog?.log(`Debug: forceDecodeCodec set to ${codec ?? '(none)'}; decoder detection cache cleared`);
}

export function getPreferredEncodeCodec(): CodecCategory | null {
    return readCategory(PREFERRED_ENCODE_CODEC_KEY);
}

// A preference, not a restriction: if the codec turns out to be unsupported or
// too slow it simply loses to the normal ordering rather than breaking capture.
export function setPreferredEncodeCodec(codec: CodecCategory | null): void {
    writeCategory(PREFERRED_ENCODE_CODEC_KEY, codec);
    encoderCodecCache.clear();
    infoLog?.log(`Debug: preferredEncodeCodec set to ${codec ?? '(none)'}; encoder detection cache cleared`);
}


export function detectSupportedCodecs(width = 1920, height = 1080): Promise<CodecInfo[]> {
    const key = `${width}x${height}`;
    let cached = encoderCodecCache.get(key);
    if (!cached) {
        cached = detectSupportedCodecsUncached(width, height).then(result => {
            // Only a TRANSIENT failure goes uncached. A codec measured as too
            // slow is a stable property of this browser — Firefox's H.264 is
            // ~18 frames behind on every run — and caching it is what keeps
            // detection off the critical path. Caching nothing whenever any
            // codec was disqualified meant Firefox re-ran the whole latency
            // probe before every start and every recovery.
            if (!result.isStable)
                encoderCodecCache.delete(key);

            return result.codecs;
        }).catch((e: unknown) => {
            encoderCodecCache.delete(key);
            throw e;
        });
        encoderCodecCache.set(key, cached);
    }
    return cached;
}

// Single source of truth for encoder category enable/disable + priority.
// Probe ONE representative codec per category at the target resolution, in
// priority order. The actual encoder profile string is then derived from
// getCodecForCategory(category, w, h). Reduces startup probes ~7× vs per-profile.
//
// One representative profile per category, and the order detection probes them
// in. NOT a preference: which codec a sender uses comes from ENCODER_LADDER,
// which ranks (codec, acceleration) pairs and puts hardware AV1 first. The
// only thing order does here is decide which category is measured first.
const REPRESENTATIVE_CODECS: { category: CodecInfo['category']; name: string; codec: string }[] = [
    { category: 'hevc', name: 'HEVC Main L3.1',     codec: 'hev1.1.6.L93.B0' },
    { category: 'vp9',  name: 'VP9 Profile 0 L3.1', codec: 'vp09.00.31.08' },
    { category: 'h264', name: 'H.264 CBP 3.1',      codec: 'avc1.42E01F' },
    { category: 'av1',  name: 'AV1 Main L3.0',      codec: 'av01.0.05M.08' },
];

// Active encoder categories in priority order (best-first). Derived from
// REPRESENTATIVE_CODECS so callers outside detection (e.g. JoinVideoCallModal's
// pre-flight probe) stay in sync with what's actually enabled.
export function getActiveEncoderCategoriesByPriority(): readonly CodecInfo['category'][] {
    return REPRESENTATIVE_CODECS.map(c => c.category);
}

interface DetectionResult {
    codecs: CodecInfo[];
    // False when a probe failed rather than measured — see detectSupportedCodecs.
    isStable: boolean;
}

async function detectSupportedCodecsUncached(width: number, height: number): Promise<DetectionResult> {
    const level = WebCodecsCompat.level;
    let probeList = REPRESENTATIVE_CODECS;
    // Above `none`, libav.js is the only encoder we use. Decoding is unaffected:
    // detectSupportedDecoderCodecs still probes and advertises every category.
    if (level !== 'none')
        probeList = probeList.filter(c => c.category === 'vp9');
    if (excludedEncoderCodecs.size > 0) {
        const before = probeList.length;
        probeList = probeList.filter(c => !excludedEncoderCodecs.has(c.category));
        if (probeList.length < before) {
            infoLog?.log(
                `Encoder detection skipping excluded categories: ` +
                `[${[...excludedEncoderCodecs].join(', ')}]`);
        }
    }
    const results: CodecInfo[] = [];
    let isStable = true;
    for (const { category } of probeList) {
        if (category === 'vp9' && level !== 'none') {
            // libav.js replaces this encoder, so probing the browser's measures
            // one that never runs - and on Firefox that probe is what wrongly
            // clears its own VP9 for real-time use in the first place.
            results.push(getLibavVp9CodecInfo(width, height));
            continue;
        }

        const ladder = getEncoderCodecLadder(category, width, height)
            .filter(c => !excludedEncoderCodecStrings.has(c));
        let chosen: { codec: string; hardware: boolean; software: boolean } | null = null;
        for (const codec of ladder) {
            const { hardware, software } = await isCodecSupported(codec, category, width, height);
            if (hardware || software) {
                chosen = { codec, hardware, software };
                break;
            }
        }
        const codec = chosen?.codec
            ?? (ladder.length > 0 ? ladder[0] : getCodecForCategory(category, width, height));
        let realtime: boolean | undefined;
        if (chosen !== null) {
            // The acceleration the codec was actually accepted with, not the
            // default: VP9 is rejected outright under prefer-hardware here, so
            // probing it that way measures an encoder that never gets built.
            const frames = await probeEncoderLatencyFrames(
                codec,
                category,
                chosen.hardware ? 'prefer-hardware' : 'prefer-software');
            // The probe reports MAX only when the encoder produced nothing at
            // all; a real measurement returns the depth it settled at.
            if (frames >= MAX_LATENCY_PROBE_FRAMES)
                isStable = false;
            realtime = frames <= MAX_REALTIME_LATENCY_FRAMES;
            if (!realtime) {
                warnLog?.log(
                    `Encoder ${codec}: stays ${frames} frames behind once warm `
                    + `(> ${MAX_REALTIME_LATENCY_FRAMES}) — not usable for calls`);
            }
        }
        results.push({
            name: getCodecProfileName(codec) ?? codec,
            codec,
            category,
            supported: chosen !== null,
            hardwareSupported: chosen?.hardware ?? false,
            softwareSupported: chosen?.software ?? false,
            hardwareAccelerated: chosen?.hardware ?? false,
            realtime,
        });
    }
    const supported = results.filter(c => c.supported);
    infoLog?.log(`detectSupportedCodecsUncached: ${supported.map(c =>
        `${c.codec}(${[c.hardwareSupported ? 'hw' : '', c.softwareSupported ? 'sw' : ''].filter(Boolean).join('+')})`)
        .join(', ') || 'none'}`);
    return { codecs: results, isStable };
}

async function isCodecSupported(
    codec: string,
    category: 'h264' | 'hevc' | 'av1' | 'vp9',
    width: number,
    height: number
): Promise<{ hardware: boolean; software: boolean }> {
    try {
        const baseConfig: VideoEncoderConfig = {
            codec,
            width,
            height,
            bitrate: 5_000_000,
            framerate: 30,
            latencyMode: 'realtime',
        };

        if (category === 'h264') {
            baseConfig.avc = { format: 'annexb' };
        }

        // Ask each mode separately and believe the answers, not the echoed
        // config. Firefox reports no hardware encoder for anything, so there
        // it is the software column that carries the support.
        const ask = async (accel: HardwareAcceleration): Promise<boolean> => {
            const support = await VideoEncoder.isConfigSupported({ ...baseConfig, hardwareAcceleration: accel });
            return support.supported === true;
        };
        const hardware = await ask('prefer-hardware');
        const software = await ask('prefer-software');

        debugLog?.log(`Encoder ${codec}: hw=${hardware}, sw=${software}`);
        return { hardware, software };
    } catch (error) {
        errorLog?.log(`Error checking codec support for ${codec}:`, error);
        return { hardware: false, software: false };
    }
}

// An encoder that only emits its first chunk after this many submitted frames
// is unusable for a call: at 30fps each frame is ~33ms of added latency, and
// the pipeline would have to hold that many bundles in flight to avoid
// deadlocking. Firefox's H.264 sits at 18; every other engine/codec measured
// emits within 2.
export const MAX_REALTIME_LATENCY_FRAMES = 4;
export const MAX_LATENCY_PROBE_FRAMES = 24;
// Submissions are paced at the frame interval. Without it the whole probe runs
// inside a few milliseconds, no encoder has emitted anything yet, and every
// codec measures as infinitely deep — which is exactly what disqualified all
// four of them on Chromium.
const LATENCY_PROBE_FRAME_INTERVAL_MS = 33;
// Consecutive frames at or under the threshold that end the probe early.
const LATENCY_PROBE_STEADY_FRAMES = 6;
const latencyProbeCache = new Map<string, Promise<number>>();

// Steady-state encoder depth: how many frames stay in flight once the encoder
// is warm. Deliberately a *count*, not a duration: an earlier synthetic
// throughput probe was removed from this file because GPU contention made it
// false-fail on healthy machines. A frame count doesn't move when the machine
// is busy — it's a property of the encoder's pipeline, so it stays honest
// under load.
//
// Startup is excluded on measurement: Chromium's hardware AV1/H.264/HEVC
// encoders all emit their first chunk ~215ms in and then track submissions
// exactly, so first-chunk latency measures one-off initialisation, while depth
// after warm-up is what actually costs a call latency. Firefox's H.264 stays
// ~18 frames behind forever, so it still fails.
export function probeEncoderLatencyFrames(
    codec: string,
    category: CodecInfo['category'],
    hardwareAcceleration: HardwareAcceleration,
): Promise<number> {
    const key = `${codec}@${hardwareAcceleration}`;
    let cached = latencyProbeCache.get(key);
    if (!cached) {
        cached = probeEncoderLatencyFramesUncached(codec, category, hardwareAcceleration)
            .then(frames => {
                // A disqualifying result is not cached. It can come from a
                // transient encoder error — a GPU reset, or losing the race for
                // an encoder session against the live call — and caching that
                // would retire a working codec for the rest of the page's life.
                // A passing result is a stable property of the encoder.
                if (frames > MAX_REALTIME_LATENCY_FRAMES)
                    latencyProbeCache.delete(key);

                return frames;
            })
            .catch((e: unknown) => {
                latencyProbeCache.delete(key);
                throw e;
            });
        latencyProbeCache.set(key, cached);
    }

    return cached;
}

async function probeEncoderLatencyFramesUncached(
    codec: string,
    category: CodecInfo['category'],
    hardwareAcceleration: HardwareAcceleration,
): Promise<number> {
    // Small and cheap: this measures pipeline depth, which doesn't vary with
    // resolution, so there's no reason to pay for a big frame.
    const width = 320;
    const height = 240;
    let encoder: VideoEncoder | null = null;
    try {
        // Holder rather than captured `let`s: the callbacks below write these,
        // but a captured `let` narrows to its initializer in the loop
        // condition (same reason `decode`'s feed pump uses one).
        const state = { submitted: 0, outputs: 0, failed: false };
        const config: VideoEncoderConfig = {
            codec,
            width,
            height,
            bitrate: 400_000,
            framerate: 30,
            latencyMode: 'realtime',
            hardwareAcceleration,
        };
        if (category === 'h264')
            config.avc = { format: 'annexb' };

        encoder = new VideoEncoder({
            output: (chunk: EncodedVideoChunk) => {
                state.outputs++;
                closeEncodedChunk(chunk);
            },
            error: () => { state.failed = true; },
        });
        encoder.configure(config);

        const canvas = new OffscreenCanvas(width, height);
        const ctx = canvas.getContext('2d');
        if (!ctx)
            return 0;

        // Max over the CURRENT steady run only. A global max would let the one
        // tick where the encoder drains its startup backlog stand as the
        // verdict, disqualifying an encoder that then keeps up perfectly —
        // which is exactly the startup cost this probe exists to ignore.
        let steadyMaxDepth = 0;
        let lastDepth = MAX_LATENCY_PROBE_FRAMES;
        let steady = 0;
        for (let i = 0; i < MAX_LATENCY_PROBE_FRAMES && !state.failed; i++) {
            ctx.fillStyle = `hsl(${(i * 37) % 360} 60% 50%)`;
            ctx.fillRect(0, 0, width, height);
            const frame = new VideoFrame(canvas, { timestamp: i * 33_333 });
            try {
                // Counted before the call: a codec whose output callback fires
                // synchronously would otherwise be recorded one frame early.
                state.submitted++;
                encoder.encode(frame, { keyFrame: i === 0 });
            } finally {
                frame.close();
            }
            await new Promise(resolve => setTimeout(resolve, LATENCY_PROBE_FRAME_INTERVAL_MS));
            if (state.outputs === 0)
                continue; // Still initialising; not part of steady state.

            const depth = state.submitted - state.outputs;
            lastDepth = depth;
            if (depth > MAX_REALTIME_LATENCY_FRAMES) {
                steady = 0;
                steadyMaxDepth = 0;
                continue;
            }

            steady++;
            steadyMaxDepth = Math.max(steadyMaxDepth, depth);
            if (steady >= LATENCY_PROBE_STEADY_FRAMES)
                return steadyMaxDepth;
        }
        // Never warmed up at all, or never settled: both are disqualifying.
        return state.failed || state.outputs === 0 ? MAX_LATENCY_PROBE_FRAMES : lastDepth;
    } catch (e) {
        // An unusable probe must not veto a codec that works; only a
        // successful measurement is allowed to disqualify one.
        debugLog?.log(`probeEncoderLatencyFrames(${codec}): ${String(e)}`);
        return 0;
    } finally {
        try { if (encoder && encoder.state !== 'closed') encoder.close(); } catch { /* ignore */ }
    }
}

export interface EncoderProbeResult {
    supported: boolean;
    medianEncodeMs: number;
    failedStage: 'configure' | 'encode' | null;
}

interface ProbeLayer { width: number; height: number; bitrateKbps: number }

// Cached per (codec, top-layer-dims, layer-count). Probe exercises only the top
// tier; layer count stays in the key for clean future invalidation if multi-
// encoder probing returns. Ladders are bottom-first.
const encoderProbeCache = new Map<string, Promise<EncoderProbeResult>>();

// Single-encoder probe of the top layer. PIPELINED submission: all frames
// are encode()'d back-to-back, then we await all outputs, then compute
// average per-frame wall-clock. The sequential probe (encode → await
// output → encode next) used previously measured per-frame LATENCY
// (driver-batched on HW encoders, microtask-scheduler-bound on Chrome)
// which is wildly higher than real-recording THROUGHPUT — NVENC HEVC on
// RTX 4060 has ~1-2ms steady-state encode time but ~30-60ms per-frame
// turnaround when probed sequentially. Real recording streams frames
// continuously and gets the throughput, not the latency, so the probe
// must too.
//
// budgetMs is the per-frame AVERAGE budget; total wall-clock budget =
// frameCount * budgetMs + 500ms warmup headroom. With default 50ms/frame
// over 8 frames that's 900ms total — generous enough for cold-start,
// strict enough to catch a codec that genuinely can't keep up with 30fps.
//
// Borderline codecs are meant to pass here and rely on runtime
// backpressure (step-down → pickFallbackCodec → switchCodec) to recover;
// a too-tight budget defeats that design.
//
// Frame close is DEFERRED to the output callback for the matching frame
// (WebCodecs guarantees FIFO output ordering). Closing canvas-backed
// VideoFrames before the encoder has read the GPU texture has been
// observed to make some HW encoders silently never emit; the original
// adapter has the same rule (see adapters.ts AsyncVideoEncoder).
//
// On FAIL the encoder is closed in finally — never returned to any pool —
// so the failed codec category can be excluded and the next group tried
// without leaking HW encoder slots.
export function probeEncoder(
    codec: string,
    layers: readonly ProbeLayer[],
    frameCount = 8,
    budgetMs = 50,
    hardwareAcceleration: HardwareAcceleration = getDefaultHardwareAcceleration(),
): Promise<EncoderProbeResult> {
    if (layers.length === 0)
        return Promise.resolve({ supported: false, medianEncodeMs: 0, failedStage: 'configure' });
    const top = layers[layers.length - 1];
    // Cache key includes hwAccel so the 1-tier no-preference fallback
    // doesn't collide with a prior 'prefer-hardware' probe of the same codec
    // at the same dims.
    const key = `${codec}@${top.width}x${top.height}×${layers.length}×${hardwareAcceleration}`;
    let cached = encoderProbeCache.get(key);
    if (!cached) {
        cached = probeEncoderUncached(codec, layers, frameCount, budgetMs, hardwareAcceleration);
        encoderProbeCache.set(key, cached);
    }
    return cached;
}

async function probeEncoderUncached(
    codec: string,
    layers: readonly ProbeLayer[],
    frameCount: number,
    budgetMs: number,
    hardwareAcceleration: HardwareAcceleration,
): Promise<EncoderProbeResult> {
    // Codec support is decided by `VideoEncoder.isConfigSupported` alone.
    // The previous implementation spun up a real encoder and pushed 8
    // synthetic OffscreenCanvas frames at the top resolution to verify HW
    // throughput; under GPU contention (modal preview + bg-blur shader +
    // camera capture all running) that synthetic load false-fails on
    // healthy systems, especially iOS (1 HW encoder slot) and Windows MFT.
    // Real-frame validation now happens at the running pipeline's
    // boundary: encoder errors during actual recording drive codec
    // fallback at runtime, not in a pre-flight probe.
    if (layers.length === 0)
        return { supported: false, medianEncodeMs: 0, failedStage: 'configure' };
    void frameCount;
    void budgetMs;
    const category = getCodecCategory(codec);
    const top = layers[layers.length - 1];

    try {
        const config: VideoEncoderConfig = {
            codec,
            width: top.width,
            height: top.height,
            bitrate: kbpsToBitsPerSecond(top.bitrateKbps),
            framerate: 30,
            latencyMode: 'realtime',
            hardwareAcceleration,
        };
        if (category === 'h264')
            config.avc = { format: 'annexb' };
        const support = await VideoEncoder.isConfigSupported(config);
        if (!support.supported) {
            debugLog?.log(`probeEncoder: isConfigSupported=false for ${codec} at ${top.width}x${top.height}`);
            return { supported: false, medianEncodeMs: 0, failedStage: 'configure' };
        }
        debugLog?.log(`probeEncoder: isConfigSupported=true for ${codec} at ${top.width}x${top.height} (hwAccel=${hardwareAcceleration})`);
        return { supported: true, medianEncodeMs: 0, failedStage: null };
    } catch (error) {
        errorLog?.log('probeEncoder: unexpected error', error);
        return { supported: false, medianEncodeMs: 0, failedStage: 'configure' };
    }
}

export function getCodecCategory(codecString: string): 'h264' | 'hevc' | 'av1' | 'vp9' {
    if (codecString.startsWith('av01')) return 'av1';
    if (codecString.startsWith('hev1') || codecString.startsWith('hvc1')) return 'hevc';
    if (codecString.startsWith('vp09')) return 'vp9';
    return 'h264';
}

export function getCodecForCategory(category: 'h264' | 'hevc' | 'av1' | 'vp9', width: number, height: number): string {
    // Keep the codec string CONSTANT across resolutions — Chrome's WebCodecs
    // re-inits the underlying NVENC session on any codec-string change (even
    // just a level byte), reproducing 'Encoder initialization error' storms.
    // Use the highest ladder-cap level per category (L4.0 for 1080p), bumped
    // for >1080p (4K screencast).
    const pixels = width * height;
    const ultraHi = pixels > 2_073_600; // 4K territory

    if (category === 'av1') {
        return ultraHi ? 'av01.0.12M.08' : 'av01.0.08M.08';
    }
    if (category === 'vp9') {
        return ultraHi ? 'vp09.00.50.08' : 'vp09.00.41.08';
    }
    if (category === 'hevc') {
        return ultraHi ? 'hev1.1.6.L150.B0' : 'hev1.1.6.L120.B0';
    }
    // H.264 is Constrained Baseline everywhere. Main and High compress better,
    // but both carry CABAC and B-frames, which this project does not emit; CBP
    // is also what every decoder in the fleet accepts and what the software
    // (OpenH264) encoder implements, so the HW→SW fallback keeps the same
    // profile. H.264 no longer being the negotiation floor makes the lost
    // compression cheap — VP9 is what a constrained call falls back to.
    return getSoftwareH264Codec(width, height);
}

// Chrome's software H.264 encoder (OpenH264) implements only Constrained
// Baseline — not Main/High. A High/Main string makes
// isConfigSupported({hardwareAcceleration:'prefer-software'}) return false, so
// the SW fallback must request Constrained Baseline (42E0xx). Universally
// decodable, which keeps the HW→SW switch transparent to viewers.
// Firefox rejects 'prefer-hardware' for every H.264 profile — isConfigSupported
// returns false and configure() throws NotSupportedError — while the same
// profiles encode fine under 'no-preference'. Starting there keeps the probe
// chain on the acceleration that works, so Firefox gets the full tier ladder
// instead of falling through to the 1-tier last resort.
export function getDefaultHardwareAcceleration(): HardwareAcceleration {
    return DeviceInfo.isFirefox ? 'no-preference' : 'prefer-hardware';
}

// How many bundles may sit at the encoder before the operator waits for the
// oldest. Firefox emits its first EncodedVideoChunk only after ~18 submitted
// frames (measured on 154, identical for no-preference and prefer-software),
// so the usual depth of 5 deadlocks there: we stop submitting at 5 waiting for
// an output that needs 18 submissions to appear. Everyone else returns the
// first chunk within a frame or two.
export function getEncoderPipelineDepth(): number {
    return DeviceInfo.isFirefox ? 24 : 5;
}

export function getSoftwareH264Codec(width: number, height: number): string {
    const pixels = width * height;
    if (pixels > 2_073_600)
        return 'avc1.42E034'; // L5.2 (>1080p)
    if (pixels > 921_600)
        return 'avc1.42E028'; // L4.0 (1080p)

    return 'avc1.42E01F'; // L3.1 (≤720p)
}

// Profiles to probe for one category, best-first. Detection reports the first
// entry that `isConfigSupported` accepts, so we never advertise a profile we
// didn't test. H.264 is Constrained Baseline only — a device that rejects it
// falls back to another category rather than to Main or High.
function getEncoderCodecLadder(category: CodecInfo['category'], width: number, height: number): string[] {
    return [getCodecForCategory(category, width, height)];
}

function getCodecProfileName(codec: string): string | null {
    for (const profiles of Object.values(CODEC_PROFILES)) {
        const match = profiles.find(p => p.codec === codec);
        if (match)
            return match.name;
    }

    return null;
}

// Fallback candidates in encoder-ladder order, best-first. This is the same
// ladder selection uses, so there is one device-aware priority rather than a
// second hand-rolled one that could disagree with it: on a phone or in
// Firefox the ladder already withholds the rungs those platforms shouldn't
// use, and the caller inherits that for free.
//
// Ignores the audience set and the realtime measurement on purpose — it is
// reached only when nothing passed those filters, and streaming something the
// device can encode beats streaming nothing.
export function getFallbackCodecs(supportedCodecs: CodecInfo[], width: number, height: number): string[] {
    const byCategory = new Map(supportedCodecs.filter(c => c.supported).map(c => [c.category, c]));
    const codecs: string[] = [];
    for (const rung of getEncoderLadder()) {
        const info = byCategory.get(rung.category);
        if (!info || !supportsAcceleration(info, rung.accel))
            continue;
        // A profile that already failed configure() this session would send the
        // recorder straight back into recovery.
        if (excludedEncoderCodecStrings.has(info.codec) || codecs.includes(info.codec))
            continue;

        codecs.push(info.codec);
    }

    // The floor last: a device with no usable ladder rung still has to land on
    // something, and the floor is what the rest of the call can decode.
    const floor = byCategory.get(FLOOR_CATEGORY)?.codec
        ?? getCodecForCategory(FLOOR_CATEGORY, width, height);
    if (!codecs.includes(floor))
        codecs.push(floor);

    return codecs;
}

export function getDefaultCodec(supportedCodecs: CodecInfo[], width: number, height: number): string {
    return getFallbackCodecs(supportedCodecs, width, height)[0];
}

// prefer-hardware → no-preference fallback (matches video-player.ts init).
async function isDecoderCodecSupported(codec: string, width: number, height: number): Promise<boolean> {
    for (const accel of ['prefer-hardware', 'no-preference'] as const) {
        try {
            const { supported } = await VideoDecoder.isConfigSupported({
                codec,
                codedWidth: width,
                codedHeight: height,
                hardwareAcceleration: accel,
            });
            if (supported) return true;
        } catch { /* continue */ }
    }
    return false;
}

// Categories that probe as supported but fail at runtime configure() on this
// device. Mirrors excludedDecoderCodecs.
const excludedEncoderCodecs = new Set<string>();
// Individual profiles that failed configure() on this device, e.g. a specific
// avc1.* string. Independent of the category set above.
const excludedEncoderCodecStrings = new Set<string>();

// Excludes one exact profile rather than a whole category, so a level that a
// device rejects at one resolution doesn't cost it the codec everywhere. H.264
// now has a single profile, so excluding that string does retire the category —
// which is fine: it is no longer the floor.
export function excludeEncoderCodecString(codec: string): void {
    if (getCodecCategory(codec) === FLOOR_CATEGORY)
        return;
    if (excludedEncoderCodecStrings.has(codec))
        return;

    warnLog?.log(`Excluding encoder codec: ${codec}`);
    excludedEncoderCodecStrings.add(codec);
    encoderCodecCache.clear();
}

export function isEncoderCodecStringExcluded(codec: string): boolean {
    return excludedEncoderCodecStrings.has(codec);
}

export function excludeEncoderCodec(category: string): void {
    if (category === FLOOR_CATEGORY) return; // the floor every client must speak
    if (isEncoderCodecProven(category)) {
        warnLog?.log(`excludeEncoderCodec: ignoring '${category}' — already proven this session`);
        return;
    }
    warnLog?.log(`Excluding encoder codec category: ${category}`);
    excludedEncoderCodecs.add(category);
    encoderCodecCache.clear();
}

export function getExcludedEncoderCodecs(): string[] {
    return [...excludedEncoderCodecs];
}

export function isEncoderCodecExcluded(category: string): boolean {
    return excludedEncoderCodecs.has(category);
}

// Codecs that report support but fail at runtime (wrong dims, slow decode).
const excludedDecoderCodecs = new Set<string>();

export function excludeDecoderCodec(codec: string): void {
    if (codec === FLOOR_CATEGORY) return; // the floor every client must speak
    if (isDecoderCodecProven(codec)) {
        warnLog?.log(`excludeDecoderCodec: ignoring '${codec}' — already proven this session`);
        return;
    }
    warnLog?.log(`Excluding decoder codec: ${codec}`);
    excludedDecoderCodecs.add(codec);
    decoderCodecCache = null;
}

export function getExcludedDecoderCodecs(): string[] {
    return [...excludedDecoderCodecs];
}

let decoderCodecCache: Promise<string[]> | null = null;

export function detectSupportedDecoderCodecs(): Promise<string[]> {
    decoderCodecCache ??= detectSupportedDecoderCodecsUncached();
    return decoderCodecCache;
}

/** Encoder preference, best-first, over (category, acceleration) pairs: whether a
 *  codec is worth using depends on the encoder behind it. Anything absent here is
 *  never chosen — and cannot be reached by the preferred-codec debug setting, which
 *  only reorders rungs that already qualified.
 *  Measurements in docs/live-video/codec-performance.md. */
export interface EncoderRung {
    category: CodecCategory;
    accel: HardwareAcceleration;
}

// Why each codec sits where it does in software:
//  • VP9  — first: the floor every client already decodes, 3.75ms/frame at 720p.
//  • AV1  — behind VP9: slower in software but compresses better, so it is not
//           the default yet is reachable via the preferred-codec setting.
//  • H264 — hardware only: the slowest encoder measured anywhere (6.69ms at 480p
//           on a Galaxy SM-S948U1 vs 1.17ms hardware) and compresses worst.
//  • HEVC — hardware only: Chromium ships no software HEVC encoder at all.
const ENCODER_LADDER: readonly EncoderRung[] = [
    { category: 'av1',  accel: 'prefer-hardware' },
    { category: 'vp9',  accel: 'prefer-hardware' },
    { category: 'hevc', accel: 'prefer-hardware' },
    { category: 'vp9',  accel: 'prefer-software' },
    { category: 'av1',  accel: 'prefer-software' },
    { category: 'h264', accel: 'prefer-hardware' },
];

// Firefox drops the MPEG rungs entirely: its H.264 encoder runs ~18 frames
// behind (the realtime probe already rejects it) and it ships no HEVC encoder,
// so offering either only wastes a probe.
export function getEncoderLadder(): readonly EncoderRung[] {
    return DeviceInfo.isFirefox
        ? ENCODER_LADDER.filter(rung => rung.category !== 'h264' && rung.category !== 'hevc')
        : ENCODER_LADDER;
}

// Which acceleration modes this codec was actually accepted with.
export function supportsAcceleration(info: CodecInfo, accel: HardwareAcceleration): boolean {
    return accel === 'prefer-hardware' ? info.hardwareSupported : info.softwareSupported;
}

export interface EncoderCandidate {
    info: CodecInfo;
    accel: HardwareAcceleration;
}

// The encoder shortlist, best-first: every ladder rung this device can run,
// after the filters. Lives here rather than in the recorder so the ladder has
// one implementation and a test can exercise the real one.
export function selectEncoderCandidates(
    supportedCodecs: CodecInfo[],
    allowedCategories: ReadonlySet<CodecInfo['category']> | null,
    preferred: CodecCategory | null,
): EncoderCandidate[] {
    const byCategory = new Map<CodecInfo['category'], CodecInfo>();
    for (const codecInfo of supportedCodecs) {
        if (!codecInfo.supported) continue;
        // Measured pipeline latency, not a browser check: Firefox's H.264
        // encoder holds ~18 frames before its first chunk, which is half a
        // second of added latency and deadlocks the encode pipeline. A future
        // build that fixes it re-qualifies with no code change.
        if (codecInfo.realtime === false) continue;
        if (allowedCategories && !allowedCategories.has(codecInfo.category)) continue;
        if (isEncoderCodecExcluded(codecInfo.category)) continue;
        byCategory.set(codecInfo.category, codecInfo);
    }

    const candidates: EncoderCandidate[] = [];
    for (const rung of getEncoderLadder()) {
        const info = byCategory.get(rung.category);
        if (info && supportsAcceleration(info, rung.accel))
            candidates.push({ info, accel: rung.accel });
    }
    if (!preferred)
        return candidates;

    // A debug preference outranks the ladder so an operator can reproduce a
    // report on a specific encoder; it cannot conjure one that failed probing,
    // since those never reach this list.
    return [
        ...candidates.filter(c => c.info.category === preferred),
        ...candidates.filter(c => c.info.category !== preferred),
    ];
}

const DECODER_PROBES: { category: string; codecs: string[] }[] = [
    { category: 'h264', codecs: ['avc1.42E01E', 'avc1.42E01F', 'avc1.4D401F'] },
    { category: 'hevc', codecs: [
        'hev1.1.6.L120.B0',
        'hev1.1.6.L93.B0',
        'hvc1.1.6.L120.B0',    // iOS Safari
        'hvc1.1.6.L93.90',     // iOS Safari variant
    ] },
    { category: 'vp9', codecs: ['vp09.00.31.08', 'vp09.00.41.08'] },
    { category: 'av1', codecs: ['av01.0.05M.08', 'av01.0.08M.08'] },
];

async function detectSupportedDecoderCodecsUncached(): Promise<string[]> {
    // At `full` the classes probed below are the polyfill's, and this is what
    // installs them - probing first would advertise the browser's decoders on an
    // engine whose decoders we are about to replace.
    await WebCodecsCompat.whenReadyFor('video-decode');
    const codecs: string[] = [];

    const forced = getForceDecodeCodec();
    if (forced) {
        // A pin, not a capability report: the marker tells the server this list
        // is deliberate, so it must not be intersected with everyone else's or
        // padded with the floor. Without it the floor came back automatically
        // and outranked H.264 on efficiency, which made forcing H.264
        // unsatisfiable no matter what anyone asked for.
        const result = [FORCED_CODEC_MARKER, forced];
        infoLog?.log(`Debug: forceDecodeCodec=${forced} → advertising [${result.join(', ')}]`);
        return result;
    }

    for (const { category, codecs: probes } of DECODER_PROBES) {
        if (excludedDecoderCodecs.has(category)) {
            warnLog?.log(`Decoder ${category}: excluded at runtime`);
            continue;
        }

        let supported = false;
        for (const codec of probes) {
            try {
                // 320x240 deliberately: this asks whether a decoder exists at
                // all, not whether it handles our ladder's top tier.
                if (await isDecoderCodecSupported(codec, 320, 240)) {
                    infoLog?.log(`Decoder ${category} (${codec}): supported=true`);
                    supported = true;
                    break;
                }
                debugLog?.log(`Decoder ${category} (${codec}): supported=false`);
            } catch (e) {
                warnLog?.log(`Decoder ${category} (${codec}): error=${e}`);
            }
        }
        if (supported) codecs.push(category);
    }

    // The floor must be advertised even if probing said otherwise — a client
    // that cannot decode it is a client that needs a decoder, not a reason to
    // renegotiate the whole call. Log loudly; this should not happen.
    if (!codecs.includes(FLOOR_CATEGORY)) {
        warnLog?.log(
            `Decoder ${FLOOR_CATEGORY}: probed unsupported — advertising it anyway `
            + `(it is the negotiation floor); playback of ${FLOOR_CATEGORY} streams may fail`);
        codecs.push(FLOOR_CATEGORY);
    }

    infoLog?.log(`detectSupportedDecoderCodecsUncached: [${codecs.join(', ')}]${excludedDecoderCodecs.size > 0 ? ` (excluded: [${[...excludedDecoderCodecs].join(', ')}])` : ''}`);
    return codecs;
}

/**
 * What VP9 detection reports once libav.js owns that encoder: software-only, and
 * real-time without a probe, which is the property the swap exists to provide.
 */
function getLibavVp9CodecInfo(width: number, height: number): CodecInfo {
    const codec = getCodecForCategory('vp9', width, height);

    return {
        name: getCodecProfileName(codec) ?? codec,
        codec,
        category: 'vp9',
        supported: true,
        hardwareSupported: false,
        softwareSupported: true,
        hardwareAccelerated: false,
        realtime: true,
    };
}
