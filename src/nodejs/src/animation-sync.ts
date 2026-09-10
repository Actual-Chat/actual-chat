// Phase-aligns looping CSS animations so they tick on shared instants.
//
// Cost per rendering update is dominated by a fixed overhead, not by how many
// elements changed: measured on an iPhone 13 Pro, 8 unsynchronised looping
// animations cost +0.45 cores over idle and 40 cost +0.59 - but the same 40
// phase-aligned cost +0.08. Left alone every animation starts when its element
// appears, so with enough of them nearly every frame contains a tick and that
// fixed cost is paid ~60x/s.
//
// Alignment works by back-dating `animation-delay`, so an element is in phase
// the moment it appears - nothing is deferred and nothing needs re-checking.
// The modulus is the TICK duration, not the animation duration: that is what
// lets a 2s and a 1.5s animation share instants, provided each one's
// duration/steps lands on the same grid.
//
// Only stepped animations benefit. A continuous one changes every frame no
// matter its phase, so `sync` warns rather than pretending to help.

import { fastRaf } from 'fast-raf';


const defaultTickMs = 100;
// What collecting the animations may cost before the sweep is spaced out at all. Measured on
// a 4275-element page: 0.2ms healthy, against 137ms for the population that wedged the app.
const thresholdMs = 5;
// Share of wall time a sweep past the threshold may take: it then sleeps cost/ratio before
// trying again, so the share stays flat however bad the population gets.
const maxDutyRatio = 0.03;
// Ceiling on that spacing: even a hopeless population is retried once a minute.
const minScheduleHz = 1 / 60;
// Samples averaged. Short, so it reacts within a few sweeps in both directions.
const costWindow = 4;
// Every tick has to be a multiple of this, or two animations can never share an
// instant. `fastRaf10` schedules onto the same grid.
export const animationGridMs = 100;

// The pseudo-element an animation lives on, where it isn't the element itself. This
// is the one thing a registration has to state: a pseudo's animation is invisible in
// the host's computed style, so it can't be derived, and pseudos can't take the inline
// style the phase is published through.
const pseudoByClass = new Map<string, string>([
    ['recorder-wrapper', '::before'],       // rotate-ring
    ['streaming-entry-badge', '::before'],  // badge-shimmer
    ['author-circle', '::before'],          // rotate-border-gradient
    ['layout-subfooter', '::before'],       // bottomWave
    ['c-film-strip', '::after'],            // film-scroll
    ['video-track-player', '::after'],      // vpSpeakingPulse
]);

// Every class swept. All align to the grid, and a modulus finer than an animation's
// own tick is always safe, so no entry needs to state one. Co-located deliberately:
// this is the one place to see every synced animation.
const animationClasses = new Set<string>([
    ...pseudoByClass.keys(),
    // Audio / recording UI - these run during a call, which is what this is for.
    'record-on-btn',            // record-btn-on-pulse
    'placeholder-circle',       // placeholder-wave
    'bar',                      // equalize-*
    'swap-a',                   // timer-swap - both halves animate, so both need phasing
    'swap-b',                   // timer-swap
    'notify-mentioned-members', // shimmer
    'upload-plug',              // plugPulse
    // Video panel
    'rec-btn',                  // bgShake / record-btn-on-pulse
    'connecting-spinner',       // spin
    'c-live-dot',               // pulseLiveDot
    'c-title-inner',            // c-title-marquee
    // Skeletons + unread dots
    'animate-pulse',
    'animated-skeleton',
    'button',                   // thin-left-panel-skeleton .button
    'footer-button',            // chat-view-footer-skeleton .footer-button
    'c-line',                   // tab-skeleton .c-line
    'recorder-btn-skeleton',
    'c-circle',                 // wave
    'string-skeleton',
    'voxt-skeleton',
]);

// Animated elements a class can't identify. Same registry role as `animationClasses`
// above — kept beside it so this file remains the one place listing every synced
// animation.
const animationSelectors: readonly string[] = [
    'image-skeleton[data-image-state="skeleton"]', // skeleton shimmer
];

// An element's pending phase: the delay to publish, or null when nothing is animating yet,
// plus the tick it was resolved against. Split from the write so a caller inside a frame can
// read in its read phase and commit in its write phase.
type PhaseWrite = [HTMLElement, string | null, number];

export class AnimationSync {
    // Explicit opt-in for elements with no class in the registry - notably nodes
    // inside a shadow root, which are often bare <path>/<rect>. Bare is a complete
    // declaration; a value is only needed to name a pseudo-element, or to ask for a
    // coarser modulus than the grid: data-anim-sync="::before", "200", "200 ::before".
    public static readonly attribute = 'data-anim-sync';
    // Set once synced, so sweeps skip the element afterwards. Its value is the
    // tick that was applied, which is how an attribute-declared override outlives
    // the opt-in attribute itself.
    public static readonly syncedAttribute = 'data-anim-synced';

    public static get selector(): string {
        const byClass = [...animationClasses].map(c => `.${c}`).join(',');
        const bySelector = animationSelectors.join(',');
        return `:is(${byClass},${bySelector},[${AnimationSync.attribute}]):not([${AnimationSync.syncedAttribute}])`;
    }

    // Phase-aligns every registered element under `root`, returning how many were
    // changed. Cheap to call repeatedly - synced elements are skipped, and an
    // empty sweep of a ~750-element tree measured 11.5us on an iPhone 13 Pro.
    // Pass a shadow root for Lit components: their animated nodes are invisible
    // to a document-level sweep.
    public static syncAll(root: ParentNode | null | undefined): number {
        if (root == null)
            return 0;

        const elements = root.querySelectorAll<HTMLElement>(AnimationSync.selector);

        return AnimationSync.syncMany(elements);
    }

    public static syncMany(elements: Iterable<HTMLElement>): number {
        // Read every phase before writing any, so a batch cannot interleave
        // style reads and writes.
        const writes = AnimationSync.costMs > thresholdMs
            ? AnimationSync.markOnly(elements)
            : AnimationSync.readPhases(elements);
        for (const [element, delay, tickMs] of writes)
            AnimationSync.commit(element, delay, tickMs);
        return writes.length;
    }

    /** Single-element form for one JS-created node, which must already be in a document or
     *  shadow tree. Never in a loop: each call sorts the whole tree scope - syncMany sorts once. */
    public static sync(element: HTMLElement): void {
        const running = AnimationSync.runningByTarget([element]);
        const [, delay, tickMs] = AnimationSync.phaseOf(element, running);
        AnimationSync.commit(element, delay, tickMs);
    }

    // Re-phases an element whose animation has just begun, which is how an element
    // resolved while carrying no animation gets picked up when a class change later
    // gives it one. Also covers a re-add: the stale delay from the previous cycle is
    // still on the element, so leaving it alone would resume in an arbitrary phase.
    //
    // Deferred to the frame rather than done here, because this path carries far more
    // than it reads like: an element added by a render has a pending animation whose
    // startTime is still null when MutationProcessor sweeps it, so it yields no phase
    // there and is aligned from here instead. Phasing each event on its own cost one
    // document-wide collection per element - 50 elements measured 51 against 2 batched.
    public static scheduleStarted(element: HTMLElement): void {
        const isManaged = element.hasAttribute(AnimationSync.syncedAttribute)
            || element.hasAttribute(AnimationSync.attribute)
            || AnimationSync.isRegistered(element);
        if (!isManaged)
            return;

        AnimationSync.started.add(element);
        // The collection is a read and the phase is a write, so each goes in the frame's
        // matching half - a write between two reads is the inversion fast-raf exists to
        // prevent, and every element committed here dirties an inherited custom property.
        fastRaf({
            read: AnimationSync.readStarted,
            write: AnimationSync.commitStarted,
            key: 'animation-sync-started',
            hz: AnimationSync.scheduleHz(),
        });
    }

    public static cancelStarted(): void {
        AnimationSync.started.clear();
        AnimationSync.startedWrites = [];
    }

    // Private methods

    private static readonly started = new Set<HTMLElement>();
    private static startedWrites: readonly PhaseWrite[] = [];
    private static readonly costs: number[] = [];
    private static costMs = 0;
    private static lastCollected = 0;
    private static hasReportedCost = false;
    private static throttleHz: number | undefined = undefined;
    private static readonly readStarted = (): void => {
        const elements = [...AnimationSync.started];
        AnimationSync.started.clear();
        AnimationSync.startedWrites = AnimationSync.readPhases(elements);
    };
    private static readonly commitStarted = (): void => {
        const writes = AnimationSync.startedWrites;
        AnimationSync.startedWrites = [];
        for (const [element, delay, tickMs] of writes)
            AnimationSync.commit(element, delay, tickMs);
    };

    /** Materialized because the lookup and the phase loop both walk it; a per-element catch
     *  because fast-raf does not guard its loops - a throw would drop the frame's other writes. */
    private static readPhases(elements: Iterable<HTMLElement>): PhaseWrite[] {
        const targets = [...elements];
        const running = AnimationSync.runningByTarget(targets);
        return targets.map(element => {
            try {
                return AnimationSync.phaseOf(element, running);
            } catch (error) {
                AnimationSync.warn(element, `could not be phased: ${error as string}`);
                return [element, null, defaultTickMs] as PhaseWrite;
            }
        });
    }

    /** Over budget this path stops collecting and only marks, leaving the phase to the
     *  throttled flush - which is also the one probe that can see the cost come back down. */
    private static markOnly(elements: Iterable<HTMLElement>): PhaseWrite[] {
        return [...elements].map(element => [element, null, AnimationSync.declared(element)[0]]);
    }

    /** Mean rather than last, so one slow frame cannot pin the rate down and one fast one
     *  cannot lift it back before the population has actually shrunk. */
    private static observeCost(elapsedMs: number, collected: number): void {
        AnimationSync.costs.push(elapsedMs);
        if (AnimationSync.costs.length > costWindow)
            AnimationSync.costs.shift();

        const total = AnimationSync.costs.reduce((sum, cost) => sum + cost, 0);
        AnimationSync.costMs = total / AnimationSync.costs.length;
        AnimationSync.updateSchedule();
        if (AnimationSync.throttleHz === undefined || AnimationSync.hasReportedCost)
            return;

        AnimationSync.hasReportedCost = true;
        console.warn(`AnimationSync: collecting ${collected} animations averages `
            + `${AnimationSync.costMs.toFixed(1)}ms, over the ${thresholdMs}ms threshold - phase `
            + `alignment is now spaced to ${(1000 / AnimationSync.throttleHz).toFixed(0)}ms `
            + `to hold it under ${(maxDutyRatio * 100).toFixed(0)}% of the time.`);
    }

    /** Held constant while throttled, and re-rated only on a large move: fast-raf buckets by
     *  rate, so a rate that drifted every sweep would arm a second bucket beside the first. */
    private static updateSchedule(): void {
        if (AnimationSync.costMs <= thresholdMs) {
            AnimationSync.throttleHz = undefined;
            return;
        }

        // Sleep cost/ratio before retrying, which is the rate that holds the share at the budget.
        const hz = Math.max(minScheduleHz, 1000 * maxDutyRatio / AnimationSync.costMs);
        const current = AnimationSync.throttleHz;
        if (current === undefined || hz > current * 2 || hz < current / 2)
            AnimationSync.throttleHz = hz;
    }

    /** undefined means the next frame. Above the budget the flush is spaced so that
     *  cost/period stays inside it, which is hz = 1000 / (10 * cost). */
    private static scheduleHz(): number | undefined {
        return AnimationSync.throttleHz;
    }

    // A null delay means nothing is animating yet, which is normal - the element is still
    // marked resolved so sweeps skip it, and `animationstart` re-phases it once one begins.
    private static commit(element: HTMLElement, delay: string | null, tickMs: number): void {
        if (delay !== null) {
            element.style.animationDelay = delay;
            // ::before/::after cannot take an inline style, so the phase is published
            // as a custom property they inherit; their rules opt in with
            // `animation-delay: var(--anim-phase, 0s)`.
            element.style.setProperty('--anim-phase', delay);
        }
        element.removeAttribute(AnimationSync.attribute);
        element.setAttribute(AnimationSync.syncedAttribute, tickMs.toFixed(0));
    }

    // The phase has to come from the animation's own startTime, not from the clock: its
    // ticks fall at startTime + delay + k*tick, and a sweep almost always reaches an element
    // long after its animation began - on a class change, a re-render, or an animationstart.
    // Aligning to `performance.now()` therefore pinned each element to whenever it happened
    // to be swept, which left the boundaries scattered across the grid instead of on it
    // (measured in Chrome: 26.0, 71.0, 76.7, 82.4, 91.3, 91.7ms - all reported as synced).
    //
    // Correcting the animation's own delay by its own offset is also idempotent: an aligned
    // animation has offset 0 and keeps the delay it has. That is what makes re-syncing safe,
    // and it removes the need to fold an authored stagger in - the stagger is already part
    // of startTime + delay, so it survives on its own.
    private static phaseOf(
        element: HTMLElement,
        running: Map<Element, Animation[]>,
    ): PhaseWrite {
        const [tickMs, pseudo] = AnimationSync.declared(element);
        AnimationSync.validate(element, getComputedStyle(element, pseudo ?? null), pseudo);
        const animation = AnimationSync.animationOf(element, pseudo, running);
        if (animation === undefined)
            return [element, null, tickMs];

        const delay = animation.effect?.getTiming().delay ?? 0;
        const startTime = animation.startTime;
        if (typeof delay !== 'number' || typeof startTime !== 'number')
            return [element, null, tickMs];

        const offset = (((startTime + delay) % tickMs) + tickMs) % tickMs;
        return [element, `${(delay - offset).toFixed(0)}ms`, tickMs];
    }

    private static animationOf(
        element: HTMLElement,
        pseudo: string | undefined,
        running: Map<Element, Animation[]>,
    ): Animation | undefined {
        const animations = (running.get(element) ?? []).filter(a =>
            a.effect instanceof KeyframeEffect
            && (a.effect.pseudoElement ?? undefined) === pseudo);
        // A one-shot running alongside the loop - a fade-in, say - has no phase worth fixing,
        // and on a shared element it would otherwise be the one that got aligned.
        return animations.find(a => a.effect?.getComputedTiming().iterations === Infinity)
            ?? animations[0];
    }

    // One call per tree scope, not per element: getAnimations() collects and sorts every
    // animation in the scope before filtering by target, so calling it per element costs
    // O(elements x animations log animations) - 4448 animations made one call 137ms, and a
    // 50-element batch 6.9s against 134ms for this. Shadow roots need their own call;
    // document.getAnimations() does not cross into them.
    private static runningByTarget(elements: readonly HTMLElement[]): Map<Element, Animation[]> {
        const roots = new Set<Document | ShadowRoot>();
        for (const element of elements) {
            const root = element.getRootNode();
            if (root instanceof Document || root instanceof ShadowRoot)
                roots.add(root);
        }

        const byTarget = new Map<Element, Animation[]>();
        let collected = 0;
        const startedAt = performance.now();
        for (const root of roots) {
            const all = root.getAnimations();
            collected += all.length;
            for (const animation of all) {
                if (animation.playState !== 'running')
                    continue;
                if (!(animation.effect instanceof KeyframeEffect))
                    continue;

                const target = animation.effect.target;
                if (target === null)
                    continue;

                const animations = byTarget.get(target);
                if (animations === undefined)
                    byTarget.set(target, [animation]);
                else
                    animations.push(animation);
            }
        }
        AnimationSync.lastCollected = collected;
        // Only a sweep that actually collected says anything about the cost: an empty batch
        // would otherwise feed zeroes into the average and un-trip the breaker on an artifact.
        if (roots.size !== 0)
            AnimationSync.observeCost(performance.now() - startedAt, collected);

        return byTarget;
    }

    // An authored stagger needs no check any more: the phase is corrected from the
    // animation's own boundary, so a stagger that isn't a whole number of grid steps is
    // snapped onto the grid rather than left off it. Relative staggers that are multiples
    // of the tick survive exactly, which is what makes a wave across several bars still read
    // as a wave.
    private static validate(
        element: HTMLElement,
        style: CSSStyleDeclaration,
        pseudo: string | undefined,
    ): void {
        const duration = AnimationSync.parseMs(style.getPropertyValue('animation-duration'));
        if (duration <= 0) {
            // Nothing animating anywhere is the normal idle state - .recorder-wrapper
            // has no ring to rotate until recording starts, and `animationstart` picks
            // it up if and when one begins. Only the element animating while its
            // declared pseudo doesn't is a real mistake: the registration points at
            // the wrong target.
            const ownDuration = getComputedStyle(element).getPropertyValue('animation-duration');
            if (pseudo && AnimationSync.parseMs(ownDuration) > 0)
                AnimationSync.warn(element, `declares ${pseudo}, but the animation is on the element itself`);

            return;
        }

        const timingFunction = style.getPropertyValue('animation-timing-function');
        // A shorthand list means several animations on one element, and there is no
        // way to tell which entry is the one being aligned - so validate nothing.
        if (timingFunction.includes(','))
            return;

        const steps = AnimationSync.parseSteps(timingFunction);
        if (steps === null) {
            AnimationSync.warn(element, 'is not stepped, so aligning its phase cannot help - it changes every frame');
            return;
        }

        const tick = duration / steps;
        if (Math.abs(tick % animationGridMs) > 0.5)
            AnimationSync.warn(element,
                `ticks every ${tick.toFixed(1)}ms (${duration}ms / steps(${steps})), `
                + `which is not a multiple of the ${animationGridMs}ms grid`);
    }

    // Attribute form: bare, `"::before"`, `"200"`, or `"200 ::before"`. It wins over
    // the registry, which is the point of the explicit opt-in. The tick defaults to
    // the grid: a modulus finer than an animation's own tick keeps it on the grid
    // either way, so only a deliberately coarser one has to be stated.
    private static declared(element: HTMLElement): readonly [number, string?] {
        const attribute = element.getAttribute(AnimationSync.attribute)
            ?? element.getAttribute(AnimationSync.syncedAttribute);
        const tickMs = attribute === null ? Number.NaN : Number.parseFloat(attribute);
        // The class lookup still runs when the attribute names no pseudo: `commit` only
        // records the tick, so a re-sync would otherwise lose a registered pseudo and
        // read the host's computed style instead of the one that animates.
        return [
            Number.isFinite(tickMs) && tickMs > 0 ? tickMs : defaultTickMs,
            (attribute === null ? undefined : /::[\w-]+/.exec(attribute)?.[0])
                ?? AnimationSync.pseudoOfClasses(element),
        ];
    }

    private static isRegistered(element: HTMLElement): boolean {
        for (const className of element.classList)
            if (animationClasses.has(className))
                return true;

        return animationSelectors.some(s => element.matches(s));
    }

    private static pseudoOfClasses(element: HTMLElement): string | undefined {
        let found: string | undefined;
        for (const className of element.classList) {
            const pseudo = pseudoByClass.get(className);
            if (pseudo === undefined || pseudo === found)
                continue;
            if (found !== undefined) {
                AnimationSync.warn(element,
                    `matches registered classes with different pseudo-elements (${found}, ${pseudo})`);
                break;

            }
            found = pseudo;
        }
        return found;
    }

    private static warn(element: HTMLElement, message: string): void {
        const name = element.tagName.toLowerCase()
            + (typeof element.className === 'string' && element.className
                ? `.${element.className.trim().split(/\s+/).join('.')}`
                : '');
        console.warn(`AnimationSync: ${name} ${message}`);
    }

    private static parseSteps(timingFunction: string): number | null {
        const match = /^steps\((\d+)/.exec(timingFunction.split(',')[0]?.trim() ?? '');
        return match ? Number.parseInt(match[1], 10) : null;
    }

    // Only the first value matters: these are single-animation elements, and a
    // comma-separated list would mean the caller wants per-animation control.
    private static parseMs(value: string): number {
        const first = value.split(',')[0]?.trim() ?? '';
        if (first.endsWith('ms'))
            return Number.parseFloat(first) || 0;
        if (first.endsWith('s'))
            return (Number.parseFloat(first) || 0) * 1000;

        return 0;
    }
}

// Sweeps the document for un-synced elements. Blazor and plain-JS code never
// has to call anything: an element only has to carry a registered class. Lit
// components still call syncAll(this.renderRoot) themselves - querySelectorAll
// cannot cross a shadow boundary.
//
// 5Hz costs ~0.006% of a core (11.5us per empty sweep, measured on an iPhone 13
// Pro), which is far cheaper than the JS interop the alternative would need.
export class AnimationSweeper {
    private static readonly animationStartListener = (e: AnimationEvent): void => {
        // Not e.target: that is retargeted to the shadow host for an animation
        // inside a shadow root, and the host is not the element that animates.
        const target = e.composedPath()[0];
        if (target instanceof Element)
            AnimationSync.scheduleStarted(target as HTMLElement);
    };

    public static start(): void {
        // Elements are phase-aligned as they arrive - see MutationProcessor - so there is
        // no periodic sweep. This listener covers the case no mutation can be matched to:
        // an element resolved without an animation that a later class change gives one.
        // Re-phasing does not restart an animation - it moves its start time - so this
        // cannot loop.
        document.removeEventListener('animationstart', AnimationSweeper.animationStartListener);
        document.addEventListener('animationstart', AnimationSweeper.animationStartListener);
    }

    public static stop(): void {
        document.removeEventListener('animationstart', AnimationSweeper.animationStartListener);
        AnimationSync.cancelStarted();
    }
}
