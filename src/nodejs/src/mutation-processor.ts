// The single place DOM mutations are turned into work, so nothing has to poll for them.
//
// A MutationObserver callback is delivered once per microtask checkpoint - after a render
// batch is applied and before paint - so anything driven from here is both timelier and
// cheaper than an interval, and sees JS-driven mutations no render hook would.
//
// Two consumers today:
//  - presence classes, which replace `container:has(descendant)`. WebKit has no
//    descendant-direction :has() bits, so StyleInvalidator re-runs a real match up the
//    ancestor chain on every mutation - 6-8% of WebContent's main thread during a call.
//  - animation phase sync, which used to sweep the whole document every 200ms.

import { AnimationSync } from 'animation-sync';

export interface PresenceClassRule {
    container: string;
    match: string;
    className: string;
}

const rules = new Array<PresenceClassRule>();
const observedAttributes = ['class', 'data-side-nav'];
// Class tokens any rule's match selector can turn on. Records touching none of them cannot
// change any predicate, which is nearly all of them - this app toggles classes constantly
// for animation and hover state, and a full rescan per toggle is what we're avoiding.
const matchTokens = new Set<string>();
let matchSelector = '';
let observer: MutationObserver | null = null;
let isEnabled = true;

export const MutationProcessor = {
    get presenceClassRules(): readonly PresenceClassRule[] {
        return rules;
    },

    // Runtime toggle so the mechanism can be A/B'd inside one session rather than across builds
    get isEnabled(): boolean {
        return isEnabled;
    },

    set isEnabled(value: boolean) {
        if (isEnabled === value)
            return;

        isEnabled = value;
        if (value)
            updatePresenceClasses();
        else
            clearPresenceClasses();
    },

    // Registered at import time by the modules that own the matching CSS: the CSS applies
    // whenever the markup exists, so registering when a component mounts would leave a
    // window with the class missing.
    registerPresenceClasses(...newRules: PresenceClassRule[]): void {
        rules.push(...newRules);
        matchSelector = rules.map(r => r.match).join(',');
        matchTokens.clear();
        for (const rule of rules)
            for (const token of rule.match.match(/\.[\w-]+/g) ?? [])
                matchTokens.add(token.slice(1));
        if (observer)
            updatePresenceClasses();
    },

    start(): void {
        if (observer)
            return;

        observer = new MutationObserver(onMutated);
        observer.observe(document.body, {
            childList: true,
            subtree: true,
            attributes: true,
            attributeFilter: observedAttributes,
            attributeOldValue: true,
        });
        updatePresenceClasses();
        AnimationSync.syncAll(document);
    },

    stop(): void {
        observer?.disconnect();
        observer = null;
    },

    update: updatePresenceClasses,
};

// Private methods

function onMutated(records: MutationRecord[]): void {
    if (!isEnabled)
        return;

    let hasPresenceChange = false;
    for (const record of records) {
        if (record.type === 'childList')
            syncAddedAnimations(record.addedNodes);
        hasPresenceChange ||= affectsPresence(record);
    }
    if (hasPresenceChange) {
        updatePresenceClasses();
        // Our own class writes are mutations too; they land after this callback, so dropping
        // the records here discards exactly them and nothing else.
        observer?.takeRecords();
    }
}

// Phase-aligns whatever arrived, replacing the sweep that used to run every 200ms.
// The animationstart listener still covers an element that gains an animation later,
// which no mutation can be matched to.
function syncAddedAnimations(nodes: NodeList): void {
    for (const node of nodes) {
        if (!(node instanceof HTMLElement) && !(node instanceof SVGElement))
            continue;

        // querySelectorAll excludes the root, so an added element that is itself
        // animated has to be handled separately.
        if (node.matches(AnimationSync.selector))
            AnimationSync.sync(node as HTMLElement);
        AnimationSync.syncAll(node);
    }
}

function affectsPresence(record: MutationRecord): boolean {
    if (!matchSelector)
        return false;

    if (record.type === 'attributes') {
        if (record.attributeName !== 'class')
            return true;

        // A class change matters only if it added or removed a token some rule tests for.
        const target = record.target as Element;
        const before = new Set((record.oldValue ?? '').split(/\s+/));
        for (const token of matchTokens)
            if (target.classList.contains(token) !== before.has(token))
                return true;

        return false;
    }

    return hasMatch(record.addedNodes) || hasMatch(record.removedNodes);
}

function hasMatch(nodes: NodeList): boolean {
    for (const node of nodes) {
        if (!(node instanceof Element))
            continue;
        // Walks only the added/removed subtree, which is one component's worth of markup -
        // far cheaper than rescanning every container.
        if (node.matches(matchSelector) || node.querySelector(matchSelector))
            return true;
    }

    return false;
}

function updatePresenceClasses(): void {
    for (const rule of rules)
        for (const container of document.querySelectorAll(rule.container))
            container.classList.toggle(rule.className, container.querySelector(rule.match) !== null);
}

function clearPresenceClasses(): void {
    for (const rule of rules)
        for (const container of document.querySelectorAll(rule.container))
            container.classList.remove(rule.className);
}
