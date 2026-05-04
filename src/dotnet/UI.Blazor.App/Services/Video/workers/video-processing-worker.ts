/**
 * Video Processing Worker — entry point.
 * Initializes RPC and wires callbacks to the implementation in video-processing.ts.
 */

import { rpcClientServer } from 'rpc';
import { getLogs } from 'logging';
import type { VideoProcessingWorkerCallbacks } from './video-processing-worker-contract';
import { serverImpl, setCallbacks } from './video-processing';

const { infoLog, warnLog } = getLogs('VideoPipeline');

// Worker-side stall detector. Catches the worker's own analogue of the main
// thread's MainThreadDiagnostics watchdog — long sync handlers, microtask
// saturation, GPU sync waits. Logs once per stall, then re-arms.
(() => {
    const STALL_THRESHOLD_MS = 250;
    const CHECK_PERIOD_MS = 100;
    let lastTick = performance.now();
    let stallStart = 0;
    setInterval(() => {
        const now = performance.now();
        const delta = now - lastTick;
        if (delta > STALL_THRESHOLD_MS) {
            if (!stallStart) {
                stallStart = lastTick;
                warnLog?.log(`worker stall: ${Math.round(delta)}ms (threshold ${STALL_THRESHOLD_MS}ms)`);
            }
        } else if (stallStart) {
            warnLog?.log(`worker stall recovered after ${Math.round(now - stallStart)}ms total`);
            stallStart = 0;
        }
        lastTick = now;
    }, CHECK_PERIOD_MS);
})();

const callbacks = rpcClientServer<VideoProcessingWorkerCallbacks>(
    'VideoProcessingWorker',
    self as unknown as Worker,
    serverImpl,
);

setCallbacks(callbacks);

infoLog?.log('Video processing worker initialized');
