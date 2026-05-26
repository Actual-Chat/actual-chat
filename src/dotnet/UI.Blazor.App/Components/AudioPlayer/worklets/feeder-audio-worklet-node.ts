import { Disposable } from 'disposable';
import {
    FeederAudioWorkletEventHandler,
    FeederAudioWorklet,
    FeederState,
} from './feeder-audio-worklet-contract';
import { ResolvedPromise } from 'actuallab-core';
import { rpcClientServer, RpcNoWait } from 'rpc';
import { Log, getLogs } from 'logging';
import { AC, whenAppConstantsReady } from 'app-constants';
import { SharedSettings } from 'shared-settings';
import { SharedSettingsWorkerSync } from 'shared-settings-worker';

const { logScope, errorLog } = getLogs('FeederNode');

/** Part of the feeder that lives in main global scope. It's the counterpart of FeederAudioWorkletProcessor */
export class FeederAudioWorkletNode extends AudioWorkletNode {
    private readonly worklet: FeederAudioWorklet & Disposable;
    private sharedSettingsRegistration: Disposable | null = null;

    public onStateChanged?: (state: FeederState) => void;

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
        this.worklet = rpcClientServer<FeederAudioWorklet>(`${logScope}.feederWorklet`, this.port, server);
    }

    public static async create(
        id: string,
        decoderWorkerPort: MessagePort,
        context: BaseAudioContext,
        name: string,
        options?: AudioWorkletNodeOptions
    ): Promise<FeederAudioWorkletNode> {
        const node = new FeederAudioWorkletNode(id, context, name, options);
        await whenAppConstantsReady;
        await node.worklet.init(AC, SharedSettings.all, id, decoderWorkerPort);
        node.sharedSettingsRegistration = SharedSettingsWorkerSync.register(node.worklet);
        return node;
    }

    public pause(noWait?: RpcNoWait): Promise<void> {
        return this.worklet.pause(noWait);
    }

    public resume(preSkip: number): Promise<void> {
        return this.worklet.resume(preSkip);
    }

    public dispose(): void {
        this.sharedSettingsRegistration?.dispose();
        this.sharedSettingsRegistration = null;
        this.worklet.dispose();
    }

    private onProcessorError = (ev: Event) => {
        errorLog?.log(`#${this.id}.onProcessorError: unhandled error, context.state=${this.context.state}:`, Log.ref(ev));
    };
}
