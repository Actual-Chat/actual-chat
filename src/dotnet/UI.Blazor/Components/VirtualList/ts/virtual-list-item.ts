import { NumberRange } from './range';

export class VirtualListItem {
    constructor(public key: string, public countAs: number)
    {
        this.range = null;
        this.size = -1;
    }

    public range?: NumberRange;
    public size?: number;
    public isOld?: boolean;

    get isMeasured(): boolean {
        return (this.size ?? -1) >= 0 && this.range != null;
    }

    get isChatEntry(): boolean {
        return !isNaN(+this.key);
    }
}
