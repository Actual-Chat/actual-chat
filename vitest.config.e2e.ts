import { defineConfig } from 'vitest/config';
import path from 'path';

const src = (name: string) => path.resolve(__dirname, `src/nodejs/src/${name}.ts`);

export default defineConfig({
    resolve: {
        alias: {
            'logging-init': src('logging-init'),
            'logging': src('logging'),
            'timeout': src('timeout'),
            'timerQueue': src('timerQueue'),
            'disposable': src('disposable'),
            'resettable': src('resettable'),
            'resilient-stream': src('resilient-stream'),
            'rpc': src('rpc'),
            'math': src('math'),
            'object-pool': src('object-pool'),
            'clocks': src('clocks'),
            'async-processor': src('async-processor'),
        },
    },
    test: {
        include: ['tests/ts/e2e/**/*.test.ts'],
        globalSetup: ['tests/ts/e2e/global-setup.ts'],
        testTimeout: 60_000,
        hookTimeout: 120_000,
        // Run files sequentially: all tests share one test account and
        // parallel sign-ins race each other on the same server flow.
        fileParallelism: false,
        reporters: ['default', 'junit'],
        outputFile: {
            junit: 'tmp/ts-e2e-tests.xml',
        },
    },
});
