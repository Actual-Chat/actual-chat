/**
 * Codec Support Detection
 * Detects which video codecs are supported by the browser
 */

import { getLogs } from 'logging';
import { DeviceInfo } from 'device-info';

const { debugLog, warnLog, errorLog } = getLogs('VideoPipeline');

export interface CodecInfo {
    name: string;
    codec: string;
    category: 'h264' | 'hevc' | 'av1' | 'vp9';
    supported: boolean;
    hardwareAccelerated: boolean;
    scalabilityModes: string[];
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

// Cache detection results by resolution key. Codec support is a property of the
// UA + OS and does not change within a page lifetime, so each (W, H) probe runs
// at most once. Stores the in-flight Promise so concurrent callers share work.
const encoderCodecCache = new Map<string, Promise<CodecInfo[]>>();

// Debug flag: when true, codec detection only considers H.264. Persists in
// localStorage so it survives page reloads. Toggled from VideoDiagnosticsSettingsModal.
// Used to reproduce / debug AVC-only behaviour when other codecs cause issues.
const FORCE_H264_ONLY_KEY = 'video.debug.forceH264Only';

function readForceH264OnlyFromStorage(): boolean {
    try {
        return globalThis.localStorage.getItem(FORCE_H264_ONLY_KEY) === 'true';
    } catch {
        // localStorage access can throw in private mode / sandboxed contexts.
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
    warnLog?.log(`Debug: forceH264Only set to ${enabled}; codec detection caches cleared`);
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

// Category-level detection: probe ONE representative codec per category at the
// target resolution, in priority order (AV1 → HEVC → VP9 → H.264). For the
// actual encoder profile string we consult `getCodecForCategory()` which picks
// a profile deterministically from category + resolution + platform — no extra
// probing needed. Reduces startup probe count ~7× vs per-profile detection.
const REPRESENTATIVE_CODECS: { category: CodecInfo['category']; name: string; codec: string }[] = [
    // TEMPORARILY DISABLED — AV1 mobile issues, VP9 selection disabled. Re-enable by uncommenting.
    // { category: 'av1',  name: 'AV1 Main L3.0',      codec: 'av01.0.05M.08' },
    { category: 'hevc', name: 'HEVC Main L3.1',     codec: 'hev1.1.6.L93.B0' },
    // { category: 'vp9',  name: 'VP9 Profile 0 L3.1', codec: 'vp09.00.31.08' },
    { category: 'h264', name: 'H.264 Main 3.1',     codec: 'avc1.4D401F' },
];

async function detectSupportedCodecsUncached(width: number, height: number): Promise<CodecInfo[]> {
    const forceH264 = readForceH264OnlyFromStorage();
    const probeList = forceH264
        ? REPRESENTATIVE_CODECS.filter(c => c.category === 'h264')
        : REPRESENTATIVE_CODECS;
    if (forceH264)
        warnLog?.log('Debug: forceH264Only=true → encoder detection limited to H.264');
    const results: CodecInfo[] = [];
    for (const { category, name, codec } of probeList) {
        const { supported, hardwareAccelerated, scalabilityModes } = await isCodecSupported(codec, category, width, height);
        // Report with the profile string the encoder will actually use for this
        // category at the target resolution. getCodecForCategory may pick a
        // higher-level profile than the one we probed — WebCodecs level ladders
        // are backward-compatible so a working low-level profile implies the
        // higher ones work for the same width/height too.
        const chosenCodec = supported ? getCodecForCategory(category, width, height) : codec;
        results.push({
            name,
            codec: chosenCodec,
            category,
            supported,
            hardwareAccelerated,
            scalabilityModes,
        });
    }
    const supported = results.filter(c => c.supported);
    warnLog?.log(`ENCODER_CODECS: ${supported.map(c =>
        `${c.codec}(${c.hardwareAccelerated ? 'hw' : 'sw'})`).join(', ') || 'none'}`);
    return results;
}

async function isCodecSupported(
    codec: string,
    category: 'h264' | 'hevc' | 'av1' | 'vp9',
    width: number,
    height: number
): Promise<{ supported: boolean; hardwareAccelerated: boolean; scalabilityModes: string[] }> {
    try {
        const baseConfig: VideoEncoderConfig = {
            codec,
            width,
            height,
            bitrate: 5_000_000,
            framerate: 30,
            latencyMode: 'realtime',
        };

        // Add codec-specific config
        if (category === 'h264') {
            baseConfig.avc = { format: 'avc' };
        }

        // Try hardware-accelerated first, then software fallback
        // Firefox often returns supported: false for 'prefer-hardware' but works with 'no-preference'
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

        // Detect supported scalability modes
        const scalabilityModes: string[] = [];
        if (supported) {
            const modesToTest = ['L1T1', 'L1T2', 'L1T3'];
            for (const mode of modesToTest) {
                try {
                    const testConfig: VideoEncoderConfig = {
                        ...baseConfig,
                        hardwareAcceleration: hardwareAccelerated ? 'prefer-hardware' : 'no-preference',
                        scalabilityMode: mode,
                    };
                    const modeSupport = await VideoEncoder.isConfigSupported(testConfig);
                    if (modeSupport.supported) {
                        scalabilityModes.push(mode);
                    }
                } catch {
                    // Mode not supported, continue
                }
            }
        }

        debugLog?.log(`Encoder ${codec}: ${supported ? `supported, hw=${hardwareAccelerated}` : 'not supported'}`);
        return { supported, hardwareAccelerated, scalabilityModes };
    } catch (error) {
        errorLog?.log(`Error checking codec support for ${codec}:`, error);
        return { supported: false, hardwareAccelerated: false, scalabilityModes: [] };
    }
}

export interface ConcurrentEncoderProbeResult {
    supported: boolean;
    medianEncodeMs: number;
    failedStage: 'configure' | 'encode' | null;
}

interface ProbeLayer { width: number; height: number; bitrate: number }

// Cache results per (codec, layer-count, top-layer-dims) — layer count and top
// dims dominate throughput, lower tiers are cheap-by-comparison and don't move
// the bottleneck meaningfully for gating decisions. Ladders are bottom-first
// (index 0 = base, last = top) so we key on `layers[layers.length - 1]`.
const concurrentEncoderProbeCache = new Map<string, Promise<ConcurrentEncoderProbeResult>>();

export function probeConcurrentEncoders(
    codec: string,
    layers: readonly ProbeLayer[],
    frameCount = 8,
    budgetMs = 12,
): Promise<ConcurrentEncoderProbeResult> {
    if (layers.length === 0)
        return Promise.resolve({ supported: false, medianEncodeMs: 0, failedStage: 'configure' });
    const top = layers[layers.length - 1];
    const key = `${codec}@${top.width}x${top.height}×${layers.length}`;
    let cached = concurrentEncoderProbeCache.get(key);
    if (!cached) {
        cached = probeConcurrentEncodersUncached(codec, layers, frameCount, budgetMs);
        concurrentEncoderProbeCache.set(key, cached);
    }
    return cached;
}

async function probeConcurrentEncodersUncached(
    codec: string,
    layers: readonly ProbeLayer[],
    frameCount: number,
    budgetMs: number,
): Promise<ConcurrentEncoderProbeResult> {
    const category = getCodecCategory(codec);
    const encoders: VideoEncoder[] = [];
    const outputCounters: number[] = layers.map(() => 0);
    const pendingResolvers: ((() => void) | null)[] = layers.map(() => null);

    try {
        for (let i = 0; i < layers.length; i++) {
            const layer = layers[i];
            const config: VideoEncoderConfig = {
                codec,
                width: layer.width,
                height: layer.height,
                bitrate: layer.bitrate,
                framerate: 30,
                latencyMode: 'realtime',
                hardwareAcceleration: 'prefer-hardware',
            };
            if (category === 'h264')
                config.avc = { format: 'avc' };
            const support = await VideoEncoder.isConfigSupported(config);
            if (!support.supported) {
                debugLog?.log(`probeConcurrentEncoders: configure fail for ${codec} at ${layer.width}x${layer.height}`);
                return { supported: false, medianEncodeMs: 0, failedStage: 'configure' };
            }
            const idx = i;
            const enc = new VideoEncoder({
                output: () => {
                    outputCounters[idx]++;
                    const resolve = pendingResolvers[idx];
                    if (resolve) {
                        pendingResolvers[idx] = null;
                        resolve();
                    }
                },
                error: e => errorLog?.log(`probeConcurrentEncoders[${idx}] error`, e),
            });
            enc.configure(config);
            encoders.push(enc);
        }

        // Shared synthetic source — one canvas at top-tier dims, smaller
        // encoders resize internally (Chrome's VideoEncoder accepts mismatched
        // input and scales to configured dims). Feeding the same top-res frame
        // to all is conservative — production downscales upstream, so per-tier
        // encode cost is no higher than what we measure here.
        // A 1920×1080 BGRA canvas is ~8 MB, allocated once.
        const top = layers[layers.length - 1];
        const srcW = top.width;
        const srcH = top.height;
        const canvas = new OffscreenCanvas(srcW, srcH);
        const ctx = canvas.getContext('2d');
        if (!ctx) {
            warnLog?.log('probeConcurrentEncoders: 2D context unavailable on OffscreenCanvas');
            return { supported: false, medianEncodeMs: 0, failedStage: 'configure' };
        }

        const frameDurationUs = Math.round(1_000_000 / 30);
        const timings: number[] = [];

        for (let f = 0; f < frameCount; f++) {
            // Vary the color so the encoder can't trivially predict the frame.
            ctx.fillStyle = `rgb(${(f * 31) & 0xff}, ${(f * 61) & 0xff}, ${(f * 127) & 0xff})`;
            ctx.fillRect(0, 0, srcW, srcH);
            const srcFrame = new VideoFrame(canvas, { timestamp: f * frameDurationUs });

            const waits = encoders.map((_, i) => new Promise<void>(resolve => {
                pendingResolvers[i] = resolve;
            }));
            const t0 = performance.now();
            try {
                for (let i = 0; i < encoders.length; i++) {
                    // Narrow the frame dims per encoder by creating a cropped view —
                    // cheap ref bump, no pixel copy. Encoders that need resizing
                    // handle it themselves.
                    const copy = srcFrame.clone();
                    try {
                        encoders[i].encode(copy, { keyFrame: f === 0 });
                    } catch (e) {
                        copy.close();
                        errorLog?.log(`probeConcurrentEncoders[${i}] encode threw`, e);
                        return { supported: false, medianEncodeMs: 0, failedStage: 'encode' };
                    }
                    copy.close();
                }
            } finally {
                srcFrame.close();
            }
            await Promise.all(waits);
            timings.push(performance.now() - t0);
        }

        // Discard cold-start samples — the first frame is a keyframe and the
        // first 1-2 frames also pay codec init / first I-frame allocation costs
        // that don't repeat at steady state. Without this the probe rejects
        // HW-capable machines that just need a few ms more for warmup (e.g.
        // 1080p single-encoder probes failing at ~20 ms median because the
        // warmup samples dragged the median above an 18 ms budget).
        const warmupCount = Math.min(2, Math.max(0, frameCount - 4));
        const steadyTimings = timings.slice(warmupCount);
        steadyTimings.sort((a, b) => a - b);
        const median = steadyTimings[Math.floor(steadyTimings.length / 2)];
        const supported = median <= budgetMs;
        debugLog?.log(`probeConcurrentEncoders: ${codec} × ${layers.length} @ ${top.width}x${top.height} — median=${median.toFixed(1)}ms (warmup=${warmupCount}, steady=${steadyTimings.length}), budget=${budgetMs}ms, ${supported ? 'PASS' : 'FAIL'}`);
        return { supported, medianEncodeMs: median, failedStage: supported ? null : 'encode' };
    } catch (error) {
        errorLog?.log('probeConcurrentEncoders: unexpected error', error);
        return { supported: false, medianEncodeMs: 0, failedStage: 'configure' };
    } finally {
        for (const enc of encoders) {
            try { enc.close(); } catch { /* already closed */ }
        }
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
    // Codec STRING is the encoder's max-capability hint (level), not the actual
    // bitstream level. Picking a higher level for low-res streams is harmless —
    // the encoder still outputs at the actual dims. Critically, keeping the
    // codec string CONSTANT across resolutions lets the worker-side encoder
    // pool reuse the NVENC session across stop/start: Chrome's WebCodecs
    // re-inits the underlying NVENC session on any codec-string change (even
    // just a level byte), so changing strings on resolution change defeats
    // pool reuse and reproduces the `OperationError: Encoder initialization
    // error` storm. Pick the highest "ladder-cap" level per category — L4.0
    // for 1080p — and use it for every session of that category.
    // For >1080p (rare path: 4K screencast) bump up; otherwise stay constant.
    const pixels = width * height;
    const ultraHi = pixels > 2_073_600; // 4K territory

    if (category === 'av1') {
        return ultraHi ? 'av01.0.12M.08' : 'av01.0.08M.08'; // Main L5.0 / Main L4.0
    }
    if (category === 'vp9') {
        return ultraHi ? 'vp09.00.50.08' : 'vp09.00.41.08'; // Profile 0 L5.0 / L4.1
    }
    if (category === 'hevc') {
        return ultraHi ? 'hev1.1.6.L150.B0' : 'hev1.1.6.L120.B0'; // Main L5.0 / Main L4.0
    }
    // H.264: Firefox and mobile use Main profile, desktop uses High (better compression)
    if (isMobile || DeviceInfo.isFirefox) {
        if (ultraHi) return 'avc1.4D4034'; // Main 5.2
        return 'avc1.4D4029';              // Main 4.1 (covers up to 1080p)
    }
    if (ultraHi) return 'avc1.640034'; // High 5.2
    return 'avc1.640028';              // High 4.0 (covers up to 1080p)
}

export function getDefaultCodec(supportedCodecs: CodecInfo[], width: number, height: number): string {
    // Firefox: force H.264 Main 3.1 — only reliable encoder profile
    if (DeviceInfo.isFirefox) {
        return 'avc1.4D401F';
    }

    const isMobile = DeviceInfo.isMobile; // includes iOS

    // Priority: AV1 HW > HEVC HW > VP9 HW > H.264 HW (profile by platform) > H.264 SW

    // 1. Try AV1 with hardware acceleration
    const av1HW = supportedCodecs.find(
        c => c.category === 'av1' && c.supported && c.hardwareAccelerated
    );
    if (av1HW) return av1HW.codec;

    // 2. Try HEVC with hardware acceleration
    const hevcHW = supportedCodecs.find(
        c => c.category === 'hevc' && c.supported && c.hardwareAccelerated
    );
    if (hevcHW) return hevcHW.codec;

    // 3. Try VP9 with hardware acceleration
    const vp9HW = supportedCodecs.find(
        c => c.category === 'vp9' && c.supported && c.hardwareAccelerated
    );
    if (vp9HW) return vp9HW.codec;

    // H.264 profile preference depends on platform
    // Mobile: Main > Baseline > High (power efficient)
    // Desktop: High > Main > any (better compression)
    const h264ProfileOrder = isMobile
        ? ['4D40', '42E0', '6400']  // Main, Baseline, High
        : ['6400', '4D40'];         // High, Main

    // 4. Try H.264 HW in profile preference order
    for (const profile of h264ProfileOrder) {
        const match = supportedCodecs.find(
            c => c.category === 'h264' && c.supported && c.hardwareAccelerated && c.codec.includes(profile)
        );
        if (match) return match.codec;
    }
    // 4b. Any H.264 HW
    const anyH264HW = supportedCodecs.find(
        c => c.category === 'h264' && c.supported && c.hardwareAccelerated
    );
    if (anyH264HW) return anyH264HW.codec;

    // 5. Try VP9 SW (better compression than H.264 SW)
    const vp9SW = supportedCodecs.find(
        c => c.category === 'vp9' && c.supported
    );
    if (vp9SW) return vp9SW.codec;

    // 6. Try H.264 SW in profile preference order
    for (const profile of h264ProfileOrder) {
        const match = supportedCodecs.find(
            c => c.category === 'h264' && c.supported && c.codec.includes(profile)
        );
        if (match) return match.codec;
    }
    // 5b. Any H.264 SW
    const anyH264 = supportedCodecs.find(
        c => c.category === 'h264' && c.supported
    );
    if (anyH264) return anyH264.codec;

    // Fallback: resolution-aware codec string
    return getCodecForCategory('h264', width, height);
}

export async function getAV1CodecSupport(): Promise<CodecInfo[]> {
    const results: CodecInfo[] = [];

    // Check AV1 codecs
    for (const profile of CODEC_PROFILES.av1) {
        const { supported, hardwareAccelerated, scalabilityModes } = await isCodecSupported(profile.codec, 'av1', 1920, 1080);
        results.push({
            name: profile.name,
            codec: profile.codec,
            category: 'av1',
            supported,
            hardwareAccelerated,
            scalabilityModes,
        });
    }

    return results;
}

/** Check decoder support with prefer-hardware → no-preference fallback (matches video-player.ts init) */
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

// Runtime codec exclusion — codecs that technically report support but fail at runtime
// (for example, wrong decoded dimensions or sustained slow decode).
const excludedDecoderCodecs = new Set<string>();

/** Exclude a decoder codec category at runtime (persists for JS module lifetime). */
export function excludeDecoderCodec(codec: string): void {
    if (codec === 'h264') return; // never exclude h264 — universal fallback
    warnLog?.log(`Excluding decoder codec: ${codec}`);
    excludedDecoderCodecs.add(codec);
    // Invalidate the cache so the next call re-detects with the new exclusion set.
    decoderCodecCache = null;
}

/** Get the set of runtime-excluded decoder codecs. */
export function getExcludedDecoderCodecs(): string[] {
    return [...excludedDecoderCodecs];
}

// Cache decoder-codec detection the same way as encoder detection.
let decoderCodecCache: Promise<string[]> | null = null;

export function detectSupportedDecoderCodecs(): Promise<string[]> {
    decoderCodecCache ??= detectSupportedDecoderCodecsUncached();
    return decoderCodecCache;
}

async function detectSupportedDecoderCodecsUncached(): Promise<string[]> {
    const codecs: string[] = ['h264']; // H.264 always assumed supported

    if (readForceH264OnlyFromStorage()) {
        warnLog?.log('Debug: forceH264Only=true → decoder detection limited to H.264');
        return codecs;
    }

    // AV1 — TEMPORARILY DISABLED (mobile issues). Re-enable by restoring the probe block.
    warnLog?.log('Decoder AV1: temporarily disabled');

    // HEVC — try multiple codec strings
    if (!excludedDecoderCodecs.has('hevc')) {
        let hevcSupported = false;
        for (const hevcCodec of [
            'hev1.1.6.L120.B0',    // hev1 Main L4.0
            'hev1.1.6.L93.B0',     // hev1 Main L3.1
            'hvc1.1.6.L120.B0',    // hvc1 Main L4.0 (iOS Safari)
            'hvc1.1.6.L93.90',     // hvc1 Main L3.1 (iOS Safari variant)
        ]) {
            try {
                if (await isDecoderCodecSupported(hevcCodec, 1280, 720)) {
                    warnLog?.log(`Decoder HEVC (${hevcCodec}): supported=true`);
                    hevcSupported = true;
                    break;
                }
                warnLog?.log(`Decoder HEVC (${hevcCodec}): supported=false`);
            } catch (e) {
                warnLog?.log(`Decoder HEVC (${hevcCodec}): error=${e}`);
            }
        }
        if (hevcSupported) codecs.push('hevc');
    } else {
        warnLog?.log(`Decoder HEVC: excluded at runtime`);
    }

    // VP9 — TEMPORARILY DISABLED (selection disabled). Re-enable by restoring the probe block.
    warnLog?.log('Decoder VP9: temporarily disabled');

    warnLog?.log(`DECODER_CODECS: [${codecs.join(', ')}]${excludedDecoderCodecs.size > 0 ? ` (excluded: [${[...excludedDecoderCodecs].join(', ')}])` : ''}`);
    return codecs;
}
