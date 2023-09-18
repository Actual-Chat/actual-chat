import { Interactive } from 'interactive';
import { Gestures } from 'gestures';
import { ServiceWorker } from 'service-worker';

Interactive.init();
Gestures.init();
void ServiceWorker.init();
