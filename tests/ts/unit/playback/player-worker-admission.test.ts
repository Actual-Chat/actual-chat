import { afterAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { RpcError } from 'actuallab-rpc';

const mocks = vi.hoisted(() => {
    vi.stubGlobal('self', {});
    return {
        hooks: {} as {
            getStream(streamId: string, codecs?: string[]): Promise<AsyncIterable<unknown>>;
            prewarmRpc(url: string): void;
        },
        legacyPull: vi.fn<() => Promise<AsyncIterable<unknown>>>(),
        admissionPull: vi.fn<() => Promise<AsyncIterable<unknown>>>(),
    };
});

vi.mock('api', () => ({
    Api: { init: vi.fn(), requireConnection: vi.fn(), peer: { isConnected: true } },
    streamingApi: {
        liveVideoStreams: { GetStream: mocks.legacyPull, GetStreamWithCapabilities: mocks.admissionPull },
    },
}));
vi.mock('rpc', () => ({ rpcClientServer: () => ({}) }));
vi.mock('../../../../src/dotnet/UI.Blazor.App/Services/Video/playback/player-worker', () => ({
    playerWorkerImpl: {},
    __setPlayerWorkerHooks: (hooks: object) => Object.assign(mocks.hooks, hooks),
}));
vi.mock('../../../../src/dotnet/UI.Blazor.App/Components/AudioRecorder/workers/worker-connectivity-ui',
    () => ({ WorkerConnectivityUI: {} }));

import '../../../../src/dotnet/UI.Blazor.App/Services/Video/playback/player-worker-host';

const stream: AsyncIterable<unknown> = {
    [Symbol.asyncIterator]: async function* () {
        await Promise.resolve();
        yield 1;
    },
};

beforeEach(() => {
    mocks.legacyPull.mockReset().mockResolvedValue(stream);
    mocks.admissionPull.mockReset().mockResolvedValue(stream);
    mocks.hooks.prewarmRpc('https://example.test');
});

afterAll(() => vi.unstubAllGlobals());

describe('player worker capability admission', () => {
    it('should send decoder capabilities with the stream subscription', async () => {
        expect(await mocks.hooks.getStream('stream', ['vp9', 'hevc'])).toBe(stream);

        expect(mocks.admissionPull).toHaveBeenCalledWith('~', 'stream', ['vp9', 'hevc']);
        expect(mocks.legacyPull).not.toHaveBeenCalled();
    });

    it('should retain the legacy call for old worker options', async () => {
        await mocks.hooks.getStream('stream');

        expect(mocks.legacyPull).toHaveBeenCalledWith('~', 'stream');
        expect(mocks.admissionPull).not.toHaveBeenCalled();
    });

    it('should fall back only when the older server lacks the admission endpoint', async () => {
        mocks.admissionPull.mockRejectedValue(new RpcError(
            "Endpoint not found: 'ILiveVideoStreams.GetStreamWithCapabilities'.",
            'ActualLab.Rpc.RpcException',
        ));

        mocks.legacyPull.mockResolvedValue({
            [Symbol.asyncIterator]: async function* () {
                await Promise.resolve();
                yield { Index: 0, KeyFrameIndex: 0, Codec: 'hevc' };

                yield { Index: 1, KeyFrameIndex: 1, Codec: 'vp9' };
            },
        });
        const admitted = await mocks.hooks.getStream('stream', ['vp9']);
        const iterator = admitted[Symbol.asyncIterator]();

        expect(await iterator.next()).toEqual({
            done: false, value: { Index: 1, KeyFrameIndex: 1, Codec: 'vp9' },
        });
        expect((await iterator.next()).done).toBe(true);
        expect(mocks.legacyPull).toHaveBeenCalledWith('~', 'stream');
    });

    it('should not bypass admission after a permissions or transport failure', async () => {
        const error = new RpcError('ReadVideo permission denied', 'System.Security.SecurityException');
        mocks.admissionPull.mockRejectedValue(error);

        await expect(mocks.hooks.getStream('stream', ['vp9'])).rejects.toBe(error);
        expect(mocks.legacyPull).not.toHaveBeenCalled();
    });
});
