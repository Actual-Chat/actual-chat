/* eslint-disable @typescript-eslint/ban-types */

import { Disposable } from 'disposable';
import {
    FeederAudioWorkletEventHandler,
    FeederAudioWorklet,
    FeederState,
} from './feeder-audio-worklet-contract';
import { ResolvedPromise } from 'promises';
import { rpcClientServer, RpcNoWait } from 'rpc';

import { Log, LogLevel, LogScope } from 'logging';
const LogScope: LogScope = 'FeederNode';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const errorLog = Log.get(LogScope, LogLevel.Error);

/** Part of the feeder that lives in main global scope. It's the counterpart of FeederAudioWorkletProcessor */
export class FeederAudioWorkletNode extends AudioWorkletNode {
    private readonly worklet: FeederAudioWorklet & Disposable = null;

    public onStateChanged?: (state: FeederState) => void = null;

    private constructor(
        public readonly id: string,
        context: BaseAudioContext,
        name: string,
        options?: AudioWorkletNodeOptions
    ) {
        super(context, name, options);
        this.onprocessorerror = this.onProcessorError;
        const server: FeederAudioWorkletEventHandler = {
            onStateChanged: async (state: FeederState, _noWait?: RpcNoWait): Promise<void> => {
                this.onStateChanged?.(state)
                return ResolvedPromise.Void;
            }
        }
        this.worklet = rpcClientServer<FeederAudioWorklet>(`${LogScope}.feederWorklet`, this.port, server);
    }

    public static async create(
        id: string,
        decoderWorkerPort: MessagePort,
        context: BaseAudioContext,
        name: string,
        options?: AudioWorkletNodeOptions
    ): Promise<FeederAudioWorkletNode> {
        const node = new FeederAudioWorkletNode(id, context, name, options);
        await node.worklet.init(id, decoderWorkerPort);
        return node;
    }

    public pause(noWait?: RpcNoWait): Promise<void> {
        return this.worklet.pause(noWait);
    }

    public resume(noWait?: RpcNoWait): Promise<void> {
        return this.worklet.resume(noWait);
    }

    public end(mustAbort: boolean, noWait?: RpcNoWait): Promise<void> {
        return this.worklet.end(mustAbort, noWait);
    }

    private onProcessorError = (ev: Event) => {
        errorLog?.log(`#${this.id}.onProcessorError: unhandled error:`, Log.ref(ev));
    };
}
