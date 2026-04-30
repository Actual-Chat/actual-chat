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
}

export class MainThreadDiagnostics {
    private static loafObserver: PerformanceObserver | null = null;
    private static originalMutationObserver: typeof MutationObserver | null = null;
    private static watchdogIntervalId: number | null = null;
    private static watchdogVisibilityHandler: (() => void) | null = null;

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
        if (o.watchdog?.enabled !== false)
            this.installFrameWatchdog(o.watchdog?.stallThresholdMs ?? 1000);

        const enabled = [
            this.loafObserver ? 'longAnimationFrame' : null,
            this.originalMutationObserver ? 'mutationStorm' : null,
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
                        warnLog?.log('long-animation-frame', {
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
                            })),
                        });
                    } else {
                        const signature = `longtask:${entry.name}`;
                        if (!shouldReport(signature)) continue;
                        warnLog?.log('longtask', {
                            duration: Math.round(entry.duration),
                            startTime: Math.round(entry.startTime),
                            name: entry.name,
                        });
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
                    errorLog?.log(
                        `MutationObserver storm: ${calls.length} callbacks in ${windowMs}ms`,
                        {
                            createdAt,
                            sampleRecords: records.slice(0, 3).map(r => ({
                                type: r.type,
                                target: describeNode(r.target),
                                added: Array.from(r.addedNodes).slice(0, 3).map(describeNode),
                                removed: Array.from(r.removedNodes).slice(0, 3).map(describeNode),
                                attributeName: r.attributeName,
                            })),
                        });
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

        // Hidden tabs throttle setInterval to ~1s. Without filtering, every
        // throttled tick (delta ~1000-1100ms) would be reported as a stall.
        // We only ignore deltas that fit the throttling profile; bigger deltas
        // are real stalls on top of throttling and still get reported.
        const HIDDEN_THROTTLE_FLOOR_MS = 1100;

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
            if (isHidden && delta < HIDDEN_THROTTLE_FLOOR_MS) {
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
                    errorLog?.log(
                        `watchdog: main thread stalled for ${Math.round(delta)}ms`
                        + (isHidden ? ' (tab hidden — throttling-adjusted)' : ''));
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
