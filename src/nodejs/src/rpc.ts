import { PromiseSource } from 'promises';

const LogScope = 'Rpc';
const debug = true;
const selfTest = false;

export interface RpcCallMessage {
    rpcResultId: number;
    method: string,
    arguments: unknown[],
}

export function rpcCallMessage(rpcResult: RpcResult<unknown> | number, method: string, ...args: unknown[]) : RpcCallMessage {
    const rpcResultId = typeof rpcResult === 'number' ? rpcResult : rpcResult.id;
    return {
        rpcResultId: rpcResultId,
        method: method,
        arguments: args,
    }
}

export interface Result<T> {
    value?: T;
    error?: unknown;
}

export interface RpcResultMessage extends Result<unknown> {
    rpcResultId: number;
}

export function rpcResultMessage(rpcResultId: number, value: unknown, error?: unknown = undefined) : RpcResultMessage {
    return {
        rpcResultId: rpcResultId,
        value: value,
    }
}

export function rpcErrorResultMessage(rpcResultId: number, error: unknown) : RpcResultMessage {
    return {
        rpcResultId: rpcResultId,
        value: undefined,
        error: error,
    }
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
            if (debug)
                console.debug(`${LogScope}.RpcResult.resolve[#${this.id}] =`, value)
            this.unregister();
            oldResolve(value);
        };
        this.reject = (reason: unknown) => {
            if (debug)
                console.debug(`${LogScope}.RpcResult.reject[#${this.id}] =`, reason)
            this.unregister();
            oldReject(reason);
        };
        this.id = id ?? ++lastResultId;
        results.set(this.id, this);
        if (debug)
            console.debug(`${LogScope}.RpcResult.ctor[#${this.id}]`)
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

export async function handleRpcCall(
    rpcCallMessage: RpcCallMessage,
    sender: (message: RpcResultMessage) => Promise<void> | void,
    target: object,
    errorHandler?: (error: unknown) => void
) : Promise<unknown> {
    if (debug)
        console.debug(`${LogScope}.handleRpcCall:`, rpcCallMessage)
    return handleRpcCustom<unknown>(rpcCallMessage.rpcResultId, sender, async () => {
        // eslint-disable-next-line @typescript-eslint/ban-types
        const method = target[rpcCallMessage.method] as Function;
        const result = await method.apply(sender, rpcCallMessage.arguments) as unknown;
        return result;
    }, errorHandler);
}

export async function handleRpcCustom<T>(
    rpcResultId: number | null,
    sender: (message: RpcResultMessage) => Promise<void> | void,
    handler: () => Promise<T>,
    errorHandler?: (error: unknown) => void
) : Promise<T> {
    let value: T | undefined = undefined;
    let error: unknown = undefined;
    try {
        value = await handler();
    }
    catch (e) {
        error = e;
    }
    const message = rpcResultMessage(rpcResultId, value, error);
    if (debug)
        console.debug(`${LogScope}.handleRpc[#${rpcResultId}] =`, message)
    await sender(message);
    if (error !== undefined && errorHandler != null)
        errorHandler(error);
    return value;
}

export function completeRpc(message: RpcResultMessage) : RpcResult<unknown> | null {
    const { rpcResultId, value, error } = message;
    const rpcResult = RpcResult.get<unknown>(rpcResultId);
    if (rpcResult == null) {
        console.warn(`${LogScope}.completeRpc: RpcResult #${rpcResultId} is not found`);
        return null;
    }
    try {
        if (error !== undefined)
            rpcResult.reject(error);
        else
            rpcResult.resolve(value);
    }
    catch (error) {
        rpcResult.reject(error);
    }
    return rpcResult;
}

if (selfTest) {
    void (async () => {
        let rpcResult = rpc<string>(() => undefined);
        console.assert(!rpcResult.isResolved());
        void completeRpc(rpcResultMessage(rpcResult.id, 'x'));
        console.assert(rpcResult.isResolved());
        console.assert('x' == await rpcResult);

        rpcResult = rpc<string>(() => undefined);
        console.assert(!rpcResult.isResolved());
        void completeRpc(rpcErrorResultMessage(rpcResult.id, 'Error'));
        console.assert(rpcResult.isResolved());
        try {
            await rpcResult;
        }
        catch (error) {
            console.assert('Error' == error);
        }
    })();
}
