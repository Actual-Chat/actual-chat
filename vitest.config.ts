import { defineConfig } from 'vitest/config';
import path from 'path';

export default defineConfig({
    resolve: {
        alias: {
            // Mirror tsconfig.json paths: bare imports resolve to src/nodejs/src/*
            'logging-init': path.resolve(__dirname, 'src/nodejs/src/logging-init.ts'),
            'logging': path.resolve(__dirname, 'src/nodejs/src/logging.ts'),
            'promises': path.resolve(__dirname, 'src/nodejs/src/promises.ts'),
            'timeout': path.resolve(__dirname, 'src/nodejs/src/timeout.ts'),
            'timerQueue': path.resolve(__dirname, 'src/nodejs/src/timerQueue.ts'),
            'disposable': path.resolve(__dirname, 'src/nodejs/src/disposable.ts'),
            'resilient-stream': path.resolve(__dirname, 'src/nodejs/src/resilient-stream.ts'),
        },
    },
    test: {
        include: ['src/nodejs/tests/**/*.test.ts'],
    },
});
