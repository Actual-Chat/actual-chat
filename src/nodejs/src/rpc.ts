import { delayAsync, PromiseSourceWithTimeout, ResolvedPromise } from 'promises';
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
        public readonly noWait: boolean = false,
    ) {
        this.timeoutMs = timeoutMs ?? null;
        if (args?.length > 0) {
            const lastArg = args[args.length - 1];
            if (lastArg == rpcNoWait) {
                args.pop();
                this.noWait = true;
            }
            else if (lastArg['type'] && lastArg['type'] === 'rpc-timeout') {
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
        return rpcPromisesInProgress.get(id) as RpcPromise<T> ?? null;
    }

    public unregister(): boolean {
        return rpcPromisesInProgress.delete(this.id);
    }
}

RpcPromise.Void.resolve(undefined);

export function completeRpc(result: RpcResult): void {
    const promise = RpcPromise.get<unknown>(result.id);
    if (promise == null) {
        // eslint-disable-next-line no-debugger
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
    // we don' use those objects yet
    // if (x instanceof ReadableStream)
    //     return true;
    // if (x instanceof WritableStream)
    //     return true;
    // if (x instanceof TransformStream)
    //     return true;
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
    if (!serverImpl)
        throw new Error(`${name}: serverImpl == null!`);

    // eslint-disable-next-line @typescript-eslint/no-unused-vars
    onUnhandledMessage ??= (event: MessageEvent<unknown>): Promise<void> => {
        throw new Error(`${name}: unhandled message.`);
    }

    const onMessage = async (event: MessageEvent<RpcCall>): Promise<void> => {
        const rpcCall = event.data;
        if (!rpcCall?.id) {
            await onUnhandledMessage(event);
            return;
        }
        debugLog?.log(`-> ${name}.onMessage[#${rpcCall.id}]:`, rpcCall)
        let value: unknown = undefined;
        let error: unknown = undefined;
        try {
            // eslint-disable-next-line @typescript-eslint/ban-types
            const method = serverImpl[rpcCall.method] as Function;
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
        if (result['method']) {
            errorLog?.log(`${name}: got an RpcCall message:`, result);
            throw new Error(`${name}: got an RpcCall message.`);
        }
        if (result.id)
            void completeRpc(result);
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

                const transferables = getTransferables(args);
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
    const clientOnMessage = messagePort.onmessage;
    const server = rpcServer(name, messagePort, serverImpl, onUnhandledMessage);
    const serverOnMessage = messagePort.onmessage;

    messagePort.onmessage = async (event: MessageEvent<RpcCall | RpcResult>): Promise<void> => {
        const data = event.data;
        if (data['method'])
            await serverOnMessage.call(messagePort, event);
        else
            await clientOnMessage.call(messagePort, event);
    }
    return client;
}

// This function is used only in tests below
async function whenNextMessage<T>(messagePort: MessagePort | Worker, timeoutMs?: number) : Promise<MessageEvent<T>> {
    const result = new PromiseSourceWithTimeout<MessageEvent<T>>();
    if (timeoutMs)
        result.setTimeout(timeoutMs);

    const oldOnMessage = messagePort.onmessage;
    messagePort.onmessage = (event: MessageEvent<T>) => result.resolve(event);
    try {
        return await result;
    }
    finally {
        messagePort.onmessage = oldOnMessage;
    }
}


// Self-test - we don't want to run it in workers & worklets
const mustRunSelfTest = debugLog != null && globalThis['focus'];
if (mustRunSelfTest) {
    const testLog = errorLog;
    if (!testLog)
        throw new Error('testLog == null');
    void (async () => {
        // Basic test

        let rpcPromise = new RpcPromise<string>();
        testLog.assert(!rpcPromise.isCompleted());
        void completeRpc(RpcResult.value(rpcPromise.id, 'x'));
        testLog.assert(rpcPromise.isCompleted());
        testLog.assert('x' == await rpcPromise);

        rpcPromise = new RpcPromise<string>();
        testLog.assert(!rpcPromise.isCompleted());
        void completeRpc(RpcResult.error(rpcPromise.id, 'Error'));
        testLog.assert(rpcPromise.isCompleted());
        try {
            await rpcPromise;
            testLog?.log('rpcPromise.Error is undefined.');
        }
        catch (error) {
            testLog.assert(error == 'Error', 'error != "Error"');
        }

        // RpcServer & rpcClient test

        interface TestService {
            mul(x: number, y: number): Promise<number>;
            ping(reply: string, port: MessagePort, noWait?: RpcNoWait): Promise<void>;
        }

        class TestServer implements TestService {
            mul(x: number, y: number): Promise<number> {
                if (x === 1 || y === 1)
                    throw '1';
                return Promise.resolve(x * y);
            }

            // eslint-disable-next-line @typescript-eslint/no-unused-vars
            async ping(reply: string, port: MessagePort, noWait?: RpcNoWait): Promise<void> {
                await delayAsync(500);
                port.postMessage(reply);
                return ResolvedPromise.Void;
            }
        }

        const channel = new MessageChannel();
        const client = rpcClient<TestService>(`client`, channel.port1, 300);
        const server = rpcServer(`server`, channel.port2, new TestServer());

        // Normal call
        testLog.assert(await client.mul(3, 4) == 12);

        // Normal call w/ transferable
        const pingChannel = new MessageChannel();
        await client.ping('Pong', pingChannel.port2, rpcNoWait);
        const sideResult = (await whenNextMessage<string>(pingChannel.port1, 1000)).data;
        debugLog?.log('Side channel result:', sideResult);
        testLog.assert(sideResult === 'Pong');

        // Error call
        try {
            await client.mul(1, 5);
            testLog.assert(false);
        }
        catch (e) {
            testLog.assert(e === '1');
        }

        // Post-dispose call
        client.dispose();
        try {
            await client.mul(3, 5);
            testLog.assert(false);
        }
        catch (e) {
            testLog.assert(!!e);
        }
    })();
}
