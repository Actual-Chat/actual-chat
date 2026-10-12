import assert from 'node:assert/strict';
import { test } from 'node:test';
import { checkFiles, collectAppliedUtilities, findUtilitySelectors } from './check-css-selectors.mjs';

const applied = collectAppliedUtilities([`
    .copied-hint { @apply hidden; }
    .tile { @apply flex-x md:hover:hidden !rounded; }
    .title { @apply text-success !important; }
    .wide { @apply w-[calc(100%-1rem)] top-1/2; }
`]);

function classNamesIn(css) {
    return findUtilitySelectors(css, applied).map(finding => finding.className);
}

test('counts a utility once per @apply, whatever its variants and flags', () => {
    assert.equal(applied.get('hidden'), 2);
    assert.equal(applied.get('rounded'), 1);
    assert.equal(applied.get('text-success'), 1);
    assert.equal(applied.get('w-[calc(100%-1rem)]'), 1);
    assert.equal(applied.has('important'), false);
});

test('reports an applied utility in a compound, a descendant and a functional selector', () => {
    assert.deepEqual(classNamesIn('.btn-share-action.hidden { display: none; }'), ['hidden']);
    assert.deepEqual(classNamesIn('.status i.text-success { color: green; }'), ['text-success']);
    assert.deepEqual(classNamesIn('.modal:not(:has(.btn:not(.hidden))) .footer { display: none; }'), ['hidden']);
    assert.deepEqual(classNamesIn(':where(.flex-x, .other) .btn { gap: 0; }'), ['flex-x']);
});

test('leaves alone a class nobody applies, and an applied one matched as an attribute', () => {
    assert.deepEqual(classNamesIn('.btn-share-action.concealed { display: none; }'), []);
    assert.deepEqual(classNamesIn('.btn-share-action[class~="hidden"] { display: none; }'), []);
    assert.deepEqual(classNamesIn('a[href$=".hidden"] { color: red; }'), []);
});

test('leaves alone a definition: the bare class, and anything inside @layer', () => {
    assert.deepEqual(classNamesIn('.hidden { display: none; }'), []);
    assert.deepEqual(classNamesIn(':root, .hidden { --x: 1; }'), []);
    assert.deepEqual(classNamesIn('@layer components { body.narrow .flex-x::before { gap: 0; } }'), []);
});

test('reports the line of the offending selector in a multi-line list', () => {
    const findings = findUtilitySelectors('.a,\n.b .hidden,\n.c {\n    color: red;\n}\n', applied);
    assert.equal(findings.length, 1);
    assert.equal(findings[0].line, 2);
    assert.equal(findings[0].selector, '.b .hidden');
    assert.equal(findings[0].applyCount, 2);
});

test('the source stylesheets name no applied utility in a selector', () => {
    assert.deepEqual(checkFiles(), []);
});
