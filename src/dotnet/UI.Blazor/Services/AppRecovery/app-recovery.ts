import { ConnectivityUI } from '../ConnectivityUI/connectivity-ui';
import { EventHandlerSet } from 'event-handling';
import { PromiseSource } from 'actuallab-core';
import { getLogs } from 'logging';
import { RenderSync } from 'render-sync';

const { infoLog, warnLog, errorLog } = getLogs('AppRecovery');

// Blazor's WebView IPC wire format is `__bwv:["RenderBatch",<id>,"<base64>"]`. Matching the prefix and
// slicing the id out beats JSON.parse, which would decode the whole batch payload on every render.
const RenderBatchPrefix = '__bwv:["RenderBatch",';

interface OpusMediaRecorderHost {
    opusMediaRecorder?: { stop: () => Promise<void> };
}

// Everything that gets the app out of a state it can't leave on its own: Blazor's reconnect and
// fatal-error overlays, and - on MAUI - a gap in the render batch sequence. All three end in
// startReloading(), which is idempotent: the first caller wins and the rest are no-ops.
export class AppRecovery {
    public static readonly whenReloading = new PromiseSource<void>();
    public static readonly reconnectedEvents = new EventHandlerSet<void>();
    public static connectionState = '';
    public static reconnectingPromise: Promise<void> | null = null;
    private static lastRenderBatchId = 0;

    public static start(): void {
        if (document.readyState === 'loading')
            document.addEventListener('DOMContentLoaded', () => this.startOverlayWatchers());
        else
            this.startOverlayWatchers();
    }

    // Registered right after RenderSync.init(), which already owns the host message seam, so the
    // very first batch is seen. MAUI-only: a WebView whose renderer stops consuming messages while
    // .NET keeps producing them is a mobile-app failure mode - a browser tab's Blazor IPC is
    // in-process, and WASM applies its batches synchronously.
    public static startRenderBatchWatch(): void {
        if (!ConnectivityUI.isMauiApp)
            return;

        RenderSync.observer = message => this.onIpcMessage(message);
    }

    public static resetAppConnectionState(): void {
        if (this.connectionState === '')
            return; // Already reset

        this.setAppConnectionState();
        this.reconnectedEvents.triggerSilently();
    }

    public static startReconnecting(mustReconnectBlazor: boolean): void {
        this.setAppConnectionState('Reconnecting...');
        if (!mustReconnectBlazor)
            return;

        this.reconnectingPromise ??= (async (): Promise<void> => {
            try {
                const blazor = window['Blazor'] as { reconnect?: () => Promise<boolean> } | undefined;
                if (!blazor?.reconnect) {
                    // No Blazor reconnect = it's WASM & something is severely wrong,
                    // so the best we can do is to reload the page.
                    void this.startReloading();
                    return;
                }

                await ConnectivityUI.whenReadyToReload('reconnecting');
                if (this.whenReloading.isCompleted)
                    return; // Already reloading

                warnLog?.log('startReconnecting: reconnecting...');
                try {
                    if (await blazor.reconnect())
                        return;
                }
                catch {
                    // We assume it may fail
                }

                errorLog?.log('startReconnecting: failed to reconnect, will reload...');
                void this.startReloading();
            }
            finally {
                this.reconnectingPromise = null;
            }
        })();
    }

    public static async startReloading(): Promise<void> {
        if (this.whenReloading.isCompleted)
            return; // Already reloading

        if (!ConnectivityUI.isMauiApp) // No "Reloading..." on MAUI app
            this.setAppConnectionState('Reloading...');

        warnLog?.log('startReloading: reloading...');
        this.whenReloading.resolve(undefined);

        const opusMediaRecorder = (window as unknown as OpusMediaRecorderHost).opusMediaRecorder;
        await opusMediaRecorder?.stop();

        await ConnectivityUI.whenReadyToReload('reloading');
        window.location.reload();
    }

    // Private methods

    private static startOverlayWatchers(): void {
        const errors: string[] = [];
        const reconnectDiv = document.getElementById('components-reconnect-modal');
        if (reconnectDiv) {
            const checkReconnectDiv = () => {
                if (this.whenReloading.isCompleted)
                    return;

                const classList = reconnectDiv.classList;
                if (classList.length == 0 || classList.contains('components-reconnect-hide'))
                    this.resetAppConnectionState();
                else if (classList.contains('components-reconnect-rejected')) {
                    warnLog?.log(
                        'startReloading: triggered by #components-reconnect-modal',
                        { classes: reconnectDiv.className, text: reconnectDiv.innerText.trim().slice(0, 300) });
                    void this.startReloading();
                }
                else if (classList.contains('components-reconnect-failed'))
                    this.startReconnecting(true);
                else if (classList.contains('components-reconnect-show'))
                    this.startReconnecting(false);
            };
            const observer = new MutationObserver(() => checkReconnectDiv());
            observer.observe(reconnectDiv, { attributes: true });
            checkReconnectDiv();
        }
        else
            errors.push('no reconnectDiv');

        const errorDiv = document.getElementById('blazor-error-ui');
        if (errorDiv) {
            const checkErrorDiv = () => {
                if (errorDiv.style.display === 'block') {
                    warnLog?.log(
                        'startReloading: triggered by #blazor-error-ui',
                        { text: errorDiv.innerText.trim().slice(0, 300) });
                    void this.startReloading();
                }
            };
            const observer = new MutationObserver(() => checkErrorDiv());
            observer.observe(errorDiv, { attributes: true });
            checkErrorDiv();
        }
        else
            errors.push('no errorDiv');

        if (errors.length === 0)
            infoLog?.log('startOverlayWatchers: completed');
        else
            errorLog?.log('startOverlayWatchers: errors:', errors);
    }

    private static onIpcMessage(data: string): void {
        if (this.whenReloading.isCompleted || !data.startsWith(RenderBatchPrefix))
            return;

        const idEnd = data.indexOf(',', RenderBatchPrefix.length);
        const batchId = idEnd < 0 ? NaN : Number(data.slice(RenderBatchPrefix.length, idEnd));
        if (!Number.isFinite(batchId))
            return;

        const lastBatchId = this.lastRenderBatchId;
        this.lastRenderBatchId = batchId;
        // A backwards id means .NET built a new page context against this same document, which restarts
        // the sequence - not a loss. Only the first id after attaching is 0, and that has no predecessor.
        if (lastBatchId === 0 || batchId <= lastBatchId + 1)
            return;

        warnLog?.log(
            'startReloading: triggered by a render batch gap',
            { lost: batchId - lastBatchId - 1, expected: lastBatchId + 1, received: batchId });
        void this.startReloading();
    }

    private static setAppConnectionState(state = ''): void {
        if (this.connectionState === state)
            return;

        this.connectionState = state;
        if (this.whenReloading.isCompleted)
            return;

        const appConnectionStateDiv = document.getElementById('app-connection-state');
        if (!appConnectionStateDiv)
            return;

        if (state) {
            appConnectionStateDiv.innerHTML = `
                <div class="c-bg"></div>
                <div class="c-circle-blur"></div>
                <div class="c-circle">
                    <!-- Must have open and close tags, otherwise doesn't work! -->
                    <loading-cat-svg></loading-cat-svg>
                    <span class="c-text">${state}</span>
                </div>
            `;
            appConnectionStateDiv.style.display = '';
        }
        else {
            appConnectionStateDiv.innerHTML = '';
            appConnectionStateDiv.style.display = 'none';
        }
    }
}
