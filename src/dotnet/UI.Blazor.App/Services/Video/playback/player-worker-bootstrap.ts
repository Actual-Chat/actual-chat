// Worker entry script. Defers everything to player-worker-host so that
// worker logging is initialized first.

import { bootstrapWorker } from 'worker-bootstrap';

bootstrapWorker(() => import('./player-worker-host'));
