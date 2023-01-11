import { PromiseSource } from 'promises';
import { Log, LogLevel } from 'logging';

const LogScope = 'Rpc';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);
const mustRunSelfTest = debugLog != null;

export interface RpcCallMessage {
    rpcResultId: number;
    method: string,
    arguments: unknown[],
}

export function rpcCallMessage(
    rpcResult: RpcResult<unknown> | number,
    method: string, ...args: unknown[]
): RpcCallMessage {
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

export function rpcResultMessage(rpcResultId: number, value: unknown, error?: unknown): RpcResultMessage {
    if (error !== undefined)
        return rpcErrorResultMessage(rpcResultId, error);
    return {
        rpcResultId: rpcResultId,
        value: value,
    }
}

export function rpcErrorResultMessage(rpcResultId: number, error: unknown): RpcResultMessage {
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
            debugLog?.log(`RpcResult.resolve[#${this.id}] =`, value)
            this.unregister();
            oldResolve(value);
        };
        this.reject = (reason: unknown) => {
            debugLog?.log(`RpcResult.reject[#${this.id}] =`, reason)
            this.unregister();
            oldReject(reason);
        };
        this.id = id ?? ++lastResultId;
        results.set(this.id, this);
        debugLog?.log(`RpcResult.ctor[#${this.id}]`)
    }

    public static get<T>(id: number): RpcResult<T> | null {
        return results.get(id) as RpcResult<T> ?? null;
    }

    public unregister(): boolean {
        return results.delete(this.id);
    }
}

export function rpc<T>(sender: (rpcResult: RpcResult<T>) => unknown, timeout?: number): RpcResult<T> {
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
): Promise<unknown> {
    debugLog?.log(`handleRpcCall:`, rpcCallMessage)
    return handleRpc<unknown>(rpcCallMessage.rpcResultId, sender, async () => {
        // eslint-disable-next-line @typescript-eslint/ban-types
        const method = target[rpcCallMessage.method] as Function;
        return await method.apply(sender, rpcCallMessage.arguments) as unknown;
    }, errorHandler);
}

export async function handleRpc<T>(
    rpcResultId: number | null,
    resultCallback: (message: RpcResultMessage) => Promise<void> | void,
    handler: () => Promise<T>,
    errorHandler?: (error: unknown) => void
): Promise<T> {
    let value: T | undefined = undefined;
    let error: unknown = undefined;
    try {
        value = await handler();
    }
    catch (e) {
        error = e;
    }
    const message = rpcResultMessage(rpcResultId, value, error);
    debugLog?.log(`handleRpc[#${rpcResultId}] =`, message)
    await resultCallback(message);
    if (error !== undefined && errorHandler != null)
        errorHandler(error);
    return value;
}

export function completeRpc(message: RpcResultMessage): RpcResult<unknown> | null {
    const { rpcResultId, value, error } = message;
    const rpcResult = RpcResult.get<unknown>(rpcResultId);
    if (rpcResult == null) {
        warnLog?.log(`completeRpc: RpcResult #${rpcResultId} is not found`);
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

if (mustRunSelfTest) {
    const testLog = errorLog;
    void (async () => {
        let rpcResult = rpc<string>(() => undefined);
        testLog?.assert(!rpcResult.isCompleted());
        void completeRpc(rpcResultMessage(rpcResult.id, 'x'));
        testLog?.assert(rpcResult.isCompleted());
        testLog?.assert('x' == await rpcResult);

        rpcResult = rpc<string>(() => undefined);
        testLog?.assert(!rpcResult.isCompleted());
        void completeRpc(rpcResultMessage(rpcResult.id, null, 'Error'));
        testLog?.assert(rpcResult.isCompleted());
        try {
            await rpcResult;
            testLog?.log('rpcResult.Error is undefined.');
        }
        catch (error) {
            testLog.assert(error == 'Error', 'error != "Error"');
        }
    })();
}
