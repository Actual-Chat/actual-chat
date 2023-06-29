import { Kvas } from 'kvas';
import { WebKvasBackend } from '../Kvas/web-kvas-backend';

export const clientComputedCache = new WebKvasBackend(new Kvas('ccc', true), "1.0", true, true);
