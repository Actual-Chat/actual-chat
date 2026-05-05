import { bootstrapWorker } from 'worker-bootstrap';

bootstrapWorker(() => import('./opus-encoder-worker'));
