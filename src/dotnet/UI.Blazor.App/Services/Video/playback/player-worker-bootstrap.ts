// Worker entry script for the playback worker. Picked up by the bundler
// via the `new Worker(new URL('./player-worker-bootstrap.ts', ...))`
// site in the main thread. This file is intentionally minimal — it
// defers everything (RPC plumbing, hook installation) to the host
// module, which `bootstrapWorker` imports lazily so that worker logging
// is initialized first.

import { bootstrapWorker } from 'worker-bootstrap';

bootstrapWorker(() => import('./player-worker-host'));
