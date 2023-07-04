export class Vector2D {
    public static readonly zero = new Vector2D(0, 0);
    public static readonly unitX = new Vector2D(1, 0);
    public static readonly unitY = new Vector2D(0, 1);

    constructor(public x: number, public y: number) { }

    public add(other: Vector2D): Vector2D {
        return new Vector2D(this.x + other.x, this.y + other.y);
    }

    public sub(other: Vector2D): Vector2D {
        return new Vector2D(this.x - other.x, this.y - other.y);
    }

    public mul(multiplier: number): Vector2D {
        return new Vector2D(this.x * multiplier, this.y * multiplier);
    }

    public dotProduct(other: Vector2D): number {
        return this.x * other.x + this.y * other.y;
    }

    public get length(): number {
        return Math.sqrt(this.squareLength);
    }

    public get squareLength(): number {
        return this.x * this.x + this.y * this.y;
    }

    public isHorizontal(minRatio = 1) {
        return Math.abs(this.x) > minRatio * Math.abs(this.y);
    }

    public isVertical(minRatio = 1) {
        return Math.abs(this.y) > minRatio * Math.abs(this.x);
    }
}

export function clamp(n: number, min: number, max: number) {
    return Math.max(min, Math.min(max, n));
}
