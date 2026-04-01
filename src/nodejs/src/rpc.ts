/* eslint-disable @typescript-eslint/no-unnecessary-type-parameters */
// TODO(AY): review eslint suppressions
import { PromiseSourceWithTimeout } from 'promises';
import { Disposable } from 'disposable';
import { Log } from 'logging';

const { debugLog, warnLog, errorLog } = Log.get('Rpc');

export type RpcNoWait = symbol;
export const rpcNoWait : RpcNoWait = Symbol('RpcNoWait');

export interface RpcTimeout {
    type: 'rpc-timeout',
    timeoutMs: number;
}

export class RpcCall {
    public readonly timeoutMs?: number;

    constructor(
        public id: number,
        public readonly method: string,
        public readonly args: unknown[],
        timeoutMs?: number,
        public readonly noWait = false,
    ) {
        this.timeoutMs = timeoutMs;
        if (args.length > 0) {
            const lastArg = args[args.length - 1];
            if (lastArg == rpcNoWait) {
                args.pop();
                this.noWait = true;
            }
            else if (typeof lastArg === 'object' && lastArg !== null && 'type' in lastArg && lastArg.type === 'rpc-timeout') {
                args.pop();
                const rpcTimeout = lastArg as RpcTimeout;
                this.timeoutMs = rpcTimeout.timeoutMs;
            }
        }
    }
}

export class RpcResult {
    public static value(id: number, value: unknown): RpcResult {
        return new RpcResult(id, value, undefined);
    }

    public static error(id: number, error: unknown): RpcResult {
        return new RpcResult(id, undefined, error);
    }

    constructor(
        public readonly id: number,
        public readonly value: unknown,
        public readonly error: unknown,
    ) { }
}

let nextRpcPromiseId = 1;
const rpcPromisesInProgress = new Map<number, RpcPromise<unknown>>();

export class RpcPromise<T> extends PromiseSourceWithTimeout<T> {
    public readonly id: number;
    public static Void = new RpcPromise<void>('Void' as unknown as number);

    constructor(id?: number) {
        super();
        this.id = id ?? nextRpcPromiseId++;
        const oldResolve = this.resolve;
        const oldReject = this.reject;
        this.resolve = (value: T) => {
            debugLog?.log(`RpcPromise.resolve[#${this.id}] =`, value)
            this.unregister();
            oldResolve(value);
        };
        this.reject = (reason: unknown) => {
            debugLog?.log(`RpcPromise.reject[#${this.id}] =`, reason)
            this.unregister();
            oldReject(reason);
        };
        rpcPromisesInProgress.set(this.id, this);
        // debugLog?.log(`RpcPromise.ctor[#${this.id}]`);
    }

    public static get<T>(id: number): RpcPromise<T> | null {
        return rpcPromisesInProgress.get(id) as RpcPromise<T>;
    }

    public unregister(): boolean {
        return rpcPromisesInProgress.delete(this.id);
    }
}

RpcPromise.Void.resolve(undefined);

export function completeRpc(result: RpcResult): void {
    const promise = RpcPromise.get<unknown>(result.id);
    if (promise == null) {

        warnLog?.log(`completeRpc: RpcPromise #${result.id} is not found`);
        return;
    }
    try {
        if (result.error !== undefined)
            promise.reject(result.error);
        else
            promise.resolve(result.value);
    }
    catch (error) {
        promise.reject(error);
    }
}

export function isTransferable(x: unknown): x is Transferable {
    if (x instanceof ArrayBuffer)
        return true;
    if (x instanceof MessagePort)
        return true;
    // VideoFrame can be transferred
    if (typeof VideoFrame !== 'undefined' && x instanceof VideoFrame)
        return true;
    // OffscreenCanvas can be transferred
    if (typeof OffscreenCanvas !== 'undefined' && x instanceof OffscreenCanvas)
        return true;
    // ImageBitmap can be transferred
    if (typeof ImageBitmap !== 'undefined' && x instanceof ImageBitmap)
        return true;
    // WebCodecs types can be transferred
    if (typeof EncodedVideoChunk !== 'undefined' && x instanceof EncodedVideoChunk)
        return true;
    if (typeof EncodedAudioChunk !== 'undefined' && x instanceof EncodedAudioChunk)
        return true;
    if (typeof AudioData !== 'undefined' && x instanceof AudioData)
        return true;
    // MediaStreamTrack can be transferred (Safari 18+)
    if (typeof MediaStreamTrack !== 'undefined' && x instanceof MediaStreamTrack)
        return true;
    if (x instanceof ReadableStream)
        return true;
    if (x instanceof WritableStream)
        return true;
    if (x instanceof TransformStream)
        return true;
    return false;
}

function getTransferables(args: unknown[]): Transferable[] | undefined {
    let result: Transferable[] | undefined = undefined;
    for (let i = args.length - 1; i >= 0; i--) {
        const value = args[i];
        if (!isTransferable(value)) {
            if (result !== undefined)
                // transferable parameters should be placed one after another
                break;
            continue;
        }

        if (!result)
            result = new Array<Transferable>(value);
        else
            result.push(value);
    }
    return result;
}

export function rpcServer(
    name: string,
    messagePort: MessagePort | Worker,
    serverImpl: object,
    onUnhandledMessage?: (event: MessageEvent<unknown>) => Promise<void>,
    onDispose?: () => void,
) : Disposable {
    // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
    if (!serverImpl)
        throw new Error(`${name}: serverImpl == null!`);

    // eslint-disable-next-line @typescript-eslint/no-unused-vars
    onUnhandledMessage ??= (event: MessageEvent<unknown>): Promise<void> => {
        throw new Error(`${name}: unhandled message.`);
    }

    const onMessage = async (event: MessageEvent<RpcCall>): Promise<void> => {
        const rpcCall = event.data;
        if (!rpcCall.id) {
            await onUnhandledMessage(event);
            return;
        }
        debugLog?.log(`-> ${name}.onMessage[#${rpcCall.id}]:`, rpcCall)
        let value: unknown = undefined;
        let error: unknown = undefined;
        try {
            // eslint-disable-next-line @typescript-eslint/no-unsafe-function-type
            const method = serverImpl[rpcCall.method] as Function | null;
            if (!method) {
                await onUnhandledMessage(event);
                return;
            }
            value = await method.apply(serverImpl, rpcCall.args);
        }
        catch (e) {
            error = e;
        }
        const result = new RpcResult(rpcCall.id, value, error);
        debugLog?.log(`<- ${name}.onMessage[#${rpcCall.id}]:`, result)
        if (!rpcCall.noWait)
            messagePort.postMessage(result);
    }

    // eslint-disable-next-line @typescript-eslint/no-unused-vars
    const onMessageError = (event: MessageEvent): Promise<void> => {
        throw new Error(`${name}: couldn't deserialize the message.`);
    }

    let isDisposed = false;
    const oldOnMessage = messagePort.onmessage;
    const oldOnMessageError = messagePort.onmessageerror;
    messagePort.onmessage = onMessage;
    messagePort.onmessageerror = onMessageError;

    return {
        dispose() {
            if (!isDisposed) {
                isDisposed = true;
                messagePort.onmessage = oldOnMessage;
                messagePort.onmessageerror = oldOnMessageError;
                if (onDispose)
                    onDispose();
            }
        }
    }
}

const DefaultRpcClientTimeoutMs = 5_000;

export function rpcClient<TService extends object>(
    name: string,
    messagePort: MessagePort | Worker,
    timeoutMs = DefaultRpcClientTimeoutMs,
    onDispose?: () => void,
) : TService & Disposable {
    const onMessage = (event: MessageEvent<RpcResult>): void => {
        if (isDisposed)
            return;

        const result = event.data;
        // @ts-expect-error - sanity check
        if (result.method) {
            errorLog?.log(`${name}: got an RpcCall message:`, result);
            throw new Error(`${name}: got an RpcCall message.`);
        }
        if (result.id)
            completeRpc(result);
    }

    const onMessageError = (event: MessageEvent<RpcResult>): void => {
        if (isDisposed)
            return;

        errorLog?.log(`${name}.onMessageError:`, event);
    }

    const proxyMethodCache = new Map<string, ((...args: unknown[]) => RpcPromise<unknown>)>();

    function getProxyMethod(method: string): ((...args: unknown[]) => RpcPromise<unknown>) {
        let result = proxyMethodCache.get(method);
        if (!result) {
            result = (...args: unknown[]): RpcPromise<unknown> => {
                if (isDisposed)
                    throw new Error(`${name}.call: already disposed.`);

                const rpcCall = new RpcCall(nextRpcPromiseId++, method, args, timeoutMs);
                const rpcPromise = rpcCall.noWait ? RpcPromise.Void : new RpcPromise<unknown>(rpcCall.id);
                if (rpcCall.timeoutMs && !rpcCall.noWait)
                    rpcPromise.setTimeout(rpcCall.timeoutMs);

                const transferables = getTransferables(args)!;
                debugLog?.log(`${name}.call:`, rpcCall, ', transfer:', transferables);
                messagePort.postMessage(rpcCall, transferables);
                return rpcPromise;
            }
            proxyMethodCache.set(method, result);
        }

        return result;
    }

    const proxyTarget: Disposable = {
        dispose(): void {
            if (!isDisposed) {
                isDisposed = true;
                messagePort.onmessage = oldOnMessage;
                messagePort.onmessageerror = oldOnMessageError;
                if (onDispose)
                    onDispose();
            }
        }
    }
    const proxy = new Proxy<TService & Disposable>(proxyTarget as (TService & Disposable), {
        // eslint-disable-next-line @typescript-eslint/no-unused-vars
        get(target: TService, p: string | symbol, receiver: unknown): unknown {
            const ownValue = target[p] as unknown;
            if (ownValue || typeof(p) !== 'string')
                return ownValue;
            return getProxyMethod(p);
        }
    })

    let isDisposed = false;
    const oldOnMessage = messagePort.onmessage;
    const oldOnMessageError = messagePort.onmessageerror;
    messagePort.onmessage = onMessage;
    messagePort.onmessageerror = onMessageError;

    return proxy;
}

export function rpcClientServer<TClient extends object>(
    name: string,
    messagePort: MessagePort | Worker,
    serverImpl: object,
    timeoutMs?: number,
    onUnhandledMessage?: (event: MessageEvent<unknown>) => Promise<void>,
) : TClient & Disposable {
    // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
    if (!serverImpl)
        throw new Error(`${name}: serverImpl == null!`);

    const oldOnMessage = messagePort.onmessage;
    const oldOnMessageError = messagePort.onmessageerror;

    const onDispose = () => {
        server.dispose();
        messagePort.onmessage = oldOnMessage;
        messagePort.onmessageerror = oldOnMessageError;
    }

    const client = rpcClient<TClient>(name, messagePort, timeoutMs, onDispose);
    const clientOnMessage = messagePort.onmessage; // rpcClient(...) call sets it
    const server = rpcServer(name, messagePort, serverImpl, onUnhandledMessage);
    const serverOnMessage = messagePort.onmessage; // rpcServer(...) call sets it

    messagePort.onmessage = async (event: MessageEvent<RpcCall | RpcResult>): Promise<void> => {
        const data = event.data;
        if ('method' in data) // RpcCall message, we process it via serverOnMessage
            await serverOnMessage?.call(messagePort, event);
        else // RpcResult message, we process it via clientOnMessage
            await clientOnMessage?.call(messagePort, event);
    }
    return client;
}

