import { describe, expect, it } from 'vitest';
import { RpcStreamSender } from '../../../src/nodejs/src/actuallab-rpc/rpc-stream-sender';
import type { RpcPeer } from '../../../src/nodejs/src/actuallab-rpc/rpc-peer';

// A reconnecting peer has `connection` set from the moment the socket opens,
// but the server accepts nothing but $sys.Handshake until the handshake
// completes: an item written in that window kills the connection
// ("Handshake failed: expected $sys.Handshake, got $sys.I").

function createSender(isConnected: boolean) {
    const sent: string[] = [];
    const connection = {};
    const peerStub = {
        hub: {
            hubId: 'test-host',
            systemCallSender: {
                item: () => { sent.push('item'); },
                batch: () => { sent.push('batch'); },
                end: () => { sent.push('end'); },
                disconnect: () => { sent.push('disconnect'); },
            },
        },
        sharedObjects: {
            nextId: () => 1,
            unregister: () => undefined,
        },
        connection,
        isConnected,
        wireConnection: isConnected ? connection : undefined,
        serializationFormat: null,
    };
    const sender = new RpcStreamSender<number>(
        peerStub as unknown as RpcPeer, 4, 8, true, true, () => true, false, 16);
    return { sender, sent };
}

describe('RpcStreamSender handshake gate', () => {
    it('writes nothing to the wire while the peer is connected but not yet handshaken', () => {
        const { sender, sent } = createSender(false);
        sender.sendItem(1);
        sender.sendBatch([2, 3]);
        sender.sendEnd();
        expect(sent).toEqual([]);
    });

    it('writes items, batches and the end marker once the handshake is done', () => {
        const { sender, sent } = createSender(true);
        sender.sendItem(1);
        sender.sendBatch([2, 3]);
        sender.sendEnd();
        expect(sent).toEqual(['item', 'batch', 'end']);
    });

    it('keeps advancing the index for held items so the replay buffer resends them after the reset ack', () => {
        const { sender } = createSender(false);
        sender.sendItem(1);
        sender.sendItem(2);
        expect(sender.nextIndex).toBe(2);
    });
});
