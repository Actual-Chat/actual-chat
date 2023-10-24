import { clamp } from 'math';

const DefaultItemSize: number = 30;
const MinItemSize: number = 12;
const MaxItemSize: number = 240;
const ItemCountResetThreshold: number = 1000;
const ItemCountResetValue: number = 900;
const MinResponseFulfillmentRatio: number = 0.25;
const MaxResponseFulfillmentRatio: number = 1;
const ResponseExpectedCountSumResetThreshold: number = 1000;
const ResponseExpectedCountSumResetValue: number = 800;

export class VirtualListStatistics {
    private _itemCount: number = 0;
    private _itemSizeSum: number = 0;
    private _responseActualCountSum: number = 0;
    private _responseExpectedCountSum: number = 0;

    public get itemSize(): number {
        const num = this._itemCount == 0
            ? DefaultItemSize
            : this._itemSizeSum / this._itemCount;
        return clamp(num, MinItemSize, MaxItemSize);
    }

    public get responseFulfillmentRatio(): number {
        const num = this._responseExpectedCountSum < 1
            ? MaxResponseFulfillmentRatio
            : this._responseActualCountSum / this._responseExpectedCountSum;
        return clamp(num, MinResponseFulfillmentRatio, MaxResponseFulfillmentRatio);
    }

    public addItem(size: number, countAs: number): void {
        if (!(size > 0 && countAs > 0))
            return;

        size /= countAs;
        this._itemSizeSum += size;
        this._itemCount += countAs;
        if (this._itemCount < ItemCountResetThreshold) return;
        this._itemSizeSum *= ItemCountResetValue / this._itemCount;
        this._itemCount = ItemCountResetValue;
    }

    public addResponse(actualCount: number, expectedCount: number): void {
        this._responseActualCountSum += actualCount;
        this._responseExpectedCountSum += expectedCount;
        if (this._responseExpectedCountSum < ResponseExpectedCountSumResetThreshold) return;

        // We change the item count too, so remaining items will have increased weight
        this._responseActualCountSum *= ResponseExpectedCountSumResetValue / this._responseExpectedCountSum;
        this._responseExpectedCountSum = ResponseExpectedCountSumResetValue;
    }
}
