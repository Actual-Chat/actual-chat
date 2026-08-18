import { describe, it, expect } from 'vitest';
import { diffKeys } from '../../../src/dotnet/UI.Blazor/Components/VirtualList/ts/key-diff';

const kinds = (oldKeys: string[], newKeys: string[]) =>
    diffKeys(oldKeys, newKeys).map(x => `${x.kind}:${x.key}${x.replacedKey ? `<-${x.replacedKey}` : ''}`);

const added = (oldKeys: string[], newKeys: string[]) =>
    diffKeys(oldKeys, newKeys).filter(x => x.kind === 'add');

describe('diffKeys', () => {
    it('should report every key as kept when nothing changed', () => {
        expect(kinds(['a', 'b', 'c'], ['a', 'b', 'c'])).toEqual(['keep:a', 'keep:b', 'keep:c']);
    });

    it('should report an appended key as an unpaired addition', () => {
        const adds = added(['a', 'b'], ['a', 'b', 'c']);
        expect(adds).toEqual([{ kind: 'add', key: 'c' }]);
    });

    it('should report a prepended key as an unpaired addition', () => {
        const adds = added(['b', 'c'], ['a', 'b', 'c']);
        expect(adds).toEqual([{ kind: 'add', key: 'a' }]);
    });

    it('should report an interleaved key as an unpaired addition', () => {
        const adds = added(['a', 'c'], ['a', 'b', 'c']);
        expect(adds).toEqual([{ kind: 'add', key: 'b' }]);
    });

    it('should pair a key that took a removed key\'s place into an edit', () => {
        const adds = added(['a', 'b', 'transcribing'], ['a', 'b', 'transcript']);
        expect(adds).toEqual([{ kind: 'add', key: 'transcript', replacedKey: 'transcribing' }]);
    });

    it('should pair an edit in the middle of retained keys', () => {
        const adds = added(['a', 'x', 'c'], ['a', 'y', 'c']);
        expect(adds).toEqual([{ kind: 'add', key: 'y', replacedKey: 'x' }]);
    });

    it('should pair each key of a multi-key replacement in order', () => {
        const adds = added(['a', 'x1', 'x2', 'd'], ['a', 'y1', 'y2', 'd']);
        expect(adds).toEqual([
            { kind: 'add', key: 'y1', replacedKey: 'x1' },
            { kind: 'add', key: 'y2', replacedKey: 'x2' },
        ]);
    });

    it('should leave surplus additions unpaired when more keys arrive than left', () => {
        const adds = added(['a', 'x', 'd'], ['a', 'y1', 'y2', 'd']);
        expect(adds).toEqual([
            { kind: 'add', key: 'y1', replacedKey: 'x' },
            { kind: 'add', key: 'y2' },
        ]);
    });

    it('should report removals with no successor as plain removals', () => {
        expect(kinds(['a', 'b', 'c'], ['a', 'c'])).toEqual(['keep:a', 'remove:b', 'keep:c']);
    });

    it('should treat the first population as all additions', () => {
        expect(kinds([], ['a', 'b'])).toEqual(['add:a', 'add:b']);
    });

    it('should not pair across retained keys', () => {
        // The removal and the addition are separated by a kept key, so they are two events, not an edit.
        const adds = added(['x', 'a'], ['a', 'y']);
        expect(adds).toEqual([{ kind: 'add', key: 'y' }]);
    });

    // The list treats the previous render's first and last keys as the bounds of its loaded range:
    // additions outside them are an expansion, additions inside are items that genuinely arrived.
    const bounds = (oldKeys: string[], newKeys: string[]) => {
        const ops = diffKeys(oldKeys, newKeys);
        const firstOld = ops.findIndex(x => x.kind !== 'add');
        const lastOld = ops.reduce((acc, x, i) => x.kind !== 'add' ? i : acc, -1);
        return ops.flatMap((op, i) => op.kind === 'add'
            ? [{ key: op.key, isOutside: i < firstOld || i > lastOld }]
            : []);
    };

    it('should place keys loaded above the old range outside it, and a new last key inside', () => {
        // old: [10 11 12 edge], new: [8 9 10 11 12 13 edge] - 8 and 9 are an expansion, 13 arrived.
        expect(bounds(['10', '11', '12', 'edge'], ['8', '9', '10', '11', '12', '13', 'edge'])).toEqual([
            { key: '8', isOutside: true },
            { key: '9', isOutside: true },
            { key: '13', isOutside: false },
        ]);
    });

    it('should place a key appended past the old range outside it', () => {
        // Without the edge still in the set, 13 lands past the last old key.
        expect(bounds(['10', '11', '12'], ['10', '11', '12', '13'])).toEqual([
            { key: '13', isOutside: true },
        ]);
    });

    it('should not move the bound inwards when the old range loses its end', () => {
        // 12 is unloaded in the same render that loads 8: 8 is still an expansion, not an insertion.
        expect(bounds(['10', '11', '12'], ['8', '9', '10', '11'])).toEqual([
            { key: '8', isOutside: true },
            { key: '9', isOutside: true },
        ]);
    });

    it('should preserve order of the resulting sequence', () => {
        const ops = diffKeys(['a', 'b', 'c'], ['a', 'x', 'c', 'd']);
        expect(ops.filter(o => o.kind !== 'remove').map(o => o.key)).toEqual(['a', 'x', 'c', 'd']);
    });
});
