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

// Runs when `data-render-script-<name>` appears on an element, or its value changes. The name selects
// the script, so one element can ask for several; the value is the script's argument.
export type RenderScript = (element: HTMLElement, value: string) => void;

const RenderScriptPrefix = 'data-render-script-';

const rules = new Array<PresenceClassRule>();
const renderScripts = new Map<string, RenderScript>();
// What each element was last run with, so a re-render that rewrites the same value is not a re-run.
const ranRenderScripts = new WeakMap<Element, Map<string, string>>();
const baseObservedAttributes = ['class', 'data-side-nav'];
let observedAttributes = [...baseObservedAttributes];
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
        // The pre-filter tests added/removed nodes directly, where a leading `:scope >` would
        // scope the selector to the node itself and so never match. Dropping it widens the
        // check, which is the safe direction for a filter that only decides whether to work.
        matchSelector = rules.map(r => r.match.replace(/^:scope\s*>\s*/, '')).join(',');
        matchTokens.clear();
        for (const rule of rules) {
            for (const token of rule.match.match(/\.[\w-]+/g) ?? [])
                matchTokens.add(token.slice(1));
            // A Blazor re-render that rewrites `class` drops whatever we wrote there, and
            // nothing else would tell us: the tokens the rule matches on didn't move. Watching
            // our own class makes that a change we see. Our writes land after this callback
            // and takeRecords() discards them, so this cannot feed back on itself.
            matchTokens.add(rule.className);
        }
        if (observer)
            updatePresenceClasses();
    },

    // Registered at import time, like presence classes: the attribute can be in the very first render.
    registerRenderScript(name: string, script: RenderScript): void {
        renderScripts.set(name, script);
        // attributeFilter is fixed when observe() is called, and there is no prefix form of it - so a
        // new name means re-observing. Observing every attribute instead would hand this callback the
        // app's whole class churn, which is the cost this module exists to avoid.
        observedAttributes = [...baseObservedAttributes, ...[...renderScripts.keys()].map(toAttribute)];
        if (observer) {
            reobserve();
            runRenderScripts(document.body);
        }
    },

    start(): void {
        if (observer)
            return;

        observer = new MutationObserver(onMutated);
        reobserve();
        updatePresenceClasses();
        runRenderScripts(document.body);
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

    const dirty = new Set<Element>();
    for (const record of records) {
        if (record.type === 'childList') {
            syncAddedAnimations(record.addedNodes);
            for (const node of record.addedNodes)
                if (node instanceof Element)
                    runRenderScripts(node);
        }
        else if (isRenderScriptAttribute(record.attributeName))
            runRenderScriptsOn(record.target as Element);
        if (affectsPresence(record))
            collectContainers(record, dirty);
    }
    if (dirty.size !== 0) {
        updateContainers(dirty);
        // Our own class writes are mutations too; they land after this callback, so dropping
        // the records here discards exactly them and nothing else.
        observer?.takeRecords();
    }
}

// Only containers on the mutated node's ancestor chain can have flipped, so a mutation
// costs O(rules) rather than O(rules x containers) - the difference between a handful of
// container subjects and every `.item` in the chat view.
function collectContainers(record: MutationRecord, dirty: Set<Element>): void {
    const target = record.target instanceof Element ? record.target : record.target.parentElement;
    if (target !== null)
        for (const rule of rules) {
            const container = target.closest(rule.container);
            if (container !== null)
                dirty.add(container);
        }
    if (record.type !== 'childList')
        return;

    // An added subtree can bring its own containers with it, and those are not on the
    // ancestor chain of anything.
    for (const node of record.addedNodes) {
        if (!(node instanceof Element))
            continue;

        for (const rule of rules) {
            if (node.matches(rule.container))
                dirty.add(node);
            for (const container of node.querySelectorAll(rule.container))
                dirty.add(container);
        }
    }
}

function updateContainers(containers: Set<Element>): void {
    for (const container of containers)
        for (const rule of rules)
            if (container.matches(rule.container))
                container.classList.toggle(rule.className, container.querySelector(rule.match) !== null);
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

function toAttribute(name: string): string {
    return RenderScriptPrefix + name;
}

function isRenderScriptAttribute(attributeName: string | null): boolean {
    return attributeName?.startsWith(RenderScriptPrefix) ?? false;
}

function reobserve(): void {
    observer?.disconnect();
    observer?.observe(document.body, {
        childList: true,
        subtree: true,
        attributes: true,
        attributeFilter: observedAttributes,
        attributeOldValue: true,
    });
}

// `root` included, not just its descendants: an added element can carry the attribute itself.
function runRenderScripts(root: Element): void {
    if (renderScripts.size === 0)
        return;

    for (const name of renderScripts.keys()) {
        const selector = `[${toAttribute(name)}]`;
        if (root.matches(selector))
            runRenderScript(root, name);
        for (const element of root.querySelectorAll(selector))
            runRenderScript(element, name);
    }
}

function runRenderScriptsOn(element: Element): void {
    for (const name of renderScripts.keys())
        if (element.hasAttribute(toAttribute(name)))
            runRenderScript(element, name);
}

function runRenderScript(element: Element, name: string): void {
    const value = element.getAttribute(toAttribute(name));
    if (value === null)
        return;

    let ran = ranRenderScripts.get(element);
    if (ran == null) {
        ran = new Map<string, string>();
        ranRenderScripts.set(element, ran);
    }
    if (ran.get(name) === value)
        return;

    ran.set(name, value);
    renderScripts.get(name)?.(element as HTMLElement, value);
}

function affectsPresence(record: MutationRecord): boolean {
    if (!matchSelector)
        return false;

    if (record.type === 'attributes') {
        if (isRenderScriptAttribute(record.attributeName))
            return false;
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
