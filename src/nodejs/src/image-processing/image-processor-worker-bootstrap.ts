import { bootstrapWorker } from 'worker-bootstrap';

bootstrapWorker(() => import('./image-processor-worker'));
