import { BaseSequencer, type TestSpecification } from 'vitest/node';

/**
 * Defers the files that assert on server-side search results. A freshly written entity only
 * becomes findable ~2.5 min later (1m indexing delay, rounded up to a 1m quantum, then a 30s
 * index refresh), so on a fresh cluster these are the files that pay for a cold index.
 */
const INDEX_DEPENDENT = ['people-search'];

const isIndexDependent = (spec: TestSpecification) =>
    INDEX_DEPENDENT.some(name => spec.moduleId.replace(/\\/g, '/').includes(`/${name}.test.ts`));

export default class E2ESequencer extends BaseSequencer {
    override async sort(files: TestSpecification[]): Promise<TestSpecification[]> {
        const sorted = await super.sort(files);
        return [...sorted.filter(x => !isIndexDependent(x)), ...sorted.filter(isIndexDependent)];
    }
}
