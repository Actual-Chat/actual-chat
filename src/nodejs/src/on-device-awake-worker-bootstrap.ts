import { bootstrapWorker } from 'worker-bootstrap';

bootstrapWorker(() => import('./on-device-awake-worker'));
