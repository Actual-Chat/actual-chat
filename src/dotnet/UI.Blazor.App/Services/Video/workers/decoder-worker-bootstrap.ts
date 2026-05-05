import { bootstrapWorker } from 'worker-bootstrap';

bootstrapWorker(() => import('./decoder-worker'));
