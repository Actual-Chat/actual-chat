import { defineConfig } from 'vitest/config';
import path from 'path';

const src = (name: string) => path.resolve(__dirname, `src/nodejs/src/${name}.ts`);

export default defineConfig({
    resolve: {
        alias: {
            // Mirror tsconfig.json paths: bare imports resolve to src/nodejs/src/*
            'logging-init': src('logging-init'),
            'logging': src('logging'),
            'promises': src('promises'),
            'timeout': src('timeout'),
            'timerQueue': src('timerQueue'),
            'disposable': src('disposable'),
            'resettable': src('resettable'),
            'resilient-stream': src('resilient-stream'),
            'rpc': src('rpc'),
            'math': src('math'),
            'object-pool': src('object-pool'),
            'server-clock': src('server-clock'),
            'async-processor': src('async-processor'),
        },
    },
    test: {
        include: ['src/nodejs/tests/**/*.test.ts'],
    },
});
