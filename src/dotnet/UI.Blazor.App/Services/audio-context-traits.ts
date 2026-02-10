// TODO: remove eslint-disables and fix errors
/* eslint-disable @typescript-eslint/no-unnecessary-condition */
import { Log } from 'logging';
import { DeviceInfo } from 'device-info';
import { createWebRtcAecStream, isWebRtcAecRequired } from './web-rtc-aec';
import { Disposable } from 'disposable';
import { resetMediaSessionMetadata, AppAudioContext } from './audio-context-source';

const { debugLog, errorLog } = Log.get('AudioContextTraits');

// Trait Interfaces

/** Configuration logic that can be attached to an AudioContext when it becomes ready */
export interface AudioContextTrait {
    /** Unique name identifying this trait type */
    readonly name: string;

    /** Called when an AudioContext becomes ready to configure this trait */
    attach(context: AppAudioContext): AttachedAudioContextTrait | Promise<AttachedAudioContextTrait>;
}

/** Lifecycle hooks for a trait attached to an AudioContext */
export interface AttachedAudioContextTrait {
    /** Called when the first ref is acquired (context becomes "in use") */
    onUsed?(): void | Promise<void>;

    /** Called when the last ref is released (context becomes "unused") */
    onUnused?(): void | Promise<void>;

    /** Called when the AudioContext is closing */
    onClosed?(): void | Promise<void>;
}

/** Symbol for empty attached trait (no lifecycle callbacks needed) */
export const emptyAttachedTrait: AttachedAudioContextTrait = Object.freeze({});


// Specific Traits

/** Singleton marker trait: when present on a ref, signals that context break should proactively demand user interaction via InteractiveUI. */
export class DemandInteractiveUI implements AudioContextTrait {
    public static readonly instance = new DemandInteractiveUI();
    public readonly name = 'demand-interactive-ui';

    private constructor() { /* empty */ }

    public attach(): AttachedAudioContextTrait {
        return emptyAttachedTrait;
    }
}

/** Fallback destination node for iOS Safari to expose mediaSession metadata at lock screen */
export class DestinationFallbackTrait implements AudioContextTrait {
    public readonly name = 'destination-fallback';

    /** Returns true if this fallback is required on the current platform */
    public static get isRequired(): boolean { return DeviceInfo.isIos; }

    /** Gets the fallback destination node if present, or undefined */
    public static getDestinationFallback(context: AppAudioContext): AudioNode | undefined {
        const fallback = context.traits?.get('destination-fallback') as AttachedDestinationFallback | undefined;
        return fallback?.destination;
    }

    /** Gets the destination node to use (fallback if present, otherwise default) */
    public static getDestination(context: AppAudioContext): AudioNode {
        return this.getDestinationFallback(context) ?? context.destination;
    }

    public attach(context: AppAudioContext): AttachedDestinationFallback {
        return new AttachedDestinationFallback(context);
    }
}

/** Attached destination fallback that manages the audio element lifecycle */
export class AttachedDestinationFallback implements AttachedAudioContextTrait, Disposable {
    private readonly audio: HTMLAudioElement;
    private destinationNode: MediaStreamAudioDestinationNode;
    private aecStream?: MediaStream & Disposable;
    private isDisposed = false;

    /** The destination node to use instead of context.destination */
    public get destination(): MediaStreamAudioDestinationNode { return this.destinationNode; }

    private get audioStream(): MediaStream | null {
        return this.aecStream ?? this.destinationNode?.stream ?? null;
    }

    constructor(context: AudioContext) {
        this.audio = new Audio();
        this.audio.id = 'audio-context-destination';
        this.audio.preload = 'none';
        this.audio.loop = false;
        this.audio.hidden = true;
        this.audio.muted = false;
        this.audio.controls = false;
        document.body.append(this.audio);

        this.destinationNode = context.createMediaStreamDestination();
        this.destinationNode.channelInterpretation = 'speakers';

        resetMediaSessionMetadata();

        if (isWebRtcAecRequired) {
            void createWebRtcAecStream(this.destinationNode.stream)
                .then(x => {
                    if (!this.isDisposed) {
                        this.aecStream = x;
                    } else {
                        x?.dispose();
                    }
                });
        }
    }

    public async onUsed(): Promise<void> {
        await this.play();
    }

    public onUnused(): void {
        this.pause();
    }

    public onClosed(): void {
        this.dispose();
    }

    /** Starts playing through the audio element */
    public async play(): Promise<void> {
        if (this.isDisposed)
            return;

        debugLog?.log('-> play()', this.audio?.paused);
        try {
            this.audio.srcObject = this.audioStream;
            this.audio.muted = false;
            if (this.audio.paused)
                await this.audio.play();
        } catch (e) {
            errorLog?.log('play(): failed to resume:', e);
        }
        debugLog?.log('<- play()', this.audio?.paused);
    }

    /** Pauses the audio element */
    public pause(): void {
        if (this.isDisposed)
            return;

        debugLog?.log('-> pause()', this.audio?.paused);
        try {
            this.audio.muted = true;
            this.audio.pause();
        } catch (e) {
            errorLog?.log('pause(): failed to pause:', e);
        }
        debugLog?.log('<- pause()', this.audio?.paused);
    }

    /** Disposes the audio element and destination node */
    public dispose(): void {
        if (this.isDisposed)
            return;

        this.isDisposed = true;
        debugLog?.log('dispose()');

        this.audio.pause();
        this.audio.srcObject = null;
        this.audio.src = undefined!;
        try {
            document.body.removeChild(this.audio);
        } catch {
            // Element might not be in document
        }

        if (this.destinationNode) {
            this.destinationNode.stream.getAudioTracks().forEach(x => x.stop());
            this.destinationNode.stream.getVideoTracks().forEach(x => x.stop());
            this.destinationNode.disconnect();
            this.destinationNode = undefined!;
        }

        if (this.aecStream) {
            this.aecStream.dispose();
            this.aecStream = undefined;
        }
    }
}
