import {Log} from "logging";

const { infoLog } = Log.get('BulkInitUI');

export function bulkInit(calls: Array<unknown>) {
    infoLog?.log('-> bulkInit');
    calls = Array.from(calls);
    for (let i = 0; i < calls.length;) {
        const name = calls[i] as string;
        const argumentCount = calls[i + 1] as number;
        const nextIndex = i + 2 + argumentCount;
        const args = calls.slice(i + 2, nextIndex);
        i = nextIndex;
        globalInvoke(name, args);
    }
    infoLog?.log('<- bulkInit');
}

// Helpers

function globalInvoke(name: string, args: unknown[]) {
    const fn = globalEval(name) as Function;
    if (typeof fn === 'function') {
        const [typeName, methodName] = splitLast(name, '.');
        if (methodName === '') {
            fn(...args);
            infoLog?.log(`globalInvoke:`, name, ', arguments:', args);
        }
        else {
            const self = globalEval(typeName);
            infoLog?.log(`globalInvoke:`, name, ', this:', self, ', arguments:', args);
            fn.apply(self, args);
        }
    }
    else {
        infoLog?.log(`globalInvoke: script:`, name);
    }
}

function globalEval(...args: any) {
    return eval.apply(this, args);
}

function splitLast(source, by) {
    const lastIndex = source.lastIndexOf(by);
    if (lastIndex < 0)
        return [source, ''];

    const before = source.slice(0, lastIndex);
    const after = source.slice(lastIndex + 1);
    return [before, after];
}
