import { getLogs } from 'logging';
import { kbpsToBitsPerSecond } from 'app-constants';
import { DeviceInfo } from 'device-info';
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

export interface CodecInfo {
    name: string;
    codec: string;
    category: 'h264' | 'hevc' | 'av1' | 'vp9';
    supported: boolean;
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
// exclusion path may drop. It is VP9 rather than H.264 on measurement: VP9
// encodes and decodes at real-time latency on Chromium, Firefox, desktop
// Safari and the iOS WebView, while Firefox's H.264 encoder is ~18 frames
// behind. See docs/plans/codec-negotiation.md.
export const FLOOR_CATEGORY = 'vp9';

// Codec support is per UA+OS for the page lifetime, so each (W,H) probes once.
// In-flight Promise stored so concurrent callers share work.
const encoderCodecCache = new Map<string, Promise<CodecInfo[]>>();

// Debug flag persisted in localStorage; toggled from VideoDiagnosticsSettingsModal.
// Pins negotiation to FLOOR_CATEGORY so a report can be reproduced on the one
// codec every client is required to speak. Its own key, not the old
// forceH264Only one: the flag now means a different codec, so an existing
// "force H.264" setting must not silently become "force VP9".
const FORCE_FLOOR_CODEC_ONLY_KEY = 'video.debug.forceFloorCodecOnly';

function readForceFloorCodecOnlyFromStorage(): boolean {
    try {
        return globalThis.localStorage.getItem(FORCE_FLOOR_CODEC_ONLY_KEY) === 'true';
    } catch {
        // localStorage throws in private mode / sandboxed contexts.
        return false;
    }
}

export function getForceFloorCodecOnly(): boolean {
    return readForceFloorCodecOnlyFromStorage();
}

export function setForceFloorCodecOnly(enabled: boolean): void {
    try {
        if (enabled) globalThis.localStorage.setItem(FORCE_FLOOR_CODEC_ONLY_KEY, 'true');
        else globalThis.localStorage.removeItem(FORCE_FLOOR_CODEC_ONLY_KEY);
    } catch (e) {
        warnLog?.log(`setForceFloorCodecOnly: localStorage write failed: ${String(e)}`);
    }
    // Invalidate detection caches so the next stream re-probes with the new flag.
    encoderCodecCache.clear();
    decoderCodecCache = null;
    infoLog?.log(`Debug: forceFloorCodecOnly set to ${enabled}; codec detection caches cleared`);
}


export function detectSupportedCodecs(width = 1920, height = 1080): Promise<CodecInfo[]> {
    const key = `${width}x${height}`;
    let cached = encoderCodecCache.get(key);
    if (!cached) {
        cached = detectSupportedCodecsUncached(width, height);
        encoderCodecCache.set(key, cached);
    }
    return cached;
}

// Single source of truth for encoder category enable/disable + priority.
// Probe ONE representative codec per category at the target resolution, in
// priority order. The actual encoder profile string is then derived from
// getCodecForCategory(category, w, h). Reduces startup probes ~7× vs per-profile.
//
// Priority order, best-first. Detection, the modal-time probe, the recorder
// probe, default-codec fallback and audience ordering all derive from this
// list (directly or via `supportedCodecs`), so a change here cascades.
//
// HEVC first where it exists (Apple hardware, best efficiency of the
// hardware-backed options), then VP9 — the floor, and the only entry every
// measured engine encodes and decodes at real-time latency. H.264 sits below
// VP9 because Firefox's H.264 encoder runs ~18 frames behind and no
// configuration fixes it. AV1 is last: absent on every Apple device measured
// and software-only elsewhere, so it is enabled but never preferred until its
// sustained cost is known.
const REPRESENTATIVE_CODECS: { category: CodecInfo['category']; name: string; codec: string }[] = [
    { category: 'hevc', name: 'HEVC Main L3.1',     codec: 'hev1.1.6.L93.B0' },
    { category: 'vp9',  name: 'VP9 Profile 0 L3.1', codec: 'vp09.00.31.08' },
    { category: 'h264', name: 'H.264 Main 3.1',     codec: 'avc1.4D401F' },
    { category: 'av1',  name: 'AV1 Main L3.0',      codec: 'av01.0.05M.08' },
];

// Active encoder categories in priority order (best-first). Derived from
// REPRESENTATIVE_CODECS so callers outside detection (e.g. JoinVideoCallModal's
// pre-flight probe) stay in sync with what's actually enabled.
export function getActiveEncoderCategoriesByPriority(): readonly CodecInfo['category'][] {
    return REPRESENTATIVE_CODECS.map(c => c.category);
}

async function detectSupportedCodecsUncached(width: number, height: number): Promise<CodecInfo[]> {
    const forceFloor = readForceFloorCodecOnlyFromStorage();
    let probeList = forceFloor
        ? REPRESENTATIVE_CODECS.filter(c => c.category === FLOOR_CATEGORY)
        : REPRESENTATIVE_CODECS;
    if (forceFloor)
        infoLog?.log(`Debug: forceFloorCodecOnly=true → encoder detection limited to ${FLOOR_CATEGORY}`);
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
    for (const { category } of probeList) {
        const ladder = getEncoderCodecLadder(category, width, height)
            .filter(c => !excludedEncoderCodecStrings.has(c));
        let chosen: { codec: string; hardwareAccelerated: boolean } | null = null;
        for (const codec of ladder) {
            const { supported, hardwareAccelerated } = await isCodecSupported(codec, category, width, height);
            if (supported) {
                chosen = { codec, hardwareAccelerated };
                break;
            }
        }
        const codec = chosen?.codec
            ?? (ladder.length > 0 ? ladder[0] : getCodecForCategory(category, width, height));
        let realtime: boolean | undefined;
        if (chosen !== null) {
            const frames = await probeEncoderLatencyFrames(
                codec, category, getDefaultHardwareAcceleration());
            realtime = frames <= MAX_REALTIME_LATENCY_FRAMES;
            if (!realtime) {
                warnLog?.log(
                    `Encoder ${codec}: first chunk only after ${frames} frames `
                    + `(> ${MAX_REALTIME_LATENCY_FRAMES}) — not usable for calls`);
            }
        }
        results.push({
            name: getCodecProfileName(codec) ?? codec,
            codec,
            category,
            supported: chosen !== null,
            hardwareAccelerated: chosen?.hardwareAccelerated ?? false,
            realtime,
        });
    }
    const supported = results.filter(c => c.supported);
    infoLog?.log(`detectSupportedCodecsUncached: ${supported.map(c =>
        `${c.codec}(${c.hardwareAccelerated ? 'hw' : 'sw'})`).join(', ') || 'none'}`);
    return results;
}

async function isCodecSupported(
    codec: string,
    category: 'h264' | 'hevc' | 'av1' | 'vp9',
    width: number,
    height: number
): Promise<{ supported: boolean; hardwareAccelerated: boolean }> {
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

        // Firefox often returns false for 'prefer-hardware' but works with 'no-preference'.
        let supported = false;
        let hardwareAccelerated = false;

        for (const accel of ['prefer-hardware', 'no-preference'] as const) {
            const config = { ...baseConfig, hardwareAcceleration: accel };
            const support = await VideoEncoder.isConfigSupported(config);
            if (support.supported) {
                supported = true;
                hardwareAccelerated = accel === 'prefer-hardware'
                    && (support.config?.hardwareAcceleration === 'prefer-hardware');
                break;
            }
        }

        debugLog?.log(`Encoder ${codec}: ${supported ? `supported, hw=${hardwareAccelerated}` : 'not supported'}`);
        return { supported, hardwareAccelerated };
    } catch (error) {
        errorLog?.log(`Error checking codec support for ${codec}:`, error);
        return { supported: false, hardwareAccelerated: false };
    }
}

// An encoder that only emits its first chunk after this many submitted frames
// is unusable for a call: at 30fps each frame is ~33ms of added latency, and
// the pipeline would have to hold that many bundles in flight to avoid
// deadlocking. Firefox's H.264 sits at 18; every other engine/codec measured
// emits within 2.
const MAX_REALTIME_LATENCY_FRAMES = 4;
const MAX_LATENCY_PROBE_FRAMES = 24;
const latencyProbeCache = new Map<string, Promise<number>>();

// Frames submitted before the encoder produced its first chunk, or
// MAX_LATENCY_PROBE_FRAMES if it produced none. Deliberately a *count*, not a
// duration: an earlier synthetic throughput probe was removed from this file
// because GPU contention made it false-fail on healthy machines. A frame count
// doesn't move when the machine is busy — it's a property of the encoder's
// pipeline, so it stays honest under load.
export function probeEncoderLatencyFrames(
    codec: string,
    category: CodecInfo['category'],
    hardwareAcceleration: HardwareAcceleration,
): Promise<number> {
    const key = `${codec}@${hardwareAcceleration}`;
    let cached = latencyProbeCache.get(key);
    if (!cached) {
        cached = probeEncoderLatencyFramesUncached(codec, category, hardwareAcceleration);
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
        const state = { submitted: 0, firstOutputAt: -1, failed: false };
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
                if (state.firstOutputAt < 0) state.firstOutputAt = state.submitted;
                closeEncodedChunk(chunk);
            },
            error: () => { state.failed = true; },
        });
        encoder.configure(config);

        const canvas = new OffscreenCanvas(width, height);
        const ctx = canvas.getContext('2d');
        if (!ctx)
            return 0;

        for (let i = 0; i < MAX_LATENCY_PROBE_FRAMES && state.firstOutputAt < 0 && !state.failed; i++) {
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
            // Yield so the output callback can land between submissions —
            // without this the whole loop runs before any callback fires and
            // every encoder looks equally slow.
            await new Promise(resolve => setTimeout(resolve, 0));
        }
        if (state.failed)
            return MAX_LATENCY_PROBE_FRAMES;

        return state.firstOutputAt < 0 ? MAX_LATENCY_PROBE_FRAMES : state.firstOutputAt;
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
    const isMobile = DeviceInfo.isMobile; // includes iOS
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
    // H.264: Firefox/mobile = Main, desktop = High (better compression).
    if (isMobile || DeviceInfo.isFirefox)
        return ultraHi ? 'avc1.4D4034' : 'avc1.4D4029';

    return ultraHi ? 'avc1.640034' : 'avc1.640028';
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

function getMainH264Codec(width: number, height: number): string {
    const pixels = width * height;
    if (pixels > 2_073_600)
        return 'avc1.4D4034'; // L5.2 (>1080p)
    if (pixels > 921_600)
        return 'avc1.4D4029'; // L4.1 (1080p)

    return 'avc1.4D401F'; // L3.1 (≤720p)
}

// Profiles to probe for one category, best-first. Detection reports the first
// entry that `isConfigSupported` accepts, so we never advertise a profile we
// didn't test: Main → High is a profile change, not a level step, and a device
// that encodes Main 3.1 may well reject High 4.0. Only H.264 gets alternatives
// — it's the one category with no other category to fall back to.
function getEncoderCodecLadder(category: CodecInfo['category'], width: number, height: number): string[] {
    const preferred = getCodecForCategory(category, width, height);
    if (category !== 'h264')
        return [preferred];

    const ladder = [preferred, getMainH264Codec(width, height), getSoftwareH264Codec(width, height)];
    return ladder.filter((codec, i) => ladder.indexOf(codec) === i);
}

function getCodecProfileName(codec: string): string | null {
    for (const profiles of Object.values(CODEC_PROFILES)) {
        const match = profiles.find(p => p.codec === codec);
        if (match)
            return match.name;
    }

    return null;
}

export function getDefaultCodec(supportedCodecs: CodecInfo[], width: number, height: number): string {
    const isMobile = DeviceInfo.isMobile; // includes iOS

    // Priority: AV1 HW > HEVC HW > VP9 HW > H.264 HW (profile by platform) > H.264 SW

    const av1HW = supportedCodecs.find(
        c => c.category === 'av1' && c.supported && c.hardwareAccelerated
    );
    if (av1HW) return av1HW.codec;

    const hevcHW = supportedCodecs.find(
        c => c.category === 'hevc' && c.supported && c.hardwareAccelerated
    );
    if (hevcHW) return hevcHW.codec;

    const vp9HW = supportedCodecs.find(
        c => c.category === 'vp9' && c.supported && c.hardwareAccelerated
    );
    if (vp9HW) return vp9HW.codec;

    // Mobile prefers Main>Baseline>High (power); desktop prefers High>Main (compression).
    const h264ProfileOrder = isMobile
        ? ['4D40', '42E0', '6400']
        : ['6400', '4D40'];

    for (const profile of h264ProfileOrder) {
        const match = supportedCodecs.find(
            c => c.category === 'h264' && c.supported && c.hardwareAccelerated && c.codec.includes(profile)
        );
        if (match) return match.codec;
    }
    const anyH264HW = supportedCodecs.find(
        c => c.category === 'h264' && c.supported && c.hardwareAccelerated
    );
    if (anyH264HW) return anyH264HW.codec;

    // VP9 SW beats H.264 SW on compression.
    const vp9SW = supportedCodecs.find(
        c => c.category === 'vp9' && c.supported
    );
    if (vp9SW) return vp9SW.codec;

    for (const profile of h264ProfileOrder) {
        const match = supportedCodecs.find(
            c => c.category === 'h264' && c.supported && c.codec.includes(profile)
        );
        if (match) return match.codec;
    }
    const anyH264 = supportedCodecs.find(
        c => c.category === 'h264' && c.supported
    );
    if (anyH264) return anyH264.codec;

    // Last resort is the floor, not H.264: a device with no usable H.264 (any
    // Firefox) must still land on something it can actually encode.
    const floor = supportedCodecs.find(c => c.category === FLOOR_CATEGORY && c.supported);
    if (floor) return floor.codec;

    const ladder = getEncoderCodecLadder('h264', width, height)
        .filter(c => !excludedEncoderCodecStrings.has(c));

    return ladder.length > 0 ? ladder[0] : getCodecForCategory(FLOOR_CATEGORY, width, height);
}

export async function getAV1CodecSupport(): Promise<CodecInfo[]> {
    const results: CodecInfo[] = [];

    for (const profile of CODEC_PROFILES.av1) {
        const { supported, hardwareAccelerated } = await isCodecSupported(profile.codec, 'av1', 1920, 1080);
        results.push({
            name: profile.name,
            codec: profile.codec,
            category: 'av1',
            supported,
            hardwareAccelerated,
        });
    }

    return results;
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

// Excludes one exact profile rather than a whole category. H.264 has no
// category to fall back to, so `excludeEncoderCodec` refuses to drop it and a
// failed avc1.640028 would otherwise be re-picked forever; dropping just that
// string still leaves Main and Constrained Baseline to try.
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

// Representative decode strings per category, tried in order until one probes
// supported. H.264 is probed at the FLOOR of the profile ladder, not the
// ceiling: the question is "is there an H.264 decoder at all", and Constrained
// Baseline at a small size is the narrowest thing that answers it.
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
    const codecs: string[] = [];

    if (readForceFloorCodecOnlyFromStorage()) {
        infoLog?.log(`Debug: forceFloorCodecOnly=true → decoder detection limited to ${FLOOR_CATEGORY}`);
        return [FLOOR_CATEGORY];
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
