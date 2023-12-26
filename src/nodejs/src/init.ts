import { DeviceInfo } from "device-info";
import { Interactive } from 'interactive';
import { Gestures } from 'gestures';
import { ServiceWorker } from 'service-worker';

DeviceInfo.init();
Interactive.init();
Gestures.init();
void ServiceWorker.init();
