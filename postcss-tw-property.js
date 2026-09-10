// @ts-check
"use strict";
// Moves Tailwind v3's composition defaults off every element.
//
// Tailwind builds one CSS property out of several independent utilities, so `rotate-90` alone has
// to leave the other five transform variables defined - an unresolvable var() with no fallback
// makes the whole declaration invalid at computed-value time, and `transform` would be dropped
// rather than partially applied. v3 guarantees that by declaring all 51 variables on every element
// and pseudo. WebKit builds a per-element custom-property map for every element whose style it
// resolves, and a navbar switch in this app creates ~1500 fresh elements, so that block is the
// single largest CSS-side cost in the profile.
//
// Before - 51 declarations landing on every element, twice more for the pseudos:
//
//   *, ::before, ::after { --tw-translate-x: 0; --tw-rotate: 0; --tw-blur: ; ...48 more }
//   ::backdrop           { ...the same 51 }
//   .rotate-180 { --tw-rotate: 180deg;
//                 transform: translate(var(--tw-translate-x), var(--tw-translate-y))
//                            rotate(var(--tw-rotate)) ... scaleX(var(--tw-scale-x)) ... }
//   .blur       { --tw-blur: blur(8px); filter: var(--tw-blur) var(--tw-brightness) ... }
//
// After - the defaults live in the property registry and at the point of use, nowhere on elements:
//
//   @property --tw-rotate { syntax: "*"; inherits: false; }
//   .rotate-180 { --tw-rotate: 180deg;
//                 transform: translate(var(--tw-translate-x, 0), var(--tw-translate-y, 0))
//                            rotate(var(--tw-rotate, 0)) ... scaleX(var(--tw-scale-x, 1)) ... }
//   .blur       { --tw-blur: blur(8px);
//                 filter: var(--tw-blur, ) var(--tw-brightness, ) var(--tw-contrast, ) ... }
//
// The fallback does what the declaration did; `inherits: false` does what re-declaring on every
// element did, keeping a parent's --tw-blur from reaching its children. Registering with no
// initial-value leaves an unset property guaranteed-invalid, which is exactly what makes the
// fallback apply. initial-value is unusable here: 34 of the 51 defaults are Tailwind's empty
// "space toggle" (`--tw-blur: ;`), and @property cannot express an empty initial value.
//
// On this bundle: 52 names found, 41 registered, 11 that no emitted utility reads dropped
// outright, 444 declarations rewritten. Measured on an iPhone 13 Pro over a navbar switch, mean
// stall 188 -> 119ms and median 202 -> 138ms; in the Time Profiler, Document::resolveStyle falls
// from 63% to 40% of WebContent main-thread CPU, applyMatchedProperties from 31% to 8%,
// custom-property self time from 893ms to 52ms, and applyCustomPropertyImpl - the hottest symbol
// in the whole profile before - leaves the top 30 entirely. Verified by replaying the transform on
// the live CSSOM of the running app: 4238 elements x 19 properties, zero computed-style diffs.
const UniversalSelectors = new Set(['*', '::before', ':before', '::after', ':after', '::backdrop', '::-ms-backdrop']);
const Prefix = '--tw-';

function isUniversal(rule) {
  if (rule.type !== 'rule')
    return false;
  const parts = rule.selector.split(',').map(s => s.trim()).filter(Boolean);
  return parts.length !== 0 && parts.every(s => UniversalSelectors.has(s));
}

// Adds `fallback` to every var(--name) that does not already carry one.
function addFallbacks(value, defaults) {
  let out = '';
  let i = 0;
  while (i < value.length) {
    const at = value.indexOf('var(', i);
    if (at < 0) {
      out += value.slice(i);
      break;
    }
    out += value.slice(i, at);
    let depth = 0, j = at + 3;
    for (; j < value.length; j++) {
      const c = value[j];
      if (c === '(') depth++;
      else if (c === ')') { depth--; if (depth === 0) break; }
    }
    if (j >= value.length) {
      out += value.slice(at);
      break;
    }
    const inner = value.slice(at + 4, j);
    const comma = inner.indexOf(',');
    const name = (comma < 0 ? inner : inner.slice(0, comma)).trim();
    if (comma < 0 && defaults.has(name))
      out += 'var(' + name + ', ' + defaults.get(name) + ')';
    else if (comma < 0)
      out += 'var(' + name + ')';
    else
      out += 'var(' + name + ', ' + addFallbacks(inner.slice(comma + 1), defaults) + ')';
    i = j + 1;
  }
  return out;
}

const plugin = () => ({
  postcssPlugin: 'tw-property',
  OnceExit(root, { result, postcss }) {
    if (process.env.AC_TW_PROPERTY === '0')
      return;

    const defaults = new Map();
    const universalDecls = [];
    root.walkRules(rule => {
      if (!isUniversal(rule))
        return;
      for (const node of rule.nodes) {
        if (node.type !== 'decl' || !node.prop.startsWith(Prefix))
          continue;
        // Later blocks repeat the same names with the same values; first one wins, and a
        // disagreement means the assumption behind this pass is wrong.
        const value = node.value.trim();
        if (defaults.has(node.prop) && defaults.get(node.prop) !== value)
          throw rule.error(`${node.prop} has conflicting universal defaults: `
            + `'${defaults.get(node.prop)}' and '${value}'`);
        defaults.set(node.prop, value);
        universalDecls.push(node);
      }
    });
    if (defaults.size === 0)
      return;

    // Only names something actually reads are worth registering; the rest just go.
    const referenced = new Set();
    root.walkDecls(decl => {
      if (universalDecls.includes(decl))
        return;
      for (const m of decl.value.matchAll(/var\(\s*(--[\w-]+)/g))
        referenced.add(m[1]);
    });

    let rewritten = 0;
    root.walkDecls(decl => {
      if (!decl.value.includes('var(') || universalDecls.includes(decl))
        return;
      const next = addFallbacks(decl.value, defaults);
      if (next !== decl.value) {
        decl.value = next;
        rewritten++;
      }
    });

    for (const decl of universalDecls) {
      const rule = decl.parent;
      decl.remove();
      if (rule.nodes.length === 0)
        rule.remove();
    }

    let registered = 0;
    for (const [name] of defaults) {
      if (!referenced.has(name))
        continue;
      root.prepend(postcss.atRule({
        name: 'property',
        params: name,
        nodes: [
          postcss.decl({ prop: 'syntax', value: '"*"' }),
          postcss.decl({ prop: 'inherits', value: 'false' }),
        ],
      }));
      registered++;
    }

    const dropped = defaults.size - registered;
    result.messages.push({
      type: 'tw-property',
      plugin: 'tw-property',
      text: `registered ${registered} composition properties, dropped ${dropped} unread, `
        + `rewrote ${rewritten} declarations`,
    });
    if (process.env.AC_TW_PROPERTY_LOG === '1')
      console.log(`[tw-property] ${registered} registered, ${dropped} unread dropped, `
        + `${rewritten} declarations rewritten`);
  },
});
plugin.postcss = true;
module.exports = plugin;
