import { describe, expect, it } from 'vitest';
import { isTerminalStreamError } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/playback/player-worker';

describe('player-worker terminal stream errors', () => {
    it('classifies server-disposed stream identities as ended', () => {
        expect(isTerminalStreamError(
            new Error('RpcStream not found or disconnected.'),
        )).toBe(true);
        expect(isTerminalStreamError(
            new Error('Stream gap at index 8 (expected 4); reconnect not allowed'),
        )).toBe(true);
        expect(isTerminalStreamError(new Error('Peer disconnected.'))).toBe(true);
        expect(isTerminalStreamError(
            new Error('Player stream stalled: no frames received for 30000ms'),
        )).toBe(true);
    });

    it('leaves decode/render failures on the error path', () => {
        expect(isTerminalStreamError(new Error('EncodingError: decode failed'))).toBe(false);
    });
});
