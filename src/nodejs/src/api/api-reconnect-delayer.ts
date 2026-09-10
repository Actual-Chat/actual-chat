// Api's reconnect delayer. Two inputs with different weight:
//
// - `isRequired` (= `Api.requiresConnection`) is a hard gate: with no scope
//   holding the connection, `getDelay` parks the peer's run loop until a
//   scope appears, so no WebSocket is opened for nobody.
// - `isOnline` (= `Api.isDotNetRpcConnected`) is a hint. In a worker it is a
//   copy of main-thread state pushed across realms, and a copy can go stale.
//   So it never blocks: while it says offline, every retry waits at least
//   `offlineProbeDelayMs` instead of the usual backoff, and a flip back to
//   online wakes the loop at once. A peer that reaches the server proves the
//   hint wrong on its own.
//
// .NET's `AppRpcClientPeerReconnectDelayer` parks fully while offline; there
// the signal is in-process and authoritative, here it is not.

import { RpcClientPeerReconnectDelayer } from '../actuallab-rpc/index.js';
import type { RetryDelay } from '../actuallab-core/index.js';

const DEFAULT_OFFLINE_PROBE_DELAY_MS = 15_000;

export class ApiReconnectDelayer extends RpcClientPeerReconnectDelayer {
    private _isRequired = false;
    private _isOnline = true;

    offlineProbeDelayMs = DEFAULT_OFFLINE_PROBE_DELAY_MS;

    get isRequired(): boolean {
        return this._isRequired;
    }

    get isOnline(): boolean {
        return this._isOnline;
    }

    setIsRequired(value: boolean): void {
        if (this._isRequired === value)
            return;

        this._isRequired = value;
        if (value)
            this.cancelDelays();
    }

    setIsOnline(value: boolean): void {
        if (this._isOnline === value)
            return;

        this._isOnline = value;
        if (value)
            this.cancelDelays();
    }

    override getDelay(tryIndex: number, cancellationSignal?: AbortSignal): RetryDelay {
        if (!this._isRequired)
            return this._parkUntilRequired(cancellationSignal);

        return super.getDelay(tryIndex, cancellationSignal);
    }

    // Protected/internal methods

    protected override getDelayMs(tryIndex: number): number {
        const delayMs = super.getDelayMs(tryIndex);
        return this._isOnline ? delayMs : Math.max(delayMs, this.offlineProbeDelayMs);
    }

    // Private methods

    // Resolves only once `isRequired` is true: `cancelDelays()` also fires for
    // an online flip, and that alone must not send an unwanted peer connecting.
    private _parkUntilRequired(cancellationSignal?: AbortSignal): RetryDelay {
        const promise = new Promise<void>((resolve, reject) => {
            const onAbort = (): void => reject(new Error('Retry delay aborted.'));
            cancellationSignal?.addEventListener('abort', onAbort, { once: true });
            const waitForNextRound = (): void => {
                if (cancellationSignal?.aborted)
                    return;

                if (this._isRequired) {
                    cancellationSignal?.removeEventListener('abort', onAbort);
                    resolve();
                    return;
                }

                void this.cancelDelaysChanged.whenNext().then(waitForNextRound);
            };
            waitForNextRound();
        });
        return { promise, endsAt: 0, isLimitExceeded: false };
    }
}
