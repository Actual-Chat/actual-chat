import { Kvas } from 'kvas';
import { WebKvasBackend } from '../Kvas/web-kvas-backend';

export const remoteComputedCache = new WebKvasBackend(new Kvas('ccc', true), "3.0", true, true);
