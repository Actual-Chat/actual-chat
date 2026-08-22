// Diagnostic utilities for catching main-thread stalls, feedback loops, and
// long-running scripts. Three independent monitors that can be enabled
// individually via init() options or by calling installers directly.
//
// Typical usage (e.g. from BrowserInit.init):
//   MainThreadDiagnostics.init({
//       longAnimationFrame: { enabled: true, thresholdMs: 400 },
//       mutationStorm: { enabled: true, threshold: 100, windowMs: 1000 },
//       watchdog: { enabled: true, stallThresholdMs: 1000 },
//   });
//
// Persistent kill switch (per browser session, survives reloads):
//   logLevels.override('MainThreadDiagnostics', 1000) // 1000 = LogLevel.None
//   logLevels.override('MainThreadDiagnostics', 2)    // 2 = Info — re-enable
// init() detects None and skips installation, logging the fact via console.log.

const LOAF_DEDUPE_WINDOW_MS = 5000;
const MO_STORM_COOLDOWN_MS = 5000;

import { getLogs } from 'logging';

const { infoLog, warnLog, errorLog } = getLogs('MainThreadDiagnostics');

// Compact JSON serializer for diagnostic payloads. Inlines the payload into
// the log message text so it survives "Copy console", log uploaders, and
// DevTools quirks where structured args render as just "Object". Drops
// zero/empty/null fields and truncates long strings to keep lines manageable
// while staying machine-parseable via JSON.parse.
function compactJson(payload: unknown): string {
    return JSON.stringify(payload, (_k: string, v: unknown) => {
        if (v === 0 || v === '' || v == null) return undefined;
        if (typeof v === 'string' && v.length > 200) return v.slice(0, 200) + '…';
        return v;
    });
}

// performance.memory is non-standard (Chromium-only) and absent from lib.dom.
interface PerformanceMemory {
    usedJSHeapSize: number;
    totalJSHeapSize: number;
    jsHeapSizeLimit: number;
}
function getMemory(): PerformanceMemory | undefined {
    return (performance as Performance & { memory?: PerformanceMemory }).memory;
}

// Total WebAssembly.Memory bytes for the Blazor/Mono runtime instance.
// Includes both managed (Mono GC heap) and unmanaged native allocations —
// any growth indicates allocation pressure inside .NET. HEAPU8 is the
// view; its byteLength tracks the underlying Memory's current size and
// is updated by Emscripten on memory.grow.
function getWasmMemoryBytes(): number | undefined {
    try {
        const rt = (globalThis as { getDotnetRuntime?: (id: number) => { Module?: { HEAPU8?: Uint8Array } } | undefined })
            .getDotnetRuntime?.(0);
        return rt?.Module?.HEAPU8?.byteLength;
    } catch {
        return undefined;
    }
}

interface HeapSample {
    t: number;     // performance.now() at sample, rounded to ms
    used: number;  // V8 usedJSHeapSize, bytes
    total: number; // V8 totalJSHeapSize, bytes
    wasm?: number; // WebAssembly.Memory byteLength (Blazor/Mono), bytes
}

interface ScriptTiming {
    name: string;
    invoker: string;
    invokerType: string;
    sourceURL: string;
    sourceFunctionName: string;
    sourceCharPosition: number;
    duration: number;
    forcedStyleAndLayoutDuration: number;
    pauseDuration: number;
    startTime: number;
    executionStart: number;
}

interface LongAnimationFrameTiming extends PerformanceEntry {
    duration: number;
    blockingDuration: number;
    renderStart: number;
    styleAndLayoutStart: number;
    scripts: ScriptTiming[];
}

export interface MainThreadDiagnosticsOptions {
    longAnimationFrame?: { enabled?: boolean; thresholdMs?: number };
    mutationStorm?: { enabled?: boolean; threshold?: number; windowMs?: number };
    watchdog?: { enabled?: boolean; stallThresholdMs?: number };
    heapSampler?: { enabled?: boolean; periodMs?: number; maxSamples?: number };
}

// Snapshot of subsystem state at watchdog-stall time. Returning an empty
// object skips the entry; throws are caught and logged. Providers must run
// fast (no async, no IO) — the watchdog is already firing because main is
// stalled, so these are sampled inline on the very first tick after recovery.
export type StallSnapshotProvider = () => Record<string, unknown>;

export class MainThreadDiagnostics {
    private static loafObserver: PerformanceObserver | null = null;
    private static originalMutationObserver: typeof MutationObserver | null = null;
    private static stallSnapshotProviders = new Map<string, StallSnapshotProvider>();
    private static watchdogIntervalId: number | null = null;
    private static watchdogVisibilityHandler: (() => void) | null = null;
    private static heapSamplerIntervalId: number | null = null;
    private static heapSamples: HeapSample[] = [];
    private static heapSamplesMax = 0;

    // Register a function that returns a small JSON-friendly object describing
    // the caller subsystem's state. Called inline from the watchdog stall log.
    // The name is used as the JSON key and to deduplicate registrations.
    public static addStallSnapshotProvider(name: string, provider: StallSnapshotProvider): () => void {
        this.stallSnapshotProviders.set(name, provider);
        return () => { this.stallSnapshotProviders.delete(name); };
    }

    public static init(options?: MainThreadDiagnosticsOptions): void {
        // Persistent kill switch: if the scope is silenced (LogLevel.None — i.e.
        // even errorLog is null), skip installation. Log directly via console
        // since the regular log channels are off in this state.
        if (!errorLog) {
            console.log('[MainThreadDiagnostics] init: disabled (log level set to None)');
            return;
        }

        const o = options ?? {};
        if (o.longAnimationFrame?.enabled !== false)
            this.installLongAnimationFrameReporter(o.longAnimationFrame?.thresholdMs ?? 400);
        if (o.mutationStorm?.enabled !== false)
            this.installMutationObserverStormDetector(
                o.mutationStorm?.threshold ?? 100,
                o.mutationStorm?.windowMs ?? 1000);
        // Heap sampler must be installed before watchdog so a stall report has
        // at least the steady-state trajectory available to dump.
        if (o.heapSampler?.enabled !== false)
            this.installHeapSampler(
                o.heapSampler?.periodMs ?? 10000,
                o.heapSampler?.maxSamples ?? 30);
        if (o.watchdog?.enabled !== false)
            this.installFrameWatchdog(o.watchdog?.stallThresholdMs ?? 1000);

        const enabled = [
            this.loafObserver ? 'longAnimationFrame' : null,
            this.originalMutationObserver ? 'mutationStorm' : null,
            this.heapSamplerIntervalId !== null ? 'heapSampler' : null,
            this.watchdogIntervalId !== null ? 'watchdog' : null,
        ].filter(Boolean);
        infoLog?.log(`init: enabled monitors: ${enabled.length ? enabled.join(', ') : 'none'}`);
    }

    public static installLongAnimationFrameReporter(thresholdMs = 400): void {
        if (this.loafObserver) return;
        if (typeof PerformanceObserver === 'undefined') {
            infoLog?.log('installLongAnimationFrameReporter: skipped — PerformanceObserver not available');
            return;
        }

        const supported = PerformanceObserver.supportedEntryTypes;
        const entryType = supported.includes('long-animation-frame')
            ? 'long-animation-frame'
            : supported.includes('longtask') ? 'longtask' : null;
        if (!entryType) {
            infoLog?.log('installLongAnimationFrameReporter: skipped — neither long-animation-frame nor longtask supported');
            return;
        }

        // Per-signature dedupe: skip if same offender reported within window.
        const lastReportedAt = new Map<string, number>();
        const shouldReport = (signature: string): boolean => {
            const now = performance.now();
            const last = lastReportedAt.get(signature) ?? 0;
            if (now - last < LOAF_DEDUPE_WINDOW_MS) return false;
            lastReportedAt.set(signature, now);
            return true;
        };

        try {
            this.loafObserver = new PerformanceObserver(list => {
                for (const entry of list.getEntries()) {
                    if (entry.duration < thresholdMs) continue;
                    if (entryType === 'long-animation-frame') {
                        const e = entry as LongAnimationFrameTiming;
                        const topScript = e.scripts.length
                            ? e.scripts.reduce((a, b) => a.duration >= b.duration ? a : b)
                            : null;
                        const signature = topScript
                            ? `${topScript.sourceURL}:${topScript.sourceFunctionName}`
                            : '<no-script>';
                        if (!shouldReport(signature)) continue;
                        // Full payload is inlined as JSON into the message via
                        // compactJson(): survives "Copy console", log
                        // uploaders, and DevTools rendering quirks. The
                        // human-readable summary stays first so the line is
                        // still scannable; payload= tail is for post-hoc
                        // inspection (JSON.parse the substring after "payload=").
                        const scriptsSummary = e.scripts
                            .map(s => {
                                const inv = s.invoker
                                    ? `(${s.invoker.length > 60 ? s.invoker.slice(0, 60) + '…' : s.invoker})`
                                    : '';
                                return `${s.invokerType || '?'}${inv}@${Math.round(s.duration)}ms`;
                            })
                            .join(',');
                        const payload = {
                            signature,
                            duration: Math.round(e.duration),
                            blockingDuration: Math.round(e.blockingDuration),
                            renderStart: Math.round(e.renderStart - e.startTime),
                            styleAndLayoutStart: Math.round(e.styleAndLayoutStart - e.startTime),
                            scripts: e.scripts.map(s => ({
                                invokerType: s.invokerType,
                                invoker: s.invoker,
                                sourceURL: s.sourceURL,
                                sourceFunctionName: s.sourceFunctionName,
                                sourceCharPosition: s.sourceCharPosition,
                                duration: Math.round(s.duration),
                                forcedStyleAndLayoutDuration: Math.round(s.forcedStyleAndLayoutDuration),
                                pauseDuration: Math.round(s.pauseDuration),
                            })),
                        };
                        warnLog?.log(
                            `long-animation-frame ${signature} `
                            + `dur=${Math.round(e.duration)}ms `
                            + `block=${Math.round(e.blockingDuration)}ms`
                            + (scriptsSummary ? ` scripts=[${scriptsSummary}]` : '')
                            + ` payload=${compactJson(payload)}`);
                    } else {
                        const signature = `longtask:${entry.name}`;
                        if (!shouldReport(signature)) continue;
                        const payload = {
                            duration: Math.round(entry.duration),
                            startTime: Math.round(entry.startTime),
                            name: entry.name,
                        };
                        warnLog?.log(
                            `longtask ${entry.name} dur=${Math.round(entry.duration)}ms`
                            + ` payload=${compactJson(payload)}`);
                    }
                }
            });
            this.loafObserver.observe({ type: entryType, buffered: true });
            infoLog?.log(
                `installLongAnimationFrameReporter: installed `
                + `(entryType=${entryType}, threshold=${thresholdMs}ms, `
                + `dedupeWindow=${LOAF_DEDUPE_WINDOW_MS}ms)`);
        }
        catch (e) {
            errorLog?.log('installLongAnimationFrameReporter: failed', e);
            this.loafObserver = null;
        }
    }

    public static installMutationObserverStormDetector(threshold = 100, windowMs = 1000): void {
        if (this.originalMutationObserver) return;
        if (typeof MutationObserver === 'undefined') {
            infoLog?.log('installMutationObserverStormDetector: skipped — MutationObserver not available');
            return;
        }

        this.originalMutationObserver = window.MutationObserver;
        const Original = this.originalMutationObserver;

        const Wrapped = function (this: unknown, callback: MutationCallback) {
            const calls: number[] = [];
            const createdAt = new Error('MutationObserver created here').stack ?? '<no stack>';
            let lastReportedAt = 0;

            const wrapped: MutationCallback = (records, observer) => {
                const now = performance.now();
                calls.push(now);
                while (calls.length && now - calls[0] > windowMs)
                    calls.shift();
                if (calls.length >= threshold && now - lastReportedAt >= MO_STORM_COOLDOWN_MS) {
                    lastReportedAt = now;
                    // Full payload (createdAt stack + sample records) inlined
                    // as JSON via compactJson() — see LoAF comment above.
                    const firstTarget = records.length ? describeNode(records[0].target) : '<no-records>';
                    const payload = {
                        createdAt,
                        sampleRecords: records.slice(0, 3).map(r => ({
                            type: r.type,
                            target: describeNode(r.target),
                            added: Array.from(r.addedNodes).slice(0, 3).map(describeNode),
                            removed: Array.from(r.removedNodes).slice(0, 3).map(describeNode),
                            attributeName: r.attributeName,
                        })),
                    };
                    errorLog?.log(
                        `MutationObserver storm: ${calls.length} callbacks in ${windowMs}ms `
                        + `target=${firstTarget}`
                        + ` payload=${compactJson(payload)}`);
                }
                callback(records, observer);
            };

            return new Original(wrapped);
        } as unknown as typeof MutationObserver;
        Wrapped.prototype = Original.prototype;

        window.MutationObserver = Wrapped;
        infoLog?.log(
            `installMutationObserverStormDetector: installed `
            + `(threshold=${threshold}/${windowMs}ms, cooldown=${MO_STORM_COOLDOWN_MS}ms)`);
    }

    public static installHeapSampler(periodMs = 10000, maxSamples = 30): void {
        if (this.heapSamplerIntervalId !== null) return;
        if (typeof setInterval === 'undefined') {
            infoLog?.log('installHeapSampler: skipped — setInterval not available');
            return;
        }
        // performance.memory is Chromium-only and may be missing under
        // privacy extensions, certain Permissions-Policy headers, or in
        // non-Chromium engines. Warn (not info) so the absence is obvious in
        // logs when later watchdog reports lack heap data.
        if (!getMemory()) {
            warnLog?.log('installHeapSampler: skipped — performance.memory not available; '
                + 'heap trajectory will be missing from stall reports');
            return;
        }

        this.heapSamplesMax = maxSamples;
        const sample = (): void => {
            const m = getMemory();
            if (!m) return;
            const wasm = getWasmMemoryBytes();
            this.heapSamples.push({
                t: Math.round(performance.now()),
                used: m.usedJSHeapSize,
                total: m.totalJSHeapSize,
                ...(wasm !== undefined ? { wasm } : {}),
            });
            // Drop oldest entries past the cap. shift() in a loop handles the
            // case where maxSamples was reduced between calls.
            while (this.heapSamples.length > this.heapSamplesMax)
                this.heapSamples.shift();
        };
        sample(); // baseline
        this.heapSamplerIntervalId = window.setInterval(sample, periodMs);
        infoLog?.log(`installHeapSampler: installed (period=${periodMs}ms, max=${maxSamples})`);
    }

    public static installFrameWatchdog(stallThresholdMs = 1000): void {
        if (this.watchdogIntervalId !== null) return;
        if (typeof setInterval === 'undefined') {
            infoLog?.log('installFrameWatchdog: skipped — setInterval not available');
            return;
        }

        // Uses setInterval (registered once) instead of recursive
        // requestAnimationFrame so that Chrome async stack traces don't attach
        // every prior tick to each log entry. The stall-detection precision
        // is wall-clock-based, so frame alignment isn't required.
        const checkPeriodMs = Math.max(100, Math.floor(stallThresholdMs / 4));
        let lastTick = performance.now();
        let stallStart = 0;

        // Re-baseline on visibility transitions: otherwise a stale lastTick
        // from the previous regime (e.g. throttled hidden tick) would make
        // the first tick of the new regime look like a stall.
        const onVisibilityChange = () => {
            const now = performance.now();
            if (stallStart) {
                warnLog?.log(`watchdog: recovered after ${Math.round(now - stallStart)}ms total stall (visibility change)`);
                stallStart = 0;
            }
            lastTick = now;
        };
        if (typeof document !== 'undefined') {
            this.watchdogVisibilityHandler = onVisibilityChange;
            document.addEventListener('visibilitychange', onVisibilityChange);
        }

        this.watchdogIntervalId = window.setInterval(() => {
            const now = performance.now();
            const delta = now - lastTick;
            const isHidden = typeof document !== 'undefined' && document.visibilityState === 'hidden';
            // Hidden-tab timers throttle without an upper bound (minute-long buckets,
            // or a frozen tab), so a delta measured here says nothing about the thread.
            if (isHidden) {
                if (stallStart) {
                    warnLog?.log(`watchdog: recovered after ${Math.round(now - stallStart)}ms total stall (tab hidden)`);
                    stallStart = 0;
                }
                lastTick = now;
                return;
            }
            if (delta > stallThresholdMs) {
                if (!stallStart) {
                    stallStart = lastTick;
                    // Capture a fresh post-recovery sample so the trajectory
                    // shows used/total *right after* the stall — typically the
                    // most informative datapoint when GC or memory pressure is
                    // suspected. The sampler's own setInterval was frozen
                    // during the stall, so the buffer otherwise jumps from
                    // pre-stall to >stallThresholdMs later.
                    const m = getMemory();
                    if (m) {
                        const wasm = getWasmMemoryBytes();
                        this.heapSamples.push({
                            t: Math.round(now),
                            used: m.usedJSHeapSize,
                            total: m.totalJSHeapSize,
                            ...(wasm !== undefined ? { wasm } : {}),
                        });
                        while (this.heapSamples.length > this.heapSamplesMax)
                            this.heapSamples.shift();
                    }
                    // Always emit a heap= marker so the absence (vs. empty
                    // buffer vs. unavailable API) is unambiguous in logs.
                    const heapTail = this.heapSamples.length
                        ? ` heap=${compactJson(this.heapSamples)}`
                        : (this.heapSamplerIntervalId !== null
                            ? ' heap=[] (sampler installed but buffer empty)'
                            : ' heap=unavailable');
                    const snapshots: Record<string, unknown> = {};
                    for (const [name, provider] of this.stallSnapshotProviders) {
                        try {
                            const snap = provider();
                            if (Object.keys(snap).length > 0)
                                snapshots[name] = snap;
                        } catch (e) {
                            snapshots[name] = { error: e instanceof Error ? e.message : String(e) };
                        }
                    }
                    const snapTail = Object.keys(snapshots).length
                        ? ` snapshots=${compactJson(snapshots)}`
                        : '';
                    errorLog?.log(
                        `watchdog: main thread stalled for ${Math.round(delta)}ms`
                        + heapTail
                        + snapTail);
                }
            } else if (stallStart) {
                warnLog?.log(`watchdog: recovered after ${Math.round(now - stallStart)}ms total stall`);
                stallStart = 0;
            }
            lastTick = now;
        }, checkPeriodMs);
        infoLog?.log(`installFrameWatchdog: installed (stallThreshold=${stallThresholdMs}ms, checkPeriod=${checkPeriodMs}ms)`);
    }

    public static dispose(): void {
        this.loafObserver?.disconnect();
        this.loafObserver = null;
        if (this.originalMutationObserver) {
            window.MutationObserver = this.originalMutationObserver;
            this.originalMutationObserver = null;
        }
        if (this.watchdogIntervalId !== null) {
            clearInterval(this.watchdogIntervalId);
            this.watchdogIntervalId = null;
        }
        if (this.watchdogVisibilityHandler !== null) {
            if (typeof document !== 'undefined')
                document.removeEventListener('visibilitychange', this.watchdogVisibilityHandler);
            this.watchdogVisibilityHandler = null;
        }
        if (this.heapSamplerIntervalId !== null) {
            clearInterval(this.heapSamplerIntervalId);
            this.heapSamplerIntervalId = null;
        }
        this.heapSamples = [];
        this.heapSamplesMax = 0;
    }
}

function describeNode(n: Node | null): string {
    if (!n) return '<null>';
    if (n instanceof Element) {
        const id = n.id ? `#${n.id}` : '';
        const cls = n.className && typeof n.className === 'string'
            ? `.${n.className.split(/\s+/).filter(Boolean).slice(0, 2).join('.')}`
            : '';
        return `${n.tagName.toLowerCase()}${id}${cls}`;
    }
    return `[${n.nodeName}]`;
}
