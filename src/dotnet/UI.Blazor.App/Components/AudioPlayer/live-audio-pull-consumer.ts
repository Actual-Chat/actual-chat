// Default LiveAudioPullConsumer — glue between the TS-side pull driver
// (src/nodejs/src/audio/live-audio-pull.ts) and `PullAudioRenderer`.
//
// The pull driver tells us "a new sub-stream started (streamInfo + header)";
// we spawn a renderer for it and feed every parsed Opus packet straight
// through. When the sub-stream ends we let the renderer flush and tear down.
//
// Also exposes `startLiveAudioListen` / `startLiveAudioReplay` — single-call
// entry points that initialize the shared Api with the live-audio module, run
// the pull driver with a default consumer, and return a stop handle. These are
// what the .NET side (or a `window.*` entry for manual smoke-testing) should
// call once the full rollout is wired up.

import { Api, liveAudioStreamsApi,
    type LiveStreamSettingsDto } from 'api';
import { getLogs } from 'logging';
import type { ActualOpusStreamHeader, ActualOpusPacket } from '../../../../nodejs/src/audio/actual-opus-stream-parser';
import {
    startLiveAudioPull,
    LiveAudioPullMode,
    type LiveAudioPullConsumer,
    type LiveAudioPullDriver,
} from '../../../../nodejs/src/audio/live-audio-pull';
import type { LiveStreamInfoDto } from '../../../../nodejs/src/api/live-audio-streams-api';
import { ConnectivityUI } from '../../../UI.Blazor/Services/ConnectivityUI/connectivity-ui';
import { PullAudioRenderer } from './pull-audio-renderer';
import { AudioPlayer } from './audio-player';

const { debugLog, warnLog } = getLogs('LiveAudioPull');

/** 100-ns ticks per millisecond — Moment/TimeSpan serialize as int64 ticks. */
const TICKS_PER_MS = 10_000;

/**
 * Create a consumer that spawns a `PullAudioRenderer` per sub-stream and
 * forwards Opus packets into it. The consumer's token is the renderer itself,
 * so `onOpusPacket` / `onStreamEnded` can reach it without a lookup.
 */
export function createDefaultLiveAudioPullConsumer(): LiveAudioPullConsumer {
    return {
        async onStreamStarted(
            streamInfo: LiveStreamInfoDto,
            _playsAtTicks: number,
            header: ActualOpusStreamHeader,
        ): Promise<PullAudioRenderer> {
            const recordedAtMs = momentTicksToMs(streamInfo.BeginsAt);
            debugLog?.log(
                `onStreamStarted: stream=${streamInfo.StreamId}, author=${streamInfo.AuthorId}, ` +
                `recordedAtMs=${recordedAtMs.toFixed(0)}, preSkip=${header.preSkip}`);
            return await PullAudioRenderer.create({
                streamInfo,
                recordedAtMs,
                preSkip: header.preSkip,
            });
        },

        onOpusPacket(token: unknown, packet: ActualOpusPacket): void {
            (token as PullAudioRenderer).feed(packet.data);
        },

        onStreamEnded(token: unknown, error?: unknown): void {
            const renderer = token as PullAudioRenderer;
            // Flush (mustAbort=false) unless the stream ended with an error.
            void renderer.end(error !== undefined).catch((e: unknown) => {
                warnLog?.log('renderer.end failed:', e);
            });
        },
    };
}

/**
 * Convert a .NET Moment value (int64 ticks from Unix epoch, 100-ns units) into
 * JS epoch-milliseconds. Uses `Number` — lossy beyond ±2^53 ms (~285 000 yr) so
 * fine for wall-clock moments.
 */
function momentTicksToMs(ticks: number): number {
    return ticks / TICKS_PER_MS;
}

/**
 * One-time init of the shared Api for the live-audio-pull module. Idempotent.
 * Call once before `startLiveAudioListen`/`startLiveAudioReplay`.
 *
 * Mirrors the `initVideoRpc` pattern — `Api.url` must already be set by
 * BrowserInit, and we bind .NET connectivity so the peer won't attempt to
 * connect while the .NET-side rpc is down.
 */
export function initLiveAudioPullRpc(): void {
    Api.init(undefined, liveAudioStreamsApi);
    Api.bindDotNetRpcConnected(ConnectivityUI);
}

export interface StartLiveAudioListenOptions {
    session: string;
    chatId: string;
    settings?: LiveStreamSettingsDto;
    /** If set, sub-streams from this author are dropped (skip-own-audio). */
    ownAuthorId?: string | null;
}

export interface StartLiveAudioReplayOptions {
    session: string;
    chatId: string;
    /** Moment ticks (100-ns units from Unix epoch). Accepts BigInt or a
     *  decimal string when the value overflows Number.MAX_SAFE_INTEGER. */
    startAtTicks: bigint | number | string;
    /** TimeSpan ticks. Default 0. Same overflow caveats as startAtTicks. */
    rewindOffsetTicks?: bigint | number | string;
    /** Default 1.0. */
    speed?: number;
    /** If set, sub-streams from this author are dropped (skip-own-audio). */
    ownAuthorId?: string | null;
}

/**
 * Start a live (real-time) audio listen via the TS-pull path. The returned
 * driver's `whenStopped` resolves on normal stream end / abort; call `stop()`
 * to cancel it. Ensures `AudioPlayer` is initialized (so the shared decoder
 * worker is up before the first packet arrives).
 */
export async function startLiveAudioListen(
    options: StartLiveAudioListenOptions,
): Promise<LiveAudioPullDriver> {
    initLiveAudioPullRpc();
    await AudioPlayer.ensureInitialized();
    debugLog?.log(`listen: chatId=${options.chatId}, ownAuthor=${options.ownAuthorId ?? '-'}`);
    return startLiveAudioPull(
        {
            session: options.session,
            chatId: options.chatId,
            mode: LiveAudioPullMode.Live,
            settings: options.settings,
            ownAuthorId: options.ownAuthorId ?? null,
        },
        createDefaultLiveAudioPullConsumer());
}

/** Start a replay (historical) audio pull. */
export async function startLiveAudioReplay(
    options: StartLiveAudioReplayOptions,
): Promise<LiveAudioPullDriver> {
    initLiveAudioPullRpc();
    await AudioPlayer.ensureInitialized();
    const startAt = ticksArgToBigInt(options.startAtTicks);
    const rewindOffset = ticksArgToBigInt(options.rewindOffsetTicks ?? 0);
    debugLog?.log(`replay: chatId=${options.chatId}, startAt=${startAt}`);
    return startLiveAudioPull(
        {
            session: options.session,
            chatId: options.chatId,
            mode: LiveAudioPullMode.Replay,
            startAtTicks: startAt,
            rewindOffsetTicks: rewindOffset,
            speed: options.speed ?? 1.0,
            ownAuthorId: options.ownAuthorId ?? null,
        },
        createDefaultLiveAudioPullConsumer());
}

/** Accept ticks as BigInt (preferred, lossless), decimal string (from .NET
 *  JSON interop where `long` exceeds `Number.MAX_SAFE_INTEGER`), or number. */
function ticksArgToBigInt(v: bigint | number | string): bigint {
    if (typeof v === 'bigint') return v;
    if (typeof v === 'string') return BigInt(v);
    return BigInt(Math.trunc(v));
}

/**
 * Token-based bridge for .NET. Blazor interop can't hold a JS object across
 * calls, so each active driver is keyed by a monotonically-increasing integer
 * token that .NET stores and passes back into `stop()`.
 *
 * Exported on `window.blazorApp.LiveAudioPullBridge` via
 * `UI.Blazor.App/exports.ts`. Call as:
 *   const token = await blazorApp.LiveAudioPullBridge.startListen(session, chatId);
 *   // … later …
 *   await blazorApp.LiveAudioPullBridge.stop(token);
 */
export class LiveAudioPullBridge {
    private static readonly drivers = new Map<number, LiveAudioPullDriver>();
    private static nextToken = 1;

    /** Returns a numeric token .NET should hold to later stop the driver.
     *  `ownAuthorId` may be null when filtering own audio isn't wanted (e.g.
     *  the current user has no author record in this chat). */
    public static async startListen(
        session: string,
        chatId: string,
        ownAuthorId: string | null,
    ): Promise<number> {
        const driver = await startLiveAudioListen({ session, chatId, ownAuthorId });
        return this.register(driver);
    }

    public static async startReplay(
        session: string,
        chatId: string,
        startAtTicks: bigint | number | string,
        rewindOffsetTicks: bigint | number | string,
        speed: number,
        ownAuthorId: string | null,
    ): Promise<number> {
        const driver = await startLiveAudioReplay({
            session, chatId, startAtTicks, rewindOffsetTicks, speed, ownAuthorId,
        });
        return this.register(driver);
    }

    /** Stop + unregister. No-op if token is unknown (already stopped or bogus). */
    public static async stop(token: number): Promise<void> {
        const driver = this.drivers.get(token);
        if (!driver) {
            debugLog?.log(`stop: unknown token ${token}`);
            return;
        }
        this.drivers.delete(token);
        try { await driver.stop(); }
        catch (e) { warnLog?.log(`stop(${token}) failed:`, e); }
    }

    /** Stop all drivers — used on logout / app teardown. */
    public static async stopAll(): Promise<void> {
        const tokens = [...this.drivers.keys()];
        await Promise.all(tokens.map(t => this.stop(t)));
    }

    private static register(driver: LiveAudioPullDriver): number {
        const token = this.nextToken++;
        this.drivers.set(token, driver);
        // Auto-unregister on natural completion so the map doesn't leak.
        void driver.whenStopped
            .catch(() => { /* already logged by the driver */ })
            .finally(() => this.drivers.delete(token));
        return token;
    }
}
