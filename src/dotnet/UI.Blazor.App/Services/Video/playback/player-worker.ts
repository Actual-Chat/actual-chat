import { delayAsync } from 'actuallab-core';
import { WebCodecsCompat } from 'web-codecs-compat/init';
import { VIDEO } from 'app-constants';
import { getLogs } from 'logging';
import { createEmptyPlayerStats, type PlayerStats } from '../frame-envelopes';
import type { DecoderLike } from '../operators/decode';
import type { VideoFrameDto } from '../operators/pull';
import { WorkerConnectivityUI } from '../../../Components/AudioRecorder/workers/worker-connectivity-ui';
import { setVideoTraceKill, type VideoTraceKillPeriod } from '../frame-drop-trace';
import { Player, type PlayerConfig } from './player';
import { PlaybackSession } from './session';
import { BgBlurController, bgBlurTap, type BgBlurMode } from './bg-blur-tap';
import { sharedSettingsWorker } from 'shared-settings-worker';
import type {
    PlayerWorker,
    PlayerWorkerOptions,
} from './player-worker-contract';

// Session-singleton model: the decoder pool's TTL semantics depend on
// persistent state across stop/start cycles. Recreating the session
// on every start() would defeat the pool entirely.
let session: PlaybackSession | null = null;
const players = new Map<string, Player>();
// Per-stream WebGPU bg-blur controller. Created lazily by installBgCanvas
// or by start() (whichever lands first), disposed when the player drains.
const bgBlurControllers = new Map<string, BgBlurController>();

function ensureBgBlurController(streamId: string): BgBlurController {
    let c = bgBlurControllers.get(streamId);
    if (!c) {
        c = new BgBlurController();
        bgBlurControllers.set(streamId, c);
    }
    return c;
}

interface PlayerWorkerHooks {
    getStream: (streamId: string) => Promise<AsyncIterable<VideoFrameDto>> | AsyncIterable<VideoFrameDto>;
    createDecoder: (
        codec: string,
        handlers: { onFrame: (frame: VideoFrame) => void; onError: (e: Error) => void },
    ) => DecoderLike;
    // MSTG path returns the generator's track so the worker can fire
    // onTrackReady; canvas path returns null.
    buildBackend: (opts: PlayerWorkerOptions) => {
        config: PlayerConfig['backend'];
        track: MediaStreamTrack | null;
    };
    onTrackReady?: (
        streamId: string,
        kind: PlayerConfig['backend']['kind'],
        track: MediaStreamTrack | null,
    ) => void;
    makeReportLatency?: (streamId: string) => PlayerConfig['reportLatency'];
    initAppConstants?: (appConstants: import('./player-worker-contract').AppConstantsLike) => void;
    reportError?: (streamId: string, error: string) => void;
    reportStreamEnded?: (streamId: string, reason: string) => void;
    reportCodecProven?: (streamId: string, codec: string) => void;
    reportTraceKillInjected?: () => void;
    prewarmRpc?: (apiUrl: string) => void;
}

let hooks: PlayerWorkerHooks | null = null;
// Player instances we stopped locally — so the lifecycle observer suppresses
// onError/onStreamEnded for the resulting abort. Without this, an
// MSTG-to-canvas fallback (stop then immediately start the same
// stream) raises spurious terminal callbacks while the restart is
// mid-flight. Keyed by instance, not streamId: an abandoned (wedged) player
// can outlive a new player started for the same streamId, and a streamId key
// would either suppress the new player's real terminal callbacks or have its
// marker deleted out from under it by the zombie's own late cleanup.
const locallyStopped = new WeakSet<Player>();
const { warnLog } = getLogs('VideoPipeline');

// A pipeline that ignores abort (dead MSTG track, a stuck bitmap conversion)
// must not block restarts forever — bound the wait and abandon it past this.
const STOP_HANG_TIMEOUT_MS = 8_000;

export function __setPlayerWorkerHooks(next: PlayerWorkerHooks | null): void {
    hooks = next;
}

export function __setPlayerWorkerSession(next: PlaybackSession | null): void {
    session = next;
}

export function __getPlayerWorkerSession(): PlaybackSession | null {
    return session;
}

// VideoPlayer's restart loop is the sole authority on what's terminal — every
// non-success outcome is now recoverable from this layer's perspective. Kept
// exported only for legacy import callers.
export function isTerminalStreamError(_error: unknown): boolean {
    return false;
}

function getStreamStallTimeoutMs(): number {
    const video = VIDEO as typeof VIDEO | undefined;
    return Math.max(
        video?.streamExpirationDelayMs ?? 30_000,
        video?.silenceGracePeriodMs ?? 30_000,
    );
}

function ensureSession(): PlaybackSession {
    session ??= new PlaybackSession();
    return session;
}

function ensureHooks(): PlayerWorkerHooks {
    if (!hooks) {
        throw new Error(
            'PlayerWorker: hooks not installed; '
            + 'call __setPlayerWorkerHooks before start()');
    }
    return hooks;
}

// Method order matches the PlayerWorker interface contract:
//   init → prewarm → connectivity → run → query → stop.
export const playerWorkerImpl: PlayerWorker = {
    updateSharedSettings: (settings, noWait) => sharedSettingsWorker.updateSharedSettings(settings, noWait),

    init(appConstants): Promise<void> {
        const h = hooks;
        h?.initAppConstants?.(appConstants);
        return Promise.resolve();
    },

    prewarmRpc(apiUrl: string): Promise<void> {
        hooks?.prewarmRpc?.(apiUrl);
        return Promise.resolve();
    },

    onConnectivityUpdate(
        isOnline: boolean,
        isConnected: boolean,
        isBlazorServer: boolean,
    ): Promise<void> {
        WorkerConnectivityUI.update(isOnline, isConnected, isBlazorServer);
        return Promise.resolve();
    },

    async start(
        opts: PlayerWorkerOptions,
        mstgWritable?: WritableStream<VideoFrame>,
        canvas?: OffscreenCanvas,
    ): Promise<void> {
        // Before any decoder is built: at `full` the VideoDecoder this realm gets
        // is the polyfill's, and awaiting here is what installs it.
        await WebCodecsCompat.whenReadyFor('video-decode');
        if (players.has(opts.streamId)) {
            throw new Error(
                `PlayerWorker.start: stream ${opts.streamId} already running`);
        }

        const s = ensureSession();
        const h = ensureHooks();
        // Trailing transferable args can't ride inside the
        // structured-cloned opts envelope — fold them back here.
        const fullOpts: PlayerWorkerOptions = {
            ...opts,
            ...(canvas !== undefined ? { canvas } : {}),
            ...(mstgWritable !== undefined ? { mstgWritable } : {}),
        };
        const { config: backend, track } = h.buildBackend(fullOpts);
        const player = new Player(s);
        players.set(opts.streamId, player);
        player.setExpectedPaused(opts.expectedPaused === true);
        s.registerStream();

        // Notify BEFORE pulling frames — keeps <video srcObject>/canvas
        // attachment off the critical path of the first keyframe.
        h.onTrackReady?.(opts.streamId, backend.kind, track);

        const bgController = ensureBgBlurController(opts.streamId);
        const playerConfig: PlayerConfig = {
            streamId: opts.streamId,
            getStream: h.getStream,
            targetBufferSpanMs: opts.targetBufferSpanMs,
            frameDurationMs: VIDEO.frameDurationMs,
            initialDecoderConfig: opts.initialDecoderConfig,
            createDecoder: handlers => h.createDecoder(opts.initialDecoderConfig.codec, handlers),
            reportLatency: h.makeReportLatency?.(opts.streamId),
            bgBlurTap: bgBlurTap(bgController),
            backend,
            streamStallTimeoutMs: getStreamStallTimeoutMs(),
            reportCodecProven: h.reportCodecProven
                ? (codec: string): void => h.reportCodecProven?.(opts.streamId, codec)
                : undefined,
        };

        await player.start(playerConfig);

        // Local stops skip the terminal callback so an MSTG→canvas
        // fallback's intermediate abort doesn't race the restart.
        player.whenDone().then(
            () => {
                if (!locallyStopped.has(player))
                    h.reportStreamEnded?.(opts.streamId, 'completed');
            },
            (e: unknown) => {
                if (locallyStopped.has(player))
                    return;

                if (isTerminalStreamError(e)) {
                    h.reportStreamEnded?.(opts.streamId, 'evicted');
                    return;
                }

                const msg = e instanceof Error ? e.message : String(e);
                h.reportError?.(opts.streamId, msg);
            },
        ).finally(() => {
            // Slot-ownership guard: an abandoned zombie's late drain must not
            // dispose the shared controller a replacement player is using.
            const current = players.get(opts.streamId);
            if (current === player) {
                players.delete(opts.streamId);
                s.unregisterStream();
            }
            if (current === player || current === undefined) {
                const c = bgBlurControllers.get(opts.streamId);
                if (c === bgController) {
                    bgBlurControllers.delete(opts.streamId);
                    c.dispose();
                }
            }
            locallyStopped.delete(player);
        });
    },

    setTraceKill(avgPeriod: VideoTraceKillPeriod, stage: number): Promise<boolean> {
        const h = hooks;
        return Promise.resolve(setVideoTraceKill(
            'playback',
            avgPeriod,
            stage,
            () => h?.reportTraceKillInjected?.()));
    },

    requestKeyframe(streamId?: string): Promise<void> {
        // No-op on the worker side: keyframe requests are an RPC hint
        // to the SENDER, hooked in the wiring file. Kept for contract
        // symmetry.
        void streamId;
        return Promise.resolve();
    },

    setExpectedPaused(streamId: string, paused: boolean): Promise<void> {
        const player = players.get(streamId);
        player?.setExpectedPaused(paused);
        return Promise.resolve();
    },

    setAudioCaptureOffsetMs(streamId: string, caOffsetMs: number | null): Promise<void> {
        players.get(streamId)?.setAudioCaptureOffsetMs(caOffsetMs);
        return Promise.resolve();
    },

    installBgCanvas(streamId: string, mode: BgBlurMode, canvas: OffscreenCanvas): Promise<void> {
        // Defensive: catch main↔worker bundle-version skew at the boundary.
        // If main is on an older bundle that sends (streamId, canvas) instead
        // of (streamId, mode, canvas), `mode` here is the OffscreenCanvas and
        // `canvas` is undefined — the renderer ctor would otherwise blow up
        // deep inside with `getContext is not a function`.
        if (!(canvas instanceof OffscreenCanvas)) {
            throw new Error(
                `installBgCanvas: bundle-version skew — expected OffscreenCanvas at arg[2], `
                + `got ${typeof canvas}. Update main bundle (hard-reload, clear service worker).`);
        }
        // TS narrows `mode` to BgBlurMode, but the wire delivers `unknown`. Cast
        // so the runtime check actually inspects the received value (e.g. when
        // an older main bundle sends an OffscreenCanvas in this slot).
        const modeRaw: unknown = mode;
        if (modeRaw !== 'auto'
            && modeRaw !== 'webgpu'
            && modeRaw !== 'webgl'
            && modeRaw !== 'webgl-kawase'
            && modeRaw !== 'canvas2d'
        ) {
            throw new Error(
                `installBgCanvas: unknown mode '${String(modeRaw)}'. Bundle-version skew?`);
        }
        ensureBgBlurController(streamId).install(canvas, mode);
        return Promise.resolve();
    },

    setBgActive(streamId: string, active: boolean): Promise<void> {
        // Controllers persist only while a stream is running. Drop late
        // toggles silently to keep this RPC lock-free on the main side.
        bgBlurControllers.get(streamId)?.setActive(active);
        return Promise.resolve();
    },

    getStats(streamId: string): Promise<PlayerStats> {
        const player = players.get(streamId);
        if (!player)
            return Promise.resolve(createEmptyPlayerStats());

        return Promise.resolve(player.snapshotStats());
    },

    async stop(streamId?: string): Promise<void> {
        if (streamId === undefined) {
            // Snapshot so iteration isn't perturbed by the
            // cleanup-on-drain handler racing us.
            const all = Array.from(players.entries());
            for (const [, p] of all) locallyStopped.add(p);
            for (const [, p] of all) p.stop();
            await Promise.race([
                Promise.allSettled(all.map(([, p]) => p.whenDone())),
                delayAsync(STOP_HANG_TIMEOUT_MS),
            ]);
            for (const [id, p] of all) {
                if (players.get(id) === p)
                    players.delete(id);
            }
            return;
        }

        const player = players.get(streamId);
        if (!player)
            return;

        locallyStopped.add(player);
        player.stop();
        const unwound = await Promise.race([
            player.whenDone().then(() => true, () => true),
            delayAsync(STOP_HANG_TIMEOUT_MS).then(() => false),
        ]);
        if (!unwound && players.get(streamId) === player) {
            // A pipeline that ignores abort must not block restarts forever:
            // abandon it (source already aborted) so start() can reuse the slot.
            warnLog?.log(`stop: pipeline ${streamId} did not unwind in ${STOP_HANG_TIMEOUT_MS}ms — abandoning`);
            players.delete(streamId);
        }
    },
};

export async function disposePlayerWorker(): Promise<void> {
    if (players.size > 0) {
        const all = Array.from(players.values());
        for (const p of all) p.stop();
        await Promise.race([
            Promise.allSettled(all.map(p => p.whenDone())),
            delayAsync(STOP_HANG_TIMEOUT_MS),
        ]);
        players.clear();
    }
    if (session) {
        session.dispose();
        session = null;
    }
}
