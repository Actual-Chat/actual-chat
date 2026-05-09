// Worker entry script — bundled by `build.mjs`, instantiated by the main thread.

import { bootstrapWorker } from 'worker-bootstrap';

bootstrapWorker(() => import('./recorder-worker-host'));
