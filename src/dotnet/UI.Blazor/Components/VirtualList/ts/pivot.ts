import { NumberRange } from './range';

export interface Pivot {
    itemKey: string;
    offset: number;
    time: number;
    range: NumberRange;
    isVisible: boolean;
    isInteractive: boolean;
}
