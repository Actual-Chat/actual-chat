import { describe, it, expect } from 'vitest';
import {
    RpcCall,
    RpcResult,
    RpcPromise,
    rpcServer,
    rpcClient,
    rpcClientServer,
    completeRpc,
    rpcNoWait,
    type RpcNoWait,
    type RpcTimeout,
} from 'rpc';

// --- RpcCall ---

describe('RpcCall', () => {
    it('should store id, method, and args', () => {
        const call = new RpcCall(1, 'foo', [10, 20]);
        expect(call.id).toBe(1);
        expect(call.method).toBe('foo');
        expect(call.args).toEqual([10, 20]);
        expect(call.noWait).toBe(false);
        expect(call.timeoutMs).toBeUndefined();
    });

    it('should detect rpcNoWait sentinel and remove it from args', () => {
        const call = new RpcCall(2, 'bar', ['a', rpcNoWait]);
        expect(call.noWait).toBe(true);
        expect(call.args).toEqual(['a']);
    });

    it('should detect RpcTimeout sentinel and remove it from args', () => {
        const timeout: RpcTimeout = { type: 'rpc-timeout', timeoutMs: 3000 };
        const call = new RpcCall(3, 'baz', [1, 2, timeout]);
        expect(call.timeoutMs).toBe(3000);
        expect(call.args).toEqual([1, 2]);
    });

    it('should prefer constructor timeoutMs over sentinel when sentinel absent', () => {
        const call = new RpcCall(4, 'qux', [1], 500);
        expect(call.timeoutMs).toBe(500);
    });
});

// --- RpcResult ---

describe('RpcResult', () => {
    it('should create value result', () => {
        const r = RpcResult.value(1, 42);
        expect(r.id).toBe(1);
        expect(r.value).toBe(42);
        expect(r.error).toBeUndefined();
    });

    it('should create error result', () => {
        const r = RpcResult.error(2, 'fail');
        expect(r.id).toBe(2);
        expect(r.value).toBeUndefined();
        expect(r.error).toBe('fail');
    });
});

// --- RpcPromise & completeRpc ---

describe('RpcPromise', () => {
    it('should resolve via completeRpc with value', async () => {
        const p = new RpcPromise<string>();
        expect(p.isCompleted).toBe(false);
        completeRpc(RpcResult.value(p.id, 'hello'));
        expect(p.isCompleted).toBe(true);
        await expect(p).resolves.toBe('hello');
    });

    it('should reject via completeRpc with error', async () => {
        const p = new RpcPromise<string>();
        completeRpc(RpcResult.error(p.id, 'oops'));
        expect(p.isCompleted).toBe(true);
        await expect(p).rejects.toThrow('oops');
    });

    it('should be retrievable by id', () => {
        const p = new RpcPromise<number>();
        expect(RpcPromise.get<number>(p.id)).toBe(p);
        p.resolve(0);
    });

    it('should unregister after resolve', () => {
        const p = new RpcPromise<number>();
        const id = p.id;
        p.resolve(1);
        expect(RpcPromise.get<number>(id)).toBeUndefined();
    });

    it('completeRpc should warn for unknown id', () => {
        // Should not throw
        completeRpc(RpcResult.value(-999, 'orphan'));
    });
});

// --- rpcServer & rpcClient over MessageChannel ---

interface TestService {
    mul(x: number, y: number): Promise<number>;
    concat(a: string, b: string): Promise<string>;
    fail(): Promise<void>;
    ping(reply: string, noWait?: RpcNoWait): Promise<void>;
}

class TestServer implements TestService {
    mul(x: number, y: number): Promise<number> {
        return Promise.resolve(x * y);
    }
    concat(a: string, b: string): Promise<string> {
        return Promise.resolve(a + b);
    }
    fail(): Promise<void> {
        throw new Error('intentional');
    }
    async ping(_reply: string, _noWait?: RpcNoWait): Promise<void> {
        // No-op for fire-and-forget test
    }
}

describe('rpcServer + rpcClient', () => {
    it('should handle normal call', async () => {
        const { port1, port2 } = new MessageChannel();
        const client = rpcClient<TestService>('test-client', port1);
        const server = rpcServer('test-server', port2, new TestServer());

        const result = await client.mul(3, 4);
        expect(result).toBe(12);

        client.dispose();
        server.dispose();
    });

    it('should handle string concatenation', async () => {
        const { port1, port2 } = new MessageChannel();
        const client = rpcClient<TestService>('test-client', port1);
        const server = rpcServer('test-server', port2, new TestServer());

        const result = await client.concat('hello', ' world');
        expect(result).toBe('hello world');

        client.dispose();
        server.dispose();
    });

    it('should propagate server errors to client', async () => {
        const { port1, port2 } = new MessageChannel();
        const client = rpcClient<TestService>('test-client', port1);
        const server = rpcServer('test-server', port2, new TestServer());

        await expect(client.fail()).rejects.toBeDefined();

        client.dispose();
        server.dispose();
    });

    it('should throw on call after dispose', () => {
        const { port1, port2 } = new MessageChannel();
        const client = rpcClient<TestService>('test-client', port1);
        const server = rpcServer('test-server', port2, new TestServer());
        client.dispose();

        expect(() => client.mul(1, 2)).toThrow('disposed');
        server.dispose();
    });
});

// --- rpcClientServer ---

describe('rpcClientServer', () => {
    it('should handle bidirectional RPC', async () => {
        interface SideA {
            add(a: number, b: number): Promise<number>;
        }
        interface SideB {
            greet(name: string): Promise<string>;
        }

        class SideAImpl implements SideA {
            add(a: number, b: number): Promise<number> {
                return Promise.resolve(a + b);
            }
        }
        class SideBImpl implements SideB {
            greet(name: string): Promise<string> {
                return Promise.resolve(`Hello, ${name}!`);
            }
        }

        const { port1, port2 } = new MessageChannel();
        const clientA = rpcClientServer<SideB>('sideA', port1, new SideAImpl());
        const clientB = rpcClientServer<SideA>('sideB', port2, new SideBImpl());

        // A calls B
        const greeting = await clientA.greet('World');
        expect(greeting).toBe('Hello, World!');

        // B calls A
        const sum = await clientB.add(10, 20);
        expect(sum).toBe(30);

        clientA.dispose();
        clientB.dispose();
    });
});
