import { describe, expect, it } from 'vitest';
import { isTerminalStreamError } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/playback/player-worker';

describe('player-worker terminal stream errors', () => {
    it('leaves server-disposed stream identities recoverable for VideoPlayer restart loop', () => {
        expect(isTerminalStreamError(
            new Error('RpcStream not found or disconnected.'),
        )).toBe(false);
        expect(isTerminalStreamError(
            new Error('Stream gap at index 8 (expected 4); reconnect not allowed'),
        )).toBe(false);
        expect(isTerminalStreamError(new Error('Peer disconnected.'))).toBe(false);
        expect(isTerminalStreamError(
            new Error('Player stream stalled: no frames received for 30000ms'),
        )).toBe(false);
    });

    it('leaves decode/render failures recoverable too', () => {
        expect(isTerminalStreamError(new Error('EncodingError: decode failed'))).toBe(false);
    });
});
