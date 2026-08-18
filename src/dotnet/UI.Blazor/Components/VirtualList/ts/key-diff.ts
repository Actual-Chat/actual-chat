// Order-preserving key diff, as a text diff would produce it. A removal and an addition landing in the
// same position are paired into one edit, which is what tells an item replacing another - "Transcribing..."
// giving way to the transcript - from an item genuinely arriving.
export interface KeyDiffOp<T> {
    kind: 'keep' | 'add' | 'remove';
    key: T;
    replacedKey?: T;
}

export function diffKeys<T>(oldKeys: readonly T[], newKeys: readonly T[]): KeyDiffOp<T>[] {
    const oldCount = oldKeys.length;
    const newCount = newKeys.length;
    const lcs: number[][] = Array.from({ length: oldCount + 1 }, () => new Array<number>(newCount + 1).fill(0));
    for (let i = oldCount - 1; i >= 0; i--)
        for (let j = newCount - 1; j >= 0; j--)
            lcs[i][j] = oldKeys[i] === newKeys[j]
                ? lcs[i + 1][j + 1] + 1
                : Math.max(lcs[i + 1][j], lcs[i][j + 1]);

    const ops = new Array<KeyDiffOp<T>>();
    let i = 0, j = 0;
    while (i < oldCount && j < newCount) {
        if (oldKeys[i] === newKeys[j]) {
            ops.push({ kind: 'keep', key: newKeys[j] });
            i++;
            j++;
        }
        else if (lcs[i + 1][j] >= lcs[i][j + 1]) {
            ops.push({ kind: 'remove', key: oldKeys[i] });
            i++;
        }
        else {
            ops.push({ kind: 'add', key: newKeys[j] });
            j++;
        }
    }
    for (; i < oldCount; i++)
        ops.push({ kind: 'remove', key: oldKeys[i] });
    for (; j < newCount; j++)
        ops.push({ kind: 'add', key: newKeys[j] });

    // Pair each run of removals with the additions that took their place.
    for (let k = 0; k < ops.length; k++) {
        if (ops[k].kind !== 'remove')
            continue;

        let removeEnd = k;
        while (removeEnd < ops.length && ops[removeEnd].kind === 'remove')
            removeEnd++;
        for (let r = k, a = removeEnd; r < removeEnd && a < ops.length && ops[a].kind === 'add'; r++, a++)
            ops[a].replacedKey = ops[r].key;
        k = removeEnd - 1;
    }
    return ops;
}
