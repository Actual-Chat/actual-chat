import { defineConfig } from 'vitest/config';
import path from 'path';

const src = (name: string) => path.resolve(__dirname, `src/nodejs/src/${name}.ts`);
const pkg = (name: string) => path.resolve(__dirname, `src/nodejs/src/${name}/index.ts`);

export default defineConfig({
    resolve: {
        alias: {
            // Mirror tsconfig.json paths: bare imports resolve to src/nodejs/src/*
            'logging-init': src('logging-init'),
            'logging': src('logging'),
            'timeout': src('timeout'),
            'timerQueue': src('timerQueue'),
            'disposable': src('disposable'),
            'event-handling': src('event-handling'),
            'resettable': src('resettable'),
            'resilient-stream': src('resilient-stream'),
            'rpc': src('rpc'),
            'math': src('math'),
            'object-pool': src('object-pool'),
            'buffers': src('buffers'),
            'app-constants': src('app-constants'),
            'device-info': src('device-info'),
            'fast-raf': src('fast-raf'),
            'async-video-encoder': src('async-video-encoder'),
            'clocks': src('clocks'),
            'ix-ext': src('ix-ext'),
            'pipes': src('pipes'),
            'async-iterables': src('async-iterables'),
            'shared-settings': src('shared-settings'),
            'shared-settings-worker': src('shared-settings-worker'),
            'orientation': src('orientation'),
            'async-processor': src('async-processor'),
            'actuallab-core': pkg('actuallab-core'),
            'actuallab-rpc': pkg('actuallab-rpc'),
        },
    },
    test: {
        include: ['tests/ts/unit/**/*.test.ts'],
        reporters: ['default', 'junit'],
        outputFile: {
            junit: 'tmp/ts-unit-tests.xml',
        },
    },
});
