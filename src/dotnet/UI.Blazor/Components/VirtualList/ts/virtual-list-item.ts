import { NumberRange } from './range';

export class VirtualListItem {
    constructor(public key: string)
    {
        this.range = null;
        this.size = -1;
    }

    public range?: NumberRange;
    public size?: number;
    public shouldSkipKey = false;

    get isMeasured(): boolean {
        return (this.size ?? -1) >= 0 && this.range != null;
    }

    get isChatEntry(): boolean {
        return !isNaN(+this.key);
    }
}
