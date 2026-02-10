import { clamp } from 'math';

const DefaultItemSize = 48;
const MinResponseFulfillmentRatio = 0.25;
const MaxResponseFulfillmentRatio = 2;
const ResponseExpectedCountSumResetThreshold = 1000;
const ResponseExpectedCountSumResetValue = 800;

export class VirtualListStatistics {
    private _itemCount = 0;
    private _itemSizeSum = 0;
    private _responseActualCountSum = 0;
    private _responseExpectedCountSum = 0;

    public get itemSize(): number {
        return this._itemCount == 0
            ? DefaultItemSize
            : this._itemSizeSum / this._itemCount;
    }

    public get responseFulfillmentRatio(): number {
        const num = this._responseExpectedCountSum < 1
            ? MaxResponseFulfillmentRatio
            : this._responseActualCountSum / this._responseExpectedCountSum;
        return clamp(num, MinResponseFulfillmentRatio, MaxResponseFulfillmentRatio);
    }

    public addItem(size: number): void {
        if (!(size > 0))
            return;

        this._itemSizeSum += size;
        this._itemCount++;
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
