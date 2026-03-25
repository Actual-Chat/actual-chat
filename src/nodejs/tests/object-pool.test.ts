import { describe, it, expect } from 'vitest';
import { ObjectPool, AsyncObjectPool } from 'object-pool';

describe('ObjectPool', () => {
    it('should create objects via factory', () => {
        let counter = 0;
        const pool = new ObjectPool(() => ++counter);

        expect(pool.get()).toBe(1);
        expect(pool.get()).toBe(2);
    });

    it('should reuse released objects', () => {
        let counter = 0;
        const pool = new ObjectPool(() => ({ id: ++counter }));

        const obj1 = pool.get();
        expect(obj1.id).toBe(1);

        pool.release(obj1);
        const obj2 = pool.get();
        expect(obj2).toBe(obj1); // same instance reused
    });

    it('should expandTo pre-populate the pool', () => {
        let counter = 0;
        const pool = new ObjectPool(() => ++counter);
        pool.expandTo(3);

        // All 3 should come from pool (created during expandTo)
        const a = pool.get();
        const b = pool.get();
        const c = pool.get();
        expect([a, b, c].sort()).toEqual([1, 2, 3]);

        // 4th requires new creation
        const d = pool.get();
        expect(d).toBe(4);
    });

    it('should call reset on Resettable objects', async () => {
        let resetCount = 0;
        const pool = new ObjectPool(() => ({
            value: 0,
            reset() { resetCount++; },
        }));

        const obj = pool.get();
        obj.value = 42;
        pool.release(obj);

        // Wait for async reset
        await new Promise(resolve => setTimeout(resolve, 10));
        expect(resetCount).toBe(1);
    });
});

describe('AsyncObjectPool', () => {
    it('should create objects via async factory', async () => {
        let counter = 0;
        const pool = new AsyncObjectPool(async () => ++counter);

        expect(await pool.get()).toBe(1);
        expect(await pool.get()).toBe(2);
    });

    it('should reuse released objects', async () => {
        let counter = 0;
        const pool = new AsyncObjectPool(async () => ({ id: ++counter }));

        const obj1 = await pool.get();
        await pool.release(obj1);
        const obj2 = await pool.get();
        expect(obj2).toBe(obj1);
    });

    it('should expandTo pre-populate', async () => {
        let counter = 0;
        const pool = await new AsyncObjectPool(async () => ++counter).expandTo(2);

        const a = await pool.get();
        const b = await pool.get();
        expect([a, b].sort()).toEqual([1, 2]);
    });

    it('should call reset on Resettable objects', async () => {
        let resetCount = 0;
        const pool = new AsyncObjectPool(async () => ({
            reset() { resetCount++; },
        }));

        const obj = await pool.get();
        await pool.release(obj);
        expect(resetCount).toBe(1);
    });
});
