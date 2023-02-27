/* eslint-disable @typescript-eslint/ban-types */

import { Disposable } from 'disposable';
import {
    BufferState,
    FeederAudioNode,
    FeederAudioWorklet,
    FeederState,
    PlaybackState,
} from './feeder-audio-worklet-contract';
import { ResolvedPromise } from 'promises';
import { rpcClientServer, RpcNoWait } from 'rpc';

import { Log, LogLevel, LogScope } from 'logging';
const LogScope: LogScope = 'FeederNode';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const errorLog = Log.get(LogScope, LogLevel.Error);

/** Part of the feeder that lives in main global scope. It's the counterpart of FeederAudioWorkletProcessor */
export class FeederAudioWorkletNode extends AudioWorkletNode implements FeederAudioNode {
    private readonly feederWorklet: FeederAudioWorklet & Disposable = null;

    public onStateChanged?: (playbackState: PlaybackState, bufferState: BufferState) => void = null;

    private constructor(
        public readonly id: string,
        context: BaseAudioContext,
        name: string,
        options?: AudioWorkletNodeOptions
    ) {
        super(context, name, options);
        this.onprocessorerror = this.onProcessorError;
        this.feederWorklet = rpcClientServer<FeederAudioWorklet>(`${LogScope}.feederWorklet`, this.port, this);
    }

    public static async create(
        id: string,
        decoderWorkerPort: MessagePort,
        context: BaseAudioContext,
        name: string,
        options?: AudioWorkletNodeOptions
    ): Promise<FeederAudioWorkletNode> {
        const node = new FeederAudioWorkletNode(id, context, name, options);
        await node.feederWorklet.init(id, decoderWorkerPort);
        return node;
    }

    public getState(): Promise<FeederState> {
        return this.feederWorklet.getState();
    }

    public stateChanged(playbackState: PlaybackState, bufferState: BufferState, noWait?: RpcNoWait): Promise<void> {
        this.onStateChanged?.(playbackState, bufferState)
        return ResolvedPromise.Void;
    }

    public abort(): Promise<void> {
        return this.feederWorklet.end(true);
    }

    public pause(): Promise<void> {
        return this.feederWorklet.pause();
    }

    public resume(): Promise<void> {
        return this.feederWorklet.resume();
    }

    private onProcessorError = (ev: Event) => {
        errorLog?.log(`#${this.id}.onProcessorError: unhandled error:`, ev);
    };
}
