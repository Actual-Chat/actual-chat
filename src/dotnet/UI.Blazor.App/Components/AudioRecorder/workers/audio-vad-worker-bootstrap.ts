import { bootstrapWorker } from 'worker-bootstrap';

bootstrapWorker(() => import('./audio-vad-worker'));
