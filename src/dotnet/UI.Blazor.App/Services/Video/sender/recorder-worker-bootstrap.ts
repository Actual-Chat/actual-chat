// Worker entry script for the recorder worker. Counterpart of
// `playback/player-worker-bootstrap.ts`. Picked up by the bundler via
// `build.mjs`; the main thread instantiates a worker pointing at the
// resulting bundle.

import { bootstrapWorker } from 'worker-bootstrap';

bootstrapWorker(() => import('./recorder-worker-host'));
