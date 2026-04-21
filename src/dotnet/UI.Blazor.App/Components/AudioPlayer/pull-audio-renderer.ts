// TS-pull audio renderer — pairs with `live-audio-pull` (the TS-side alternative
// to the .NET `AudioTrackPlayer` frame path). Owns one feeder-worklet + decoder
// session per sub-stream and exposes a single `feed(packet)` hot-path that goes
// straight into the OpusDecoderWorker via raw postMessage (no Blazor interop).
//
// Reuses the shared `OpusDecoderWorker` Worker instance owned by `AudioPlayer`
// (see `getDecoderWorkerInstance` / `getDecoderWorker`) and the shared
// `audioContextSource`. Each renderer allocates its own streamId + feeder node.

import type { LiveStreamInfoDto } from '../../../../nodejs/src/api/live-audio-streams-api';
import {
    AppAudioContext,
    AudioContextRef,
    AudioContextAction,
    audioContextSource,
} from '../../Services/audio-context-source';
import {
    AttachedAudioContextTrait,
    AudioContextTrait,
    DemandInteractiveUI,
    DestinationFallbackTrait,
} from '../../Services/audio-context-traits';
import { AudioPlayer, getDecoderWorker, getDecoderWorkerInstance } from './audio-player';
import { FeederAudioWorkletNode } from './worklets/feeder-audio-worklet-node';
import { FeederState } from './worklets/feeder-audio-worklet-contract';
import { AudioVideoSync } from 'audio-video-sync';
import { rpcSendNoWait, rpcNoWait } from 'rpc';
import { catchErrors } from 'promises';
import { Log, getLogs } from 'logging';

const { logScope, debugLog, warnLog } = getLogs('LiveAudioPull');

let nextInternalId = 0;

interface TraitAttached extends AttachedAudioContextTrait {
    feederNode: FeederAudioWorkletNode;
    channel: MessageChannel;
    closed: boolean;
}

interface PullRendererOptions {
    streamInfo: LiveStreamInfoDto;
    /** Moment (Unix-epoch 100-ns ticks) the stream was recorded. Passed to AudioVideoSync. */
    recordedAtMs: number;
    /** Opus `PreSkip` (samples), from the A_OPUS_S header. */
    preSkip: number;
}

/**
 * A minimal, Blazor-free audio renderer fed by a TS-side pull pipeline.
 *
 * Lifecycle:
 *   const r = await PullAudioRenderer.create({ streamInfo, recordedAtMs, preSkip });
 *   r.feed(opusPacket);  // … many times, hot path
 *   await r.end(false);  // flush remaining frames, let feeder finish
 *                        //   or: await r.end(true) to abort immediately
 */
export class PullAudioRenderer {
    private readonly internalId: string;
    private readonly options: PullRendererOptions;
    private readonly trait: FeederTrait;
    private contextRef: AudioContextRef | undefined;
    private playingAction: AudioContextAction | undefined;
    private attached: TraitAttached | undefined;
    private ended = false;
    /** Resolved when the feeder reports 'ended' after an end() call. */
    private readonly whenEnded: Promise<void>;
    private resolveWhenEnded!: () => void;
    private lastLatencyLogTime = 0;

    public onPlaybackStateChanged?: (state: FeederState) => void;

    private constructor(options: PullRendererOptions) {
        this.options = options;
        this.internalId = `pull-${nextInternalId++}`;
        this.whenEnded = new Promise<void>(resolve => { this.resolveWhenEnded = resolve; });
        this.trait = new FeederTrait(this, this.internalId);
    }

    /** Create + attach a renderer. Returns once the feeder is wired up and the
     *  AudioContext is ready (or once the attach race settles). */
    public static async create(options: PullRendererOptions): Promise<PullAudioRenderer> {
        await AudioPlayer.ensureInitialized();
        const r = new PullAudioRenderer(options);
        await r.startInternal();
        return r;
    }

    /** Feed one Opus packet into the decoder. Non-blocking, no-wait. The caller
     *  might pass a view into a larger shared ArrayBuffer (common when items
     *  come off a MessagePack decoder) — in that case we can't transfer the
     *  backing buffer without neutering unrelated data, so copy into a fresh
     *  buffer first. The copy is ~O(120 bytes) per 20-ms frame, negligible. */
    public feed(packet: Uint8Array): void {
        if (this.ended) return;
        const ownsBuffer =
            packet.byteOffset === 0 && packet.byteLength === packet.buffer.byteLength;
        const data = ownsBuffer ? packet : packet.slice();
        const buf = data.buffer as ArrayBuffer;
        rpcSendNoWait(
            getDecoderWorkerInstance(),
            'frame',
            [this.internalId, buf, data.byteOffset, data.byteLength],
            [buf]);
    }

    /** Request end. `mustAbort = true` discards queued audio immediately. */
    public async end(mustAbort: boolean): Promise<void> {
        if (this.ended) return;
        this.ended = true;
        debugLog?.log(`#${this.internalId}.end mustAbort=${mustAbort}`);
        try {
            await getDecoderWorker().end(this.internalId, mustAbort);
        }
        catch (e) {
            warnLog?.log(`#${this.internalId}.end: decoder.end error:`, e);
        }
        // Wait for the feeder's 'ended' state to fire back, then tear down.
        // The feeder reports 'ended' after playing out (or immediately on abort).
        await Promise.race([
            this.whenEnded,
            // Safety: if the feeder never fires 'ended', don't hang forever.
            new Promise<void>(resolve => setTimeout(resolve, 5_000)),
        ]);
        this.dispose();
    }

    /** Called by FeederTrait when the feeder worklet reports state. */
    public onFeederStateChanged(state: FeederState): void {
        if (this.onPlaybackStateChanged) {
            try { this.onPlaybackStateChanged(state); }
            catch (e) { warnLog?.log(`#${this.internalId}: onPlaybackStateChanged threw:`, e); }
        }

        const authorId = this.options.streamInfo.AuthorId;
        if (state.playbackState !== 'ended' && authorId) {
            AudioVideoSync.update(
                authorId,
                state.playingAt,
                this.options.recordedAtMs,
                state.playbackState);
        }

        if (state.playbackState === 'ended') {
            if (authorId)
                AudioVideoSync.clear(authorId);
            this.resolveWhenEnded();
        }
    }

    // --- Private ---

    private async startInternal(): Promise<void> {
        debugLog?.log(
            `#${this.internalId}.start: stream=${this.options.streamInfo.StreamId}, ` +
            `preSkip=${this.options.preSkip}`);

        this.contextRef = audioContextSource.createRef(this.trait, DemandInteractiveUI.instance);

        // Start a playing action — attaches the trait (creates feeder + decoder
        // session) and calls resume(preSkip) on the feeder so it plays as frames
        // arrive.
        this.playingAction = this.contextRef.run(async () => {
            const attached = this.contextRef!.getTrait<TraitAttached>(this.trait);
            if (!attached) return;
            this.attached = attached;
            await getDecoderWorker().resume(this.internalId, rpcNoWait);
            await attached.feederNode.resume(this.options.preSkip);
        });

        await this.contextRef.whenReady();
    }

    private dispose(): void {
        this.playingAction?.dispose();
        this.playingAction = undefined;
        audioContextSource.removeTrait(this.trait);
        this.contextRef?.dispose();
        this.contextRef = undefined;
        this.attached = undefined;
    }

    // --- internal accessor for the trait ---
    /** @internal */
    public _setAttached(attached: TraitAttached): void { this.attached = attached; }

    /** @internal — handed to the trait. */
    public get _internalId(): string { return this.internalId; }
}

/** Trait + attached pair — same shape as the private FeederNodeTrait in
 *  audio-player.ts, but without the player's .NET-specific bits. */
class FeederTrait implements AudioContextTrait {
    public readonly name: string;

    constructor(
        private readonly renderer: PullAudioRenderer,
        private readonly internalId: string,
    ) {
        this.name = `pull-feeder-${internalId}`;
    }

    public async attach(context: AppAudioContext): Promise<TraitAttached> {
        debugLog?.log(`#${this.internalId}.trait.attach: context:`, Log.ref(context));

        const channel = new MessageChannel();
        const options: AudioWorkletNodeOptions = {
            channelCount: 1,
            channelCountMode: 'explicit',
            numberOfInputs: 0,
            numberOfOutputs: 1,
            outputChannelCount: [1],
        };

        const feederNode = await FeederAudioWorkletNode.create(
            this.internalId,
            channel.port2,
            context,
            'feederWorklet',
            options);

        feederNode.onStateChanged = (state) => this.renderer.onFeederStateChanged(state);

        await getDecoderWorker().init(this.internalId, channel.port1);

        const destination = DestinationFallbackTrait.getDestination(context);
        feederNode.connect(destination);

        const attached: TraitAttached = {
            feederNode,
            channel,
            closed: false,
            onClosed: async (): Promise<void> => {
                if (attached.closed) return;
                attached.closed = true;
                debugLog?.log(`#${this.internalId}.trait.onClosed`);
                await catchErrors(
                    () => getDecoderWorker().close(this.internalId),
                    e => { warnLog?.log(`#${this.internalId}.onClosed decoder.close err:`, e); });
                try { channel.port1.close(); }
                catch (e) { warnLog?.log(`#${this.internalId}.onClosed port1.close err:`, e); }
                try { channel.port2.close(); }
                catch (e) { warnLog?.log(`#${this.internalId}.onClosed port2.close err:`, e); }
                feederNode.onStateChanged = undefined;
                try { feederNode.disconnect(); }
                catch (e) { warnLog?.log(`#${this.internalId}.onClosed feederNode.disconnect err:`, e); }
            },
        };
        return attached;
    }
}

// Silence eslint no-unused — logScope is the `getLogs('LiveAudioPull')` tag,
// referenced implicitly through debugLog/warnLog above.
void logScope;
