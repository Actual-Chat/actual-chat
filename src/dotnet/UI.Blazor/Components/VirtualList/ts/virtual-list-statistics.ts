import { Clamp } from './math';

export class VirtualListStatistics {
    private _itemCount: number = 0;
    private _itemSizeSum: number = 0;
    private _responseActualCountSum: number = 0;
    private _responseExpectedCountSum: number = 0;

    public DefaultItemSize: number = 100;
    public MinItemSize: number = 8;
    public MaxItemSize: number = 16_384;
    public ItemCountResetThreshold: number = 1000;
    public ItemCountResetValue: number = 900;

    public get ItemSize(): number {
        const num = this._itemCount == 0
                    ? this.DefaultItemSize
                    : this._itemSizeSum / this._itemCount;
        return Clamp(num, this.MinItemSize, this.MaxItemSize);
    }

    public MinResponseFulfillmentRatio: number = 0.25;
    public MaxResponseFulfillmentRatio: number = 1;
    public ResponseExpectedCountSumResetThreshold: number = 1000;
    public ResponseExpectedCountSumResetValue: number = 800;

    public get ResponseFulfillmentRatio(): number {
        const num = this._responseExpectedCountSum < 1
                    ? this.MaxResponseFulfillmentRatio
                    : this._responseActualCountSum / this._responseExpectedCountSum;
        return Clamp(num, this.MinResponseFulfillmentRatio, this.MaxResponseFulfillmentRatio);
    }

    public AddItem(size: number, countAs: number): void {
        if (countAs == 0)
            return;

        size /= countAs;
        this._itemSizeSum += size;
        this._itemCount += countAs;
        if (this._itemCount < this.ItemCountResetThreshold) return;

        // We change the item count too, so remaining items will have increased weight
        this._itemSizeSum *= this.ItemCountResetValue / this._itemCount;
        this._itemCount = this.ItemCountResetValue;
    }

    public AddResponse(actualCount: number, expectedCount: number): void {
        this._responseActualCountSum += actualCount;
        this._responseExpectedCountSum += expectedCount;
        if (this._responseExpectedCountSum < this.ResponseExpectedCountSumResetThreshold) return;

        // We change the item count too, so remaining items will have increased weight
        this._responseActualCountSum *= this.ResponseExpectedCountSumResetValue / this._responseExpectedCountSum;
        this._responseExpectedCountSum = this.ResponseExpectedCountSumResetValue;
    }
}
