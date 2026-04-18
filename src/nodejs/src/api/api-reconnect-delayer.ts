// Api's reconnect delayer — gates peer connection attempts on an app-level
// `allowed` flag (derived from `Api.canConnect = requiresConnection &&
// isDotNetRpcConnected`). When not allowed, `getDelay` returns a promise that
// only resolves on `cancelDelays()`, so the run loop parks without opening
// any WebSockets. When the flag flips to `true`, `Api.setAllowed(true)`
// calls `cancelDelays()` to unblock the loop immediately.
//
// Mirrors .NET `AppRpcClientPeerReconnectDelayer` which returns an infinite
// `RpcClientPeerReconnectDelay` while offline.

import { RpcClientPeerReconnectDelayer } from '../actuallab-rpc/index.js';
import type { RetryDelay } from '../actuallab-core/index.js';

export class ApiReconnectDelayer extends RpcClientPeerReconnectDelayer {
    private _allowed = false;

    get allowed(): boolean {
        return this._allowed;
    }

    /** Toggle whether the peer is allowed to attempt connections. Flipping
     *  to `true` wakes any parked run loop; flipping to `false` does not
     *  tear down an open connection — it only affects the next attempt. */
    setAllowed(value: boolean): void {
        if (this._allowed === value) return;
        this._allowed = value;
        this.cancelDelays(); // wake any currently-waiting run loop
    }

    override getDelay(tryIndex: number, cancellationSignal?: AbortSignal): RetryDelay {
        if (this._allowed)
            return super.getDelay(tryIndex, cancellationSignal);
        return this._parkUntilAllowed(cancellationSignal);
    }

    /** Returns a `RetryDelay` whose promise resolves (not rejects) when
     *  `cancelDelays()` fires — typically because `setAllowed(true)` was
     *  called. External cancellation (via `cancellationSignal`) rejects,
     *  matching the base class's semantics. */
    private _parkUntilAllowed(cancellationSignal?: AbortSignal): RetryDelay {
        // Await a fresh cancellation round. `cancelDelaysChanged` fires every
        // time `cancelDelays()` runs, whether from `setAllowed` or any other
        // caller. Using `whenNext()` registers a one-shot listener.
        const waiter = this.cancelDelaysChanged.whenNext();
        const promise = new Promise<void>((resolve, reject) => {
            let aborted = false;
            const onAbort = (): void => {
                aborted = true;
                reject(new Error('Retry delay aborted.'));
            };
            cancellationSignal?.addEventListener('abort', onAbort, { once: true });
            void waiter.then(() => {
                cancellationSignal?.removeEventListener('abort', onAbort);
                if (!aborted) resolve();
            });
        });
        return { promise, endsAt: 0, isLimitExceeded: false };
    }
}
