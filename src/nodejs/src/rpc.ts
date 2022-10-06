import { Serialized } from 'serialized';
import { PromiseSource } from 'promises';

const LogScope = 'Rpc';

export interface RpcResultMessage {
    rpcResultId: number;
    result: Serialized;
}

const results = new Map<number, RpcResult<unknown>>();
let lastResultId = 0;

export class RpcResult<T> extends PromiseSource<T> {
    public readonly id: number;

    constructor(id?: number) {
        super();
        const oldResolve = this.resolve;
        const oldReject = this.reject;
        this.resolve = (value: T) => {
            this.unregister();
            oldResolve(value);
        };
        this.reject = (reason: unknown) => {
            this.unregister();
            oldReject(reason);
        };
        this.id = id ?? ++lastResultId;
        results.set(this.id, this);
    }

    public static get<T>(id: number) : RpcResult<T> | null {
        return results.get(id) as RpcResult<T> ?? null;
    }

    public unregister() : boolean {
        return results.delete(this.id);
    }
}

export function rpc<T>(sender: (rpcResult: RpcResult<T>) => unknown, timeout?: number) : RpcResult<T> {
    const rpcResult = new RpcResult<T>();
    rpcResult.setTimeout(timeout);
    try {
        const sendResult = sender(rpcResult);
        Promise.resolve(sendResult).catch(error => rpcResult.reject(error));
    }
    catch (error) {
        rpcResult.reject(error);
    }
    return rpcResult;
}

export function completeRpc(message: RpcResultMessage) : RpcResult<unknown> | null {
    const { rpcResultId, result } = message;
    const rpcResult = RpcResult.get<unknown>(rpcResultId);
    if (rpcResult == null) {
        console.warn(`${LogScope}.completeRpc: RpcResult #${rpcResultId} is not found`);
        return null;
    }
    try {
        if (result.errorJson != null)
            rpcResult.reject(JSON.parse(result.errorJson));
        else {
            const value: unknown = result.valueJson === undefined ? undefined : JSON.parse(result.valueJson);
            rpcResult.resolve(value);
        }
    }
    catch (error) {
        rpcResult.reject(error);
    }
    return rpcResult;
}
