import { NumberRange } from './range';

export class VirtualListItem {
    constructor(public key: string)
    {
        this.size = -1;
    }

    public readonly createdAt = Date.now();
    public range?: NumberRange;
    public size?: number;
    public shouldSkipKey = false;

    get isMeasured(): boolean {
        return (this.size ?? -1) >= 0 && this.range != null;
    }
}
