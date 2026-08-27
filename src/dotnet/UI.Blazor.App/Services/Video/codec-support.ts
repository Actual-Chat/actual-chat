import { getLogs } from 'logging';
import { getVideoCodecEfficiency, kbpsToBitsPerSecond } from 'app-constants';
import { DeviceInfo } from 'device-info';
import { isDecoderCodecProven, isEncoderCodecProven } from './codec-proof';
import { getEncoderFailInjection, matchesEncoderFailInjection } from './encoder-fail-injection';

// Yield a macrotask between close() and `new VideoEncoder()` so the platform
// releases the HW codec slot — otherwise Chrome NVENC/VA-API throws
// OperationError 'Encoder creation error'.
export async function awaitHwReleased(): Promise<void> {
    await Promise.resolve();
    await new Promise<void>(resolve => setTimeout(resolve, 0));
}

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

// Codec support is per UA+OS for the page lifetime, so each (W,H) probes once.
// In-flight Promise stored so concurrent callers share work.
const encoderCodecCache = new Map<string, Promise<CodecInfo[]>>();

// Debug flag persisted in localStorage; toggled from VideoDiagnosticsSettingsModal.
const FORCE_H264_ONLY_KEY = 'video.debug.forceH264Only';

function readForceH264OnlyFromStorage(): boolean {
    try {
        return globalThis.localStorage.getItem(FORCE_H264_ONLY_KEY) === 'true';
    } catch {
        // localStorage throws in private mode / sandboxed contexts.
        return false;
    }
}

export function getForceH264Only(): boolean {
    return readForceH264OnlyFromStorage();
}

export function setForceH264Only(enabled: boolean): void {
    try {
        if (enabled) globalThis.localStorage.setItem(FORCE_H264_ONLY_KEY, 'true');
        else globalThis.localStorage.removeItem(FORCE_H264_ONLY_KEY);
    } catch (e) {
        warnLog?.log(`setForceH264Only: localStorage write failed: ${String(e)}`);
    }
    // Invalidate detection caches so the next stream re-probes with the new flag.
    encoderCodecCache.clear();
    decoderCodecCache = null;
    infoLog?.log(`Debug: forceH264Only set to ${enabled}; codec detection caches cleared`);
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
// AV1 selection disabled (mobile issues) — see commit 3ae12d7f8; re-enable by
// uncommenting. VP9 is the fallback for browsers whose H.264 encoder is broken
// or absent (Firefox, bugzil.la/1918769) — selection eligibility is gated in
// listEncoderCandidatesByEfficiency (HW-ranked, SW only when nothing else is
// left). Detection, modal-time probe, recorder probe, default-codec fallback,
// and audience ordering all derive from this list (directly, or via
// `supportedCodecs`), so a single change here cascades everywhere.
const REPRESENTATIVE_CODECS: { category: CodecInfo['category']; name: string; codec: string }[] = [
    // { category: 'av1',  name: 'AV1 Main L3.0',      codec: 'av01.0.05M.08' },
    { category: 'hevc', name: 'HEVC Main L3.1',     codec: 'hev1.1.6.L93.B0' },
    { category: 'vp9',  name: 'VP9 Profile 0 L3.1', codec: 'vp09.00.31.08' },
    { category: 'h264', name: 'H.264 Main 3.1',     codec: 'avc1.4D401F' },
];

// Active encoder categories in priority order (best-first). Derived from
// REPRESENTATIVE_CODECS so callers outside detection (e.g. JoinVideoCallModal's
// pre-flight probe) stay in sync with what's actually enabled.
export function getActiveEncoderCategoriesByPriority(): readonly CodecInfo['category'][] {
    return REPRESENTATIVE_CODECS.map(c => c.category);
}

async function detectSupportedCodecsUncached(width: number, height: number): Promise<CodecInfo[]> {
    const forceH264 = readForceH264OnlyFromStorage();
    let probeList = forceH264
        ? REPRESENTATIVE_CODECS.filter(c => c.category === 'h264')
        : REPRESENTATIVE_CODECS;
    if (forceH264)
        infoLog?.log('Debug: forceH264Only=true → encoder detection limited to H.264');
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
    for (const { category, name, codec } of probeList) {
        const { supported, hardwareAccelerated } = await isCodecSupported(codec, category, width, height);
        // WebCodecs level ladders are backward-compatible: a working low-level
        // profile implies higher ones at the same dims work too.
        const chosenCodec = supported ? getCodecForCategory(category, width, height) : codec;
        results.push({
            name,
            codec: chosenCodec,
            category,
            supported,
            hardwareAccelerated,
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
    hardwareAcceleration: HardwareAcceleration = 'prefer-hardware',
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
    // Support is decided by isConfigSupported plus a real configure() check —
    // no frames are encoded. The old 8-synthetic-frame throughput probe
    // false-failed healthy systems under GPU contention (modal preview +
    // bg-blur + capture; worst on iOS's single HW slot and Windows MFT), so
    // throughput validation stays at the running pipeline's boundary. But
    // isConfigSupported alone is not trustworthy either: Firefox reports
    // H.264 as supported while the real configure() fails (bugzil.la/1918769),
    // so the codec must survive an actual configure()+flush() to pass.
    if (layers.length === 0)
        return { supported: false, medianEncodeMs: 0, failedStage: 'configure' };
    void frameCount;
    void budgetMs;
    const category = getCodecCategory(codec);
    const top = layers[layers.length - 1];
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
    try {
        const support = await VideoEncoder.isConfigSupported(config);
        if (!support.supported) {
            debugLog?.log(`probeEncoder: isConfigSupported=false for ${codec} at ${top.width}x${top.height}`);
            return { supported: false, medianEncodeMs: 0, failedStage: 'configure' };
        }
    } catch (error) {
        errorLog?.log('probeEncoder: unexpected error', error);
        return { supported: false, medianEncodeMs: 0, failedStage: 'configure' };
    }
    if (matchesEncoderFailInjection(getEncoderFailInjection(), category, 'probe')) {
        warnLog?.log(`probeEncoder: debug fail injection active for ${category} — reporting unsupported`);
        return { supported: false, medianEncodeMs: 0, failedStage: 'configure' };
    }

    const isConfigureOk = await verifyEncoderConfigure(config);
    debugLog?.log(
        `probeEncoder: ${codec} at ${top.width}x${top.height} (hwAccel=${hardwareAcceleration}): `
        + `configure ${isConfigureOk ? 'PASS' : 'FAIL'}`);
    return isConfigureOk
        ? { supported: true, medianEncodeMs: 0, failedStage: null }
        : { supported: false, medianEncodeMs: 0, failedStage: 'configure' };
}

const CONFIGURE_PROBE_TIMEOUT_MS = 3000;

// Creates a real encoder, configure()s it, awaits flush(), closes it. A sync
// throw, the WebCodecs error callback, or a flush rejection all mean the codec
// cannot actually initialize. A timeout counts as PASS: a slow-but-live encoder
// must not be false-failed here — only a lying isConfigSupported.
async function verifyEncoderConfigure(config: VideoEncoderConfig): Promise<boolean> {
    let encoder: VideoEncoder | null = null;
    try {
        let rejectOnError: (e: unknown) => void = () => undefined;
        const whenErrored = new Promise<never>((_, reject) => rejectOnError = reject);
        encoder = new VideoEncoder({
            output: () => undefined,
            error: e => rejectOnError(e),
        });
        encoder.configure(config);
        const whenTimedOut = new Promise<'timeout'>(
            resolve => setTimeout(() => resolve('timeout'), CONFIGURE_PROBE_TIMEOUT_MS));
        const result = await Promise.race([
            encoder.flush().then(() => 'ok' as const),
            whenErrored,
            whenTimedOut,
        ]);
        if (result === 'timeout')
            warnLog?.log(`verifyEncoderConfigure: ${config.codec} flush timed out — treating as supported`);

        return true;
    } catch (error) {
        warnLog?.log(`verifyEncoderConfigure: ${config.codec} failed:`, error);
        return false;
    } finally {
        try {
            if (encoder && encoder.state !== 'closed')
                encoder.close();
        } catch { /* already closed */ }
        await awaitHwReleased();
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
    if (isMobile || DeviceInfo.isFirefox) {
        if (ultraHi) return 'avc1.4D4034';
        return 'avc1.4D4029';
    }
    if (ultraHi) return 'avc1.640034';
    return 'avc1.640028';
}

// Chrome's software H.264 encoder (OpenH264) implements only Constrained
// Baseline — not Main/High. A High/Main string makes
// isConfigSupported({hardwareAcceleration:'prefer-software'}) return false, so
// the SW fallback must request Constrained Baseline (42E0xx). Universally
// decodable, which keeps the HW→SW switch transparent to viewers.
export function getSoftwareH264Codec(width: number, height: number): string {
    const pixels = width * height;
    if (pixels > 2_073_600) return 'avc1.42E034'; // L5.2 (>1080p)
    if (pixels > 921_600) return 'avc1.42E028';    // L4.0 (1080p)
    return 'avc1.42E01F';                          // L3.1 (≤720p)
}

// Best candidate per category (HW preferred within one), ordered by codec
// efficiency with HW as the tie-break. VP9 is gated: SW VP9 is eligible only as
// the last resort — no H.264/HEVC candidate left (e.g. Firefox with its broken
// H.264 encoder excluded at runtime, bugzil.la/1918769) — and mobile requires
// HW VP9 unconditionally (VP9-SW on Android silently drops all frames).
export function listEncoderCandidatesByEfficiency(
    supportedCodecs: CodecInfo[],
    allowedCategories: ReadonlySet<CodecInfo['category']> | null,
): CodecInfo[] {
    const bestByCategory = new Map<CodecInfo['category'], CodecInfo>();
    for (const codecInfo of supportedCodecs) {
        if (!codecInfo.supported)
            continue;
        if (allowedCategories && !allowedCategories.has(codecInfo.category))
            continue;
        if (isEncoderCodecExcluded(codecInfo.category))
            continue;

        const current = bestByCategory.get(codecInfo.category);
        if (!current || (!current.hardwareAccelerated && codecInfo.hardwareAccelerated))
            bestByCategory.set(codecInfo.category, codecInfo);
    }
    const vp9 = bestByCategory.get('vp9');
    if (vp9 && !vp9.hardwareAccelerated) {
        const isLastResort = !DeviceInfo.isMobile
            && !bestByCategory.has('h264')
            && !bestByCategory.has('hevc');
        if (!isLastResort)
            bestByCategory.delete('vp9');
    }
    return [...bestByCategory.values()]
        .sort((a, b) =>
            getVideoCodecEfficiency(b.codec) - getVideoCodecEfficiency(a.codec)
            || Number(b.hardwareAccelerated) - Number(a.hardwareAccelerated));
}

// Null means no encoder category is left at all (every category runtime-excluded)
// — the caller must surface a fatal error, not fabricate a codec: the historic
// unconditional H.264 fallback resurrected the exact codec whose exclusion
// triggered the re-pick.
export function getDefaultCodec(supportedCodecs: CodecInfo[], width: number, height: number): string | null {
    const isMobile = DeviceInfo.isMobile; // includes iOS
    const candidates = supportedCodecs.filter(c => c.supported && !isEncoderCodecExcluded(c.category));

    // Priority: AV1 HW > HEVC HW > VP9 HW > H.264 HW (profile by platform) > VP9 SW > H.264 SW

    const av1HW = candidates.find(c => c.category === 'av1' && c.hardwareAccelerated);
    if (av1HW)
        return av1HW.codec;

    const hevcHW = candidates.find(c => c.category === 'hevc' && c.hardwareAccelerated);
    if (hevcHW)
        return hevcHW.codec;

    const vp9HW = candidates.find(c => c.category === 'vp9' && c.hardwareAccelerated);
    if (vp9HW)
        return vp9HW.codec;

    // Mobile prefers Main>Baseline>High (power); desktop prefers High>Main (compression).
    const h264ProfileOrder = isMobile
        ? ['4D40', '42E0', '6400']
        : ['6400', '4D40'];

    for (const profile of h264ProfileOrder) {
        const match = candidates.find(
            c => c.category === 'h264' && c.hardwareAccelerated && c.codec.includes(profile));
        if (match)
            return match.codec;
    }

    const anyH264HW = candidates.find(c => c.category === 'h264' && c.hardwareAccelerated);
    if (anyH264HW)
        return anyH264HW.codec;

    // VP9 SW beats H.264 SW on compression, but is broken on mobile
    // (VP9-SW on Android silently drops all frames).
    if (!isMobile) {
        const vp9SW = candidates.find(c => c.category === 'vp9');
        if (vp9SW)
            return vp9SW.codec;
    }

    for (const profile of h264ProfileOrder) {
        const match = candidates.find(c => c.category === 'h264' && c.codec.includes(profile));
        if (match)
            return match.codec;
    }

    const anyH264 = candidates.find(c => c.category === 'h264');
    if (anyH264)
        return anyH264.codec;

    return isEncoderCodecExcluded('h264') ? null : getCodecForCategory('h264', width, height);
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

// h264 is excludable too: on Firefox the H.264 encoder can be entirely absent
// while isConfigSupported still reports it (bugzil.la/1918769) — a "universal
// fallback" guard here just loops the re-pick forever. The proven-guard keeps
// a codec that already shipped bundles this session from being excluded by a
// transient failure.
export function excludeEncoderCodec(category: string): void {
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
    if (codec === 'h264') return; // never exclude h264 — universal fallback
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

async function detectSupportedDecoderCodecsUncached(): Promise<string[]> {
    const codecs: string[] = ['h264']; // always assumed supported

    if (readForceH264OnlyFromStorage()) {
        infoLog?.log('Debug: forceH264Only=true → decoder detection limited to H.264');
        return codecs;
    }

    // AV1 — TEMPORARILY DISABLED.
    infoLog?.log('Decoder AV1: temporarily disabled');

    if (!excludedDecoderCodecs.has('hevc')) {
        let hevcSupported = false;
        for (const hevcCodec of [
            'hev1.1.6.L120.B0',
            'hev1.1.6.L93.B0',
            'hvc1.1.6.L120.B0',    // iOS Safari
            'hvc1.1.6.L93.90',     // iOS Safari variant
        ]) {
            try {
                if (await isDecoderCodecSupported(hevcCodec, 1280, 720)) {
                    infoLog?.log(`Decoder HEVC (${hevcCodec}): supported=true`);
                    hevcSupported = true;
                    break;
                }
                infoLog?.log(`Decoder HEVC (${hevcCodec}): supported=false`);
            } catch (e) {
                warnLog?.log(`Decoder HEVC (${hevcCodec}): error=${e}`);
            }
        }
        if (hevcSupported) codecs.push('hevc');
    } else {
        warnLog?.log(`Decoder HEVC: excluded at runtime`);
    }

    if (!excludedDecoderCodecs.has('vp9')) {
        try {
            const isVp9Supported = await isDecoderCodecSupported('vp09.00.31.08', 1280, 720);
            infoLog?.log(`Decoder VP9 (vp09.00.31.08): supported=${isVp9Supported}`);
            if (isVp9Supported) codecs.push('vp9');
        } catch (e) {
            warnLog?.log(`Decoder VP9 (vp09.00.31.08): error=${e}`);
        }
    } else {
        warnLog?.log('Decoder VP9: excluded at runtime');
    }

    infoLog?.log(`detectSupportedDecoderCodecsUncached: [${codecs.join(', ')}]${excludedDecoderCodecs.size > 0 ? ` (excluded: [${[...excludedDecoderCodecs].join(', ')}])` : ''}`);
    return codecs;
}
