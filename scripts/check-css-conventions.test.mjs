import assert from 'node:assert/strict';
import { test } from 'node:test';
import { findViolations } from './check-css-conventions.mjs';

const linesOf = css => findViolations(css).map(finding => finding.line);

test('reports a physical utility, property and alignment', () => {
    assert.deepEqual(linesOf('.a {\n    @apply ml-2 md:-right-4;\n}'), [2, 2]);
    assert.deepEqual(linesOf('.a {\n    padding-left: 1rem;\n    text-align: right;\n}'), [2, 3]);
    assert.deepEqual(linesOf('.a {\n    @apply rounded-tl-lg border-r;\n}'), [2, 2]);
});

test('leaves alone the logical forms and class names that only contain a side', () => {
    assert.deepEqual(linesOf('.left-panel > .c-right { @apply ms-2 pe-4 start-0 text-start rounded-s-lg; }'), []);
    assert.deepEqual(linesOf('.a { margin-inline-start: 1rem; inset-inline-end: 0; text-align: start; }'), []);
});

test('accepts a marked rule, a safe area and centering', () => {
    assert.deepEqual(linesOf('.a {\n    /* physical: set from JS */\n    @apply left-0;\n}'), []);
    assert.deepEqual(linesOf('.a { left: var(--safe-area-left); right: calc(1rem + var(--vp-safe-right)); }'), []);
    assert.deepEqual(linesOf('.a { @apply absolute left-1/2 -translate-x-1/2; }'), []);
    assert.deepEqual(linesOf('.a { left: 50%; transform: translateX(-50%); }'), []);
    assert.deepEqual(linesOf('.a { @apply left-1/2; }'), [1]);
});

test('reports keyframes, transform origins and the physical space/divide utilities', () => {
    assert.deepEqual(linesOf('@keyframes slide {\n    from { left: 0; }\n    to { transform: none; }\n}'), [2]);
    assert.deepEqual(linesOf('.a {\n    transform-origin: left center;\n    @apply origin-top-right space-x-2;\n}'), [3, 3, 2]);
    assert.deepEqual(linesOf('.a { transform-origin: center; @apply origin-center divide-y; }'), []);
});

test('reports a c- part reached through a descendant combinator, inside :is() and :where() too', () => {
    assert.deepEqual(linesOf('.chat-list .c-title { color: red; }'), [1]);
    assert.deepEqual(linesOf('.chat-list > .c-header .btn.c-close { color: red; }'), [1]);
    assert.deepEqual(linesOf('.chat-list :is(.item.c-active) { color: red; }'), [1]);
    assert.deepEqual(linesOf('.chat-list > :where(.c-a .c-b) { color: red; }'), [1]);
});

test('accepts c- parts selected as children, and named classes as descendants', () => {
    assert.deepEqual(linesOf('.chat-list > .c-header > .c-title, .c-title { color: red; }'), []);
    assert.deepEqual(linesOf('.author-modal .tile-item > .c-content { color: red; }'), []);
    assert.deepEqual(linesOf('.bar-chart > .c-column > :where(.c-track > .c-value) { color: red; }'), []);
    assert.deepEqual(linesOf('.list :not(.c-hidden) > .c-row { color: red; }'), []);
});
