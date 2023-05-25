import { Log } from 'logging';
import { DeviceInfo } from 'device-info';

const { debugLog } = Log.get('InertialScroll');

export class InertialScroll {
    private overflowBackup: string;
    private count: number = 0;

    public constructor(private readonly element: HTMLElement) {
    }

    private get isRequired() {
        return DeviceInfo.isIos && DeviceInfo.isWebKit;
    }

    public freeze(): void {
        if (!this.isRequired)
            return;

        debugLog?.log('-> freeze()');
        if (this.count <= 0) {
            debugLog?.log('backing overflow=', this.element.style.overflow, 'up because freezeCount=', this.count);
            this.overflowBackup = this.element.style.overflow;
        }
        this.count++;
        this.element.style.overflow = 'hidden';
        debugLog?.log('<- freeze()');
    }

    public unfreeze(): void {
        if (!this.isRequired)
            return;

        debugLog?.log('-> unfreeze()');
        if (--this.count <= 0) {
            debugLog?.log('restoring overflow to ', this.element.style.overflow, 'because freezeCount=', this.count);
            this.element.style.overflow = this.overflowBackup;
        }
        debugLog?.log('<- unfreeze()');
    }

    public interrupt() {
        this.freeze();
        this.unfreeze();
    }
}
