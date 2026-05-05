import { bootstrapWorker } from 'worker-bootstrap';

bootstrapWorker(() => import('./opus-decoder-worker'));
