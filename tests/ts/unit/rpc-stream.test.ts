import { describe, expect, it } from 'vitest';
import { RpcSharedObjectTracker, RpcStream, type RpcPeer } from 'actuallab-rpc';

describe('RpcStream', () => {
    it('disconnects a local stream sender before the first ack', async () => {
        const peer = makePeer();
        const stream = new RpcStream<number>((async function* () {
            await Promise.resolve();
            yield 1;
        })());

        const streamRef = stream.toRef(peer);
        const localId = Number(String(streamRef).split(',')[1]);

        expect(peer.sharedObjects.get(localId)).toBe(stream.sender);

        stream.disconnect();

        await expect(stream.whenSent).resolves.toBeUndefined();
        expect(peer.sharedObjects.get(localId)).toBeUndefined();
    });
});

function makePeer(): RpcPeer {
    return {
        hub: {
            hubId: 'test-hub',
        },
        sharedObjects: new RpcSharedObjectTracker(),
        serializationFormat: {
            isBinary: false,
        },
    } as unknown as RpcPeer;
}
