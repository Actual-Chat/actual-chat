import { getLogs } from 'logging';
import { kbpsToBitsPerSecond } from 'app-constants';
import { DeviceInfo } from 'device-info';

const { debugLog, infoLog, warnLog, errorLog } = getLogs('VideoPipeline');

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

// Probe ONE representative codec per category at the target resolution, in
// priority order. The actual encoder profile string is then derived from
// getCodecForCategory(category, w, h). Reduces startup probes ~7× vs per-profile.
const REPRESENTATIVE_CODECS: { category: CodecInfo['category']; name: string; codec: string }[] = [
    // TEMPORARILY DISABLED — AV1 mobile issues, VP9 selection disabled.
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
        infoLog?.log('Debug: forceH264Only=true → encoder detection limited to H.264');
    const results: CodecInfo[] = [];
    for (const { category, name, codec } of probeList) {
        const { supported, hardwareAccelerated, scalabilityModes } = await isCodecSupported(codec, category, width, height);
        // WebCodecs level ladders are backward-compatible: a working low-level
        // profile implies higher ones at the same dims work too.
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
    infoLog?.log(`detectSupportedCodecsUncached: ${supported.map(c =>
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

        if (category === 'h264') {
            baseConfig.avc = { format: 'avc' };
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
                } catch { /* unsupported */ }
            }
        }

        debugLog?.log(`Encoder ${codec}: ${supported ? `supported, hw=${hardwareAccelerated}` : 'not supported'}`);
        return { supported, hardwareAccelerated, scalabilityModes };
    } catch (error) {
        errorLog?.log(`Error checking codec support for ${codec}:`, error);
        return { supported: false, hardwareAccelerated: false, scalabilityModes: [] };
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

// Single-encoder probe of the top layer. Budget defaults to 33ms (30fps frame
// interval) — a generous ceiling. Borderline codecs pass here and rely on
// runtime backpressure (step-down → pickFallbackCodec → switchCodec) to recover.
// frameCount=8: 3 warmup (cold-start, first-keyframe init) + 5 steady-state.
export function probeEncoder(
    codec: string,
    layers: readonly ProbeLayer[],
    frameCount = 8,
    budgetMs = 33,
): Promise<EncoderProbeResult> {
    if (layers.length === 0)
        return Promise.resolve({ supported: false, medianEncodeMs: 0, failedStage: 'configure' });
    const top = layers[layers.length - 1];
    const key = `${codec}@${top.width}x${top.height}×${layers.length}`;
    let cached = encoderProbeCache.get(key);
    if (!cached) {
        cached = probeEncoderUncached(codec, layers, frameCount, budgetMs);
        encoderProbeCache.set(key, cached);
    }
    return cached;
}

async function probeEncoderUncached(
    codec: string,
    layers: readonly ProbeLayer[],
    frameCount: number,
    budgetMs: number,
): Promise<EncoderProbeResult> {
    // Probe only the top-tier encoder. Spinning up N concurrent encoders here
    // creates cold-start contention not present at runtime (encoders warm,
    // submission paced), producing false-fails. Top tier dominates simulcast cost.
    if (layers.length === 0)
        return { supported: false, medianEncodeMs: 0, failedStage: 'configure' };
    const category = getCodecCategory(codec);
    const top = layers[layers.length - 1];
    let pendingResolver: (() => void) | null = null;
    let encoder: VideoEncoder | null = null;
    let encoderError: unknown = null;

    const resolvePending = (): void => {
        const resolve = pendingResolver;
        if (resolve) {
            pendingResolver = null;
            resolve();
        }
    };

    try {
        const config: VideoEncoderConfig = {
            codec,
            width: top.width,
            height: top.height,
            bitrate: kbpsToBitsPerSecond(top.bitrateKbps),
            framerate: 30,
            latencyMode: 'realtime',
            hardwareAcceleration: 'prefer-hardware',
        };
        if (category === 'h264')
            config.avc = { format: 'avc' };
        const support = await VideoEncoder.isConfigSupported(config);
        if (!support.supported) {
            debugLog?.log(`probeEncoder: configure fail for ${codec} at ${top.width}x${top.height}`);
            return { supported: false, medianEncodeMs: 0, failedStage: 'configure' };
        }
        encoder = new VideoEncoder({
            output: () => resolvePending(),
            // Hardware encoders can fail asynchronously (NVENC contention,
            // VideoToolbox init quirks). Capture and unblock the pending
            // wait so the probe fails fast instead of hanging on `await wait`.
            error: e => {
                encoderError = e;
                debugLog?.log(`probeEncoder: encoder error for ${codec}`, e);
                resolvePending();
            },
        });
        encoder.configure(config);

        const srcW = top.width;
        const srcH = top.height;
        const canvas = new OffscreenCanvas(srcW, srcH);
        const ctx = canvas.getContext('2d');
        if (!ctx) {
            warnLog?.log('probeEncoder: 2D context unavailable on OffscreenCanvas');
            return { supported: false, medianEncodeMs: 0, failedStage: 'configure' };
        }

        const frameDurationUs = Math.round(1_000_000 / 30);
        const timings: number[] = [];

        for (let f = 0; f < frameCount; f++) {
            // Vary the color so the encoder can't trivially predict the frame.
            ctx.fillStyle = `rgb(${(f * 31) & 0xff}, ${(f * 61) & 0xff}, ${(f * 127) & 0xff})`;
            ctx.fillRect(0, 0, srcW, srcH);
            const srcFrame = new VideoFrame(canvas, { timestamp: f * frameDurationUs });

            const wait = new Promise<void>(resolve => {
                pendingResolver = resolve;
            });
            const t0 = performance.now();
            try {
                encoder.encode(srcFrame, { keyFrame: f === 0 });
            } catch (e) {
                srcFrame.close();
                errorLog?.log(`probeEncoder encode threw`, e);
                return { supported: false, medianEncodeMs: 0, failedStage: 'encode' };
            }
            srcFrame.close();
            await wait;
            if (encoderError)
                return { supported: false, medianEncodeMs: 0, failedStage: 'encode' };
            timings.push(performance.now() - t0);
        }

        // Discard cold-start samples: first frame is a keyframe and first 1-2
        // pay codec init costs that don't repeat at steady state. Without this,
        // HW-capable machines fail the probe due to warmup-skewed median.
        // 6+ frames: drop 3 warmup; 3-5: drop 2; <3: drop none.
        const warmupCount = frameCount >= 6 ? 3 : (frameCount >= 3 ? 2 : 0);
        const steadyTimings = timings.slice(warmupCount);
        steadyTimings.sort((a, b) => a - b);
        const median = steadyTimings[Math.floor(steadyTimings.length / 2)];
        const supported = median <= budgetMs;
        debugLog?.log(`probeEncoder: ${codec} (top-layer solo) @ ${top.width}x${top.height} — median=${median.toFixed(1)}ms (warmup=${warmupCount}, steady=${steadyTimings.length}), budget=${budgetMs}ms, ${supported ? 'PASS' : 'FAIL'}`);
        return { supported, medianEncodeMs: median, failedStage: supported ? null : 'encode' };
    } catch (error) {
        errorLog?.log('probeEncoder: unexpected error', error);
        return { supported: false, medianEncodeMs: 0, failedStage: 'configure' };
    } finally {
        if (encoder) {
            try { encoder.close(); } catch { /* already closed */ }
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
    // Keep the codec string CONSTANT across resolutions — Chrome's WebCodecs
    // re-inits the underlying NVENC session on any codec-string change (even
    // just a level byte), so changing strings on resolution change defeats
    // encoder-pool reuse and reproduces 'Encoder initialization error' storms.
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

export function getDefaultCodec(supportedCodecs: CodecInfo[], width: number, height: number): string {
    // Firefox: H.264 Main 3.1 is the only reliable encoder profile.
    if (DeviceInfo.isFirefox) {
        return 'avc1.4D401F';
    }

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

    return getCodecForCategory('h264', width, height);
}

export async function getAV1CodecSupport(): Promise<CodecInfo[]> {
    const results: CodecInfo[] = [];

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

// Codecs that report support but fail at runtime (wrong dims, slow decode).
const excludedDecoderCodecs = new Set<string>();

export function excludeDecoderCodec(codec: string): void {
    if (codec === 'h264') return; // never exclude h264 — universal fallback
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

    // AV1 — TEMPORARILY DISABLED (mobile issues).
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

    // VP9 — TEMPORARILY DISABLED.
    infoLog?.log('Decoder VP9: temporarily disabled');

    infoLog?.log(`detectSupportedDecoderCodecsUncached: [${codecs.join(', ')}]${excludedDecoderCodecs.size > 0 ? ` (excluded: [${[...excludedDecoderCodecs].join(', ')}])` : ''}`);
    return codecs;
}
