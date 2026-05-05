import { bootstrapWorker } from 'worker-bootstrap';

bootstrapWorker(() => import('./video-processing-worker'));
