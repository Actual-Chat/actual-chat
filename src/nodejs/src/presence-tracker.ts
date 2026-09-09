// Reference-counted presence, the successor to the selector-driven presence classes in
// mutation-processor.ts. Markup declares participation instead of CSS deriving it:
//
//   <div class="group" data-children="live-block">      <- counts the children named here
//     <li class="item expanded" data-child="live-block"> <- one of them
//
// and this writes `data-has-live-block` on the container while at least one child is present.
// A name binds to every ancestor declaring it, which is what container.querySelector(match) did.
//
// Why an attribute rather than a class for the result: Blazor diffs `class` and overwrites what
// we put there, which is why the selector-driven path has to watch for and restore its own
// classes. It never touches a data- attribute it did not render.
//
// Why counting rather than re-querying: the selector path recomputes every predicate for every
// dirty container on every delivery, so its cost scales with rules x containers. This scales with
// what actually changed.

const ChildAttribute = 'data-child';
const ChildrenAttribute = 'data-children';
const HasAttributePrefix = 'data-has-';
const ChildSelector = `[${ChildAttribute}]`;

interface Membership {
    name: string;
    // Captured at registration: a removed element is already detached, so its container
    // can no longer be found by walking up from it.
    container: Element;
}

const memberships = new WeakMap<Element, Membership[]>();
const counts = new WeakMap<Element, Map<string, number>>();

export const PresenceTracker = {
    observedAttributes: [ChildAttribute, ChildrenAttribute],

    scan: scanRoot,
    unscan: unscanRoot,

    onChildChanged(element: Element): void {
        unregister(element);
        register(element);
    },

    onChildrenChanged(container: Element): void {
        // The names this element claims changed, so every child under it may have moved to or from
        // it - including children of nested containers, which closest() re-resolves on its own.
        unscanRoot(container);
        scanRoot(container);
    },

    // For presence no render owns - browser state such as focus, or a mode only JS knows about.
    // Edits the token list rather than the attribute, so a second name on the element survives.
    setChild(element: Element, name: string, isPresent: boolean): void {
        const names = namesOf(element, ChildAttribute).filter(n => n !== name);
        if (isPresent)
            names.push(name);
        if (names.length === 0)
            element.removeAttribute(ChildAttribute);
        else
            element.setAttribute(ChildAttribute, names.join(' '));
    },

    // The truth, recomputed from the DOM. Counting is state, and state that only ever moves by
    // deltas cannot recover from a delivery it never saw.
    verify(root: Element = document.body): { name: string; container: Element; counted: number; actual: number }[] {
        const wrong = new Array<{ name: string; container: Element; counted: number; actual: number }>();
        for (const container of root.querySelectorAll(`[${ChildrenAttribute}]`)) {
            for (const name of namesOf(container, ChildrenAttribute)) {
                const counted = counts.get(container)?.get(name) ?? 0;
                const actual = container.querySelectorAll(childSelectorFor(name)).length;
                if (counted !== actual)
                    wrong.push({ name, container, counted, actual });
            }
        }
        return wrong;
    },
};

// Private methods

function scanRoot(root: Element): void {
    if (root.matches(ChildSelector))
        register(root);
    for (const element of root.querySelectorAll(ChildSelector))
        register(element);
}

function unscanRoot(root: Element): void {
    if (root.matches(ChildSelector))
        unregister(root);
    for (const element of root.querySelectorAll(ChildSelector))
        unregister(element);
}

function namesOf(element: Element, attribute: string): string[] {
    const value = element.getAttribute(attribute);
    if (!value)
        return [];

    return value.trim().split(/\s+/).filter(name => name.length !== 0);
}

// JSON.stringify quotes and escapes the value, which is what an attribute selector needs - CSS.escape
// escapes identifiers, not strings, and would mangle a quoted value.
function childSelectorFor(name: string): string {
    return `[${ChildAttribute}~=${JSON.stringify(name)}]`;
}

function containerFor(element: Element, name: string): Element | null {
    return element.closest(`[${ChildrenAttribute}~=${JSON.stringify(name)}]`);
}

function register(element: Element): void {
    if (memberships.has(element))
        return;

    const list = new Array<Membership>();
    for (const name of namesOf(element, ChildAttribute)) {
        // Every ancestor claiming the name, not just the nearest: the predicate this replaces was
        // container.querySelector(match), which any matching container answered for itself, so
        // nested containers both had the class. Usually one iteration.
        let container = containerFor(element, name);
        while (container !== null) {
            list.push({ name, container });
            increment(container, name);
            container = container.parentElement === null ? null : containerFor(container.parentElement, name);
        }
    }
    memberships.set(element, list);
}

function unregister(element: Element): void {
    const list = memberships.get(element);
    if (list === undefined)
        return;

    memberships.delete(element);
    for (const membership of list)
        decrement(membership.container, membership.name);
}

function increment(container: Element, name: string): void {
    let byName = counts.get(container);
    if (byName === undefined)
        counts.set(container, byName = new Map<string, number>());

    const count = (byName.get(name) ?? 0) + 1;
    byName.set(name, count);
    if (count === 1)
        container.setAttribute(HasAttributePrefix + name, '');
}

function decrement(container: Element, name: string): void {
    const byName = counts.get(container);
    if (byName === undefined)
        return;

    const count = (byName.get(name) ?? 0) - 1;
    if (count > 0) {
        byName.set(name, count);
        return;
    }

    byName.delete(name);
    container.removeAttribute(HasAttributePrefix + name);
}
