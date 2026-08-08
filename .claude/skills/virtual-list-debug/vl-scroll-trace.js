// Per-frame scroll/position tracer for a VirtualList.
// Paste into the page console (or evaluate_script) after debugUI.virtualListDebug(true).
//   __vlTrace.install('')      // '' = sidebar chat list; chat id = chat view
//   __vlTrace.start(); __vlTrace.reset();
//   ... scroll ...
//   __vlTrace.stop(); __vlTrace.report()
//
// Records, every animation frame: scrollTop, scrollHeight, container top, itemRange,
// renderIndex, statistics.itemSize, spacer sizes, the item at the viewport top, and how
// many items cover the viewport (0 => blank screen).
//
// Two jump metrics:
//   px  - for every item present in both frames, drift = dTop + dScrollTop. Non-zero drift
//         means content moved by more than the scroll accounted for (the no-jump violation).
//   idx - change of the top-most visible item index vs. what the scroll delta predicts.
//         Quantized, so treat small values as noise; px drift is the authoritative one.
//
// requestData / rebuildItemRangeFromAnchor / ensureItemRangeCalculated / endRender are
// wrapped so their timing (and scrollTop before/after) lands in the same timeline.

globalThis.__vlTrace = (() => {
    const T = {
        samples: [], jumps: [], events: [],
        t0: performance.now(), identity: null, running: false, rafId: 0, driftEps: 6,
        vl: null,
    };
    let prev = null;

    const now = () => Math.round(performance.now() - T.t0);
    T.ev = (name, extra) => T.events.push({ t: now(), name, ...(extra ?? {}) });

    const patch = (vl, ref, name, tag) => {
        const orig = vl[name] ?? Object.getPrototypeOf(vl)[name];
        if (typeof orig !== 'function')
            return;
        vl[name] = function (...args) {
            const st = ref.scrollTop;
            const result = orig.apply(this, args);
            const rec = { t: now(), name: tag, st: Math.round(st), st2: Math.round(ref.scrollTop) };
            if (name === 'rebuildItemRangeFromAnchor')
                rec.delta = result;
            T.events.push(rec);
            return result;
        };
    };

    const sample = () => {
        const vl = T.vl, ref = vl.ref;
        const st = ref.scrollTop;
        const vr = ref.getBoundingClientRect();
        const items = new Map();
        let topKey = null, topOff = null, cover = 0;
        for (const li of ref.querySelectorAll('li.item[data-key]')) {
            const r = li.getBoundingClientRect();
            items.set(li.dataset.key, r.top);
            if (r.bottom > vr.top && r.top < vr.bottom) {
                cover++;
                if (topKey === null) {
                    topKey = Number(li.dataset.key);
                    topOff = Math.round(r.top - vr.top);
                }
            }
        }
        const ir = vl.state.itemRange;
        const s = {
            t: now(),
            st: Math.round(st * 10) / 10,
            sh: ref.scrollHeight,
            ct: parseFloat(vl.containerRef.style.top) || 0,
            ir: ir ? [Math.round(ir.start), Math.round(ir.end)] : null,
            ri: vl.state.renderState.renderIndex,
            isz: Math.round(vl.statistics.itemSize * 100) / 100,
            n: items.size, cover, topKey, topOff,
            ssp: parseFloat(vl.spacerRef.style.height) || 0,
            esp: parseFloat(vl.endSpacerRef.style.height) || 0,
        };
        if (prev) {
            const dSt = st - prev.st;
            s.dSt = Math.round(dSt * 10) / 10;
            let worst = null, shared = 0;
            for (const [key, top] of items) {
                const before = prev.items.get(key);
                if (before === undefined)
                    continue;
                shared++;
                const drift = (top - before) + dSt;
                if (!worst || Math.abs(drift) > Math.abs(worst.drift))
                    worst = { key, drift };
            }
            s.shared = shared;
            if (worst) {
                s.drift = Math.round(worst.drift * 10) / 10;
                s.dKey = worst.key;
            }
            if (prev.topKey != null && topKey != null && vl.statistics.itemSize > 0) {
                const expected = prev.topKey + dSt / vl.statistics.itemSize;
                s.idxDrift = Math.round((topKey - expected) * 10) / 10;
                if (Math.abs(s.idxDrift) > 2)
                    T.jumps.push({ kind: 'idx', ...s, prevTopKey: prev.topKey, prevT: prev.t });
            }
            if (worst && Math.abs(worst.drift) > T.driftEps)
                T.jumps.push({ kind: 'px', ...s, prevT: prev.t, prevIr: prev.ir, prevRi: prev.ri, prevCt: prev.ct });
        }
        prev = { st, items, t: s.t, ir: s.ir, ri: s.ri, ct: s.ct, topKey };
        T.samples.push(s);
    };

    const loop = () => {
        if (!T.running)
            return;
        sample();
        T.rafId = requestAnimationFrame(loop);
    };

    T.install = (identity = '') => {
        T.stop();
        const dbg = globalThis.__vlDebugs?.[identity];
        if (!dbg)
            throw new Error(`No VirtualListDebug for identity '${identity}'. Run debugUI.virtualListDebug(true) first.`);
        T.identity = identity;
        T.vl = dbg.vl;
        const ref = T.vl.ref;
        const names = ['requestData', 'rebuildItemRangeFromAnchor', 'ensureItemRangeCalculated', 'endRender'];
        const tags = ['requestData', 'rebuild', 'ensureRange', 'endRender'];
        names.forEach((n, i) => patch(T.vl, ref, n, tags[i]));
        return { identity, clientHeight: ref.clientHeight, scrollHeight: ref.scrollHeight };
    };
    T.start = () => {
        if (T.running || !T.vl)
            return;
        T.running = true;
        prev = null;
        T.t0 = performance.now();
        loop();
    };
    T.stop = () => {
        T.running = false;
        cancelAnimationFrame(T.rafId);
    };
    T.reset = () => {
        T.samples.length = 0;
        T.jumps.length = 0;
        T.events.length = 0;
    };

    // Fling with a relative scrollBy so any scrollTop compensation the list applies survives
    // (writing scrollTop absolutely each frame overwrites it and hides real jumps).
    T.fling = (velocity, frames) => new Promise(resolve => {
        const ref = T.vl.ref;
        let i = 0;
        const step = () => {
            if (i++ >= frames)
                return resolve();
            ref.scrollBy(0, velocity);
            requestAnimationFrame(step);
        };
        requestAnimationFrame(step);
    });

    // How much of the wrapper's scroll range no element covers - the blank-screen measure.
    T.coverage = () => {
        const vl = T.vl, ref = vl.ref;
        const ir = vl.state.itemRange;
        const ct = parseFloat(vl.containerRef.style.top) || 0;
        const ssp = parseFloat(vl.spacerRef.style.height) || 0;
        const esp = parseFloat(vl.endSpacerRef.style.height) || 0;
        const wrapperHeight = parseFloat(vl.wrapperRef.style.height) || ref.scrollHeight;
        const coveredUpTo = ct + ssp + (ir ? ir.end - ir.start : 0) + esp;
        return {
            scrollTop: Math.round(ref.scrollTop), clientHeight: ref.clientHeight,
            wrapperHeight, containerTop: ct, startSpacer: ssp, endSpacer: esp,
            itemRange: ir ? [ir.start, ir.end] : null,
            coveredUpTo, uncoveredPx: Math.round(wrapperHeight - coveredUpTo),
            viewportInsideUncovered: ref.scrollTop > coveredUpTo,
        };
    };

    // Scroll that moved with no input in flight. A wrapper shrink that clamps scrollTop moves the
    // content AND the scroll by the same amount, so px drift reads 0 - this is the only metric that
    // catches it, and it is what "the list jumps back" actually looks like.
    T.selfMoves = () => {
        const windows = [];
        let open = null;
        for (const e of T.events) {
            if (e.name === 'fling-start') open = e.t;
            if (e.name === 'fling-end' && open != null) { windows.push([open, e.t]); open = null; }
        }
        const inFling = t => windows.some(([a, b]) => t >= a - 60 && t <= b + 120);
        const groups = [];
        for (const x of T.samples) {
            if (x.dSt == null || Math.abs(x.dSt) <= 2 || inFling(x.t))
                continue;
            const last = groups[groups.length - 1];
            if (last && x.t - last.endT <= 120) {
                last.endT = x.t; last.total += x.dSt; last.frames++;
                last.endSt = x.st; last.endSh = x.sh;
            }
            else {
                groups.push({
                    startT: x.t, endT: x.t, startSt: Math.round(x.st - x.dSt), endSt: x.st,
                    startSh: x.sh, endSh: x.sh, total: x.dSt, frames: 1, ri: x.ri, isz: x.isz,
                });
            }
        }
        return groups.map(g => ({ ...g, total: Math.round(g.total) }))
            .sort((a, b) => Math.abs(b.total) - Math.abs(a.total));
    };

    T.report = () => {
        const s = T.samples;
        const blankRuns = [];
        let run = 0;
        for (const x of s) {
            if (x.cover === 0) run++;
            else { if (run) blankRuns.push(run); run = 0; }
        }
        if (run) blankRuns.push(run);
        const px = T.jumps.filter(j => j.kind === 'px');
        const wrapperChanges = [];
        for (let i = 1; i < s.length; i++)
            if (s[i].sh !== s[i - 1].sh)
                wrapperChanges.push({ t: s[i].t, from: s[i - 1].sh, to: s[i].sh, d: s[i].sh - s[i - 1].sh, st: s[i].st });
        return {
            samples: s.length,
            pxJumps: px.length,
            worstPx: px.slice().sort((a, b) => Math.abs(b.drift) - Math.abs(a.drift)).slice(0, 5),
            selfMoves: T.selfMoves().slice(0, 8),
            itemSizeRange: s.length ? [s[0].isz, s[s.length - 1].isz] : null,
            wrapperChanges: wrapperChanges.length,
            biggestWrapperChanges: wrapperChanges.slice().sort((a, b) => Math.abs(b.d) - Math.abs(a.d)).slice(0, 5),
            blankFrameRuns: blankRuns.sort((a, b) => b - a).slice(0, 6),
            renderIndexRange: s.length ? [s[0].ri, s[s.length - 1].ri] : null,
        };
    };

    return T;
})();
