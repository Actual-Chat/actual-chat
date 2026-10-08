import { beforeEach } from 'vitest';
// Not vitest/suite (deprecated in 4.1), nor TestRunner.setTestFn, which 4.1.1 binds to getFn by mistake
import { getFn, setFn, type Test } from '@vitest/runner';
import { screenshotOpenPages } from './helpers';

const wrappedTests = new WeakSet<Test>();

// Wraps the test body itself: vitest runs afterEach hooks, which hang up calls and close pages,
// before any onTestFailed handler, so a screenshot taken there shows the cleaned-up state
beforeEach(ctx => {
    const test = ctx.task;
    if (wrappedTests.has(test))
        return;

    wrappedTests.add(test);
    const fn = getFn(test);
    setFn(test, async () => {
        try {
            await fn();
        } catch (e) {
            await screenshotOpenPages(test.name);
            throw e;
        }
    });
});
