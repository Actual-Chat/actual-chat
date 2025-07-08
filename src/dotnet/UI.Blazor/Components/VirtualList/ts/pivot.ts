import { NumberRange } from './range';

export interface Pivot {
    itemKey: string;
    time: number;
    range: NumberRange;
    isVisible: boolean;
    isInteractive: boolean;
    stickyOffset: number | null;
}
