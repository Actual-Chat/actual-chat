/**
 * Video Processing Worker — entry point.
 * Initializes RPC and wires callbacks to the implementation in video-processing.ts.
 */

import { rpcClientServer } from 'rpc';
import { Log } from 'logging';
import type { VideoProcessingWorkerCallbacks } from './video-processing-worker-contract';
import { serverImpl, setCallbacks } from './video-processing';

const { infoLog } = Log.get('VideoPipeline');

const callbacks = rpcClientServer<VideoProcessingWorkerCallbacks>(
    'VideoProcessingWorker',
    self as unknown as Worker,
    serverImpl,
);

setCallbacks(callbacks);

infoLog?.log('Video processing worker initialized');
