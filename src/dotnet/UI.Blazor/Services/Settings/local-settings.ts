import { Kvas } from 'kvas';
import { WebKvasBackend } from '../Kvas/web-kvas-backend';

export const localSettings = new WebKvasBackend(new Kvas('local-settings', true), "1.0", false, true);
