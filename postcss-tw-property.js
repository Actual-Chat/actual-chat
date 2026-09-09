// @ts-check
"use strict";
// Tailwind v3 guarantees its composition variables are always defined by declaring all of them on
// every element and pseudo:
//
//   *, ::before, ::after { --tw-translate-x: 0; --tw-blur: ; ... 51 of them }
//
// so that `transform: translate(var(--tw-translate-x), ...) rotate(var(--tw-rotate)) ...` stays
// valid when only one utility is used. WebKit applies every one of those to every element whose
// style it resolves, and this app builds ~1500 fresh elements per navbar switch, so the block
// measures ~30-40ms per switch on an iPhone 13 Pro.
//
// This moves the defaults off the elements: each name is registered non-inheriting with no initial
// value, and every reference carries the default as a var() fallback. An unset property is then
// guaranteed-invalid, so the fallback applies - which is what the declaration used to do - and
// `inherits: false` keeps a parent's value from reaching children, which is what re-declaring on
// every element used to do.
//
// initial-value is deliberately not used: two thirds of Tailwind's defaults are the empty
// "space toggle" (`--tw-blur: ;`), and @property cannot express an empty initial value, whereas
// `var(--tw-blur, )` can.
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
