// The single place DOM mutations are turned into work, so nothing has to poll for them.
//
// A MutationObserver callback is delivered once per microtask checkpoint - after a render
// batch is applied and before paint - so anything driven from here is both timelier and
// cheaper than an interval, and sees JS-driven mutations no render hook would.
//
// Three consumers today:
//  - presence tracking, which counts data-child elements per data-children container - see
//    presence-tracker.ts for why markup declares it rather than CSS deriving it.
//  - render scripts, run when a data-render-script-<name> attribute appears.
//  - animation phase sync, which used to sweep the whole document every 200ms.

import { AnimationSync } from 'animation-sync';
import { PresenceTracker } from 'presence-tracker';


// Runs when `data-render-script-<name>` appears on an element, or its value changes. The name selects
// the script, so one element can ask for several; the value is the script's argument.
export type RenderScript = (element: HTMLElement, value: string) => void;

const RenderScriptPrefix = 'data-render-script-';

const renderScripts = new Map<string, RenderScript>();
// What each element was last run with, so a re-render that rewrites the same value is not a re-run.
const ranRenderScripts = new WeakMap<Element, Map<string, string>>();
const baseObservedAttributes = ['class', 'data-side-nav', ...PresenceTracker.observedAttributes];
let observedAttributes = [...baseObservedAttributes];
let observer: MutationObserver | null = null;
let isEnabled = true;

export const MutationProcessor = {
    // Runtime toggle so the mechanism can be A/B'd inside one session rather than across builds
    get isEnabled(): boolean {
        return isEnabled;
    },

    set isEnabled(value: boolean) {
        if (isEnabled === value)
            return;

        isEnabled = value;
        if (value)
            PresenceTracker.scan(document.body);
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
        PresenceTracker.scan(document.body);
        runRenderScripts(document.body);
        AnimationSync.syncAll(document);
    },

    stop(): void {
        observer?.disconnect();
        observer = null;
    },

    presence: PresenceTracker,
};

// Private methods

function onMutated(records: MutationRecord[]): void {
    if (!isEnabled)
        return;

    const added = new Set<Element>();
    for (const record of records)
        for (const node of record.addedNodes)
            if (node instanceof Element)
                added.add(node);

    const roots = coalesceRoots(added);
    syncAddedAnimations(roots);

    for (const record of records) {
        if (record.type === 'childList') {
            for (const node of record.addedNodes)
                if (node instanceof Element) {
                    runRenderScripts(node);
                    PresenceTracker.scan(node);
                }
            for (const node of record.removedNodes)
                if (node instanceof Element)
                    PresenceTracker.unscan(node);
        }
        else if (record.attributeName === 'data-child')
            PresenceTracker.onChildChanged(record.target as Element);
        else if (record.attributeName === 'data-children')
            PresenceTracker.onChildrenChanged(record.target as Element);
        else if (isRenderScriptAttribute(record.attributeName))
            runRenderScriptsOn(record.target as Element);
    }
}

// Phase-aligns whatever arrived, replacing the sweep that used to run every 200ms.
// The animationstart listener still covers an element that gains an animation later,
// which no mutation can be matched to.
function syncAddedAnimations(roots: Element[]): void {
    if (roots.length === 0)
        return;

    const elements = new Set<HTMLElement>();
    const selector = AnimationSync.selector;
    for (const root of roots) {
        // querySelectorAll excludes the root, so an added element that is itself
        // animated has to be handled separately.
        if (root.matches(selector))
            elements.add(root as HTMLElement);
        for (const element of root.querySelectorAll<HTMLElement>(selector))
            elements.add(element);
    }
    AnimationSync.syncMany(elements);
}

function coalesceRoots(elements: Set<Element>): Element[] {
    const roots: Element[] = [];
    for (const element of elements) {
        let parent = element.parentElement;
        while (parent !== null && !elements.has(parent))
            parent = parent.parentElement;

        if (parent === null)
            roots.push(element);
    }
    return roots;
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
