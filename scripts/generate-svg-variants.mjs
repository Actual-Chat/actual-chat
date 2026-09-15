import { readFile, readdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { optimize } from 'svgo';
import postcss from 'postcss';

const projectRoot = fileURLToPath(new URL('../', import.meta.url));
const paletteNamespace = 'urn:voxt:svg-themes';
const paintProperties = new Set(['fill', 'stroke', 'stop-color', 'flood-color', 'lighting-color', 'color']);

export function remapSvg(source) {
    return (
        optimize(source, {
            plugins: [{ name: 'theme-palette', fn: transform }],
        }).data + '\n'
    );

    function transform(root) {
        const svg = root.children.find(node => node.type === 'element' && node.name === 'svg');
        const metadata = svg?.children.filter(node => node.name === 'metadata') ?? [];
        const palettes = metadata
            .flatMap(node => node.children)
            .filter(node => node.name === 'palette' && node.attributes.xmlns === paletteNamespace);
        if (palettes.length !== 1) throw new Error('Expected exactly one SVG palette metadata element.');
        const palette = palettes[0];
        if (palette.attributes.version !== '1')
            throw new Error(`Unsupported palette version: ${palette.attributes.version}`);
        const rules = new Map();
        for (const node of palette.children.filter(node => node.type === 'element')) {
            const { light, dark, part, target } = node.attributes;
            if (node.name !== 'color' || (part && target))
                throw new Error('Palette rules must be colors with at most one part or target.');
            const key = `${target ? `#${target}` : part ? `@${part}` : ''}:${normalizeColor(light)}`;
            if (rules.has(key)) throw new Error(`Duplicate palette rule: ${key}`);
            rules.set(key, normalizeColor(dark));
        }
        for (const node of metadata) {
            node.children = node.children.filter(child => child !== palette);
            if (node.children.length === 0) svg.children = svg.children.filter(child => child !== node);
        }
        const seenScopes = new Set();
        visit(svg, []);
        for (const key of rules.keys()) {
            const scope = key.slice(0, key.lastIndexOf(':'));
            if (scope && !seenScopes.has(scope)) throw new Error(`Unknown palette scope: ${scope}`);
        }

        function visit(node, scopes) {
            if (node.type !== 'element' || node.name === 'metadata') return;
            const localScopes = [...scopes];
            if (node.attributes['data-part']) localScopes.unshift(`@${node.attributes['data-part']}`);
            if (node.attributes.id) localScopes.unshift(`#${node.attributes.id}`);
            for (const scope of localScopes) seenScopes.add(scope);
            for (const [name, value] of Object.entries(node.attributes)) {
                if (paintProperties.has(name)) node.attributes[name] = remapPaint(value, localScopes);
                if (name === 'style') node.attributes[name] = remapCss(value, localScopes);
            }
            if (node.name === 'style') {
                for (const child of node.children) {
                    if (child.type === 'text' || child.type === 'cdata') child.value = remapCss(child.value, []);
                }
            }
            for (const child of node.children) visit(child, localScopes);
        }

        function remapPaint(value, scopes) {
            const paint = value.trim();
            if (/^(none|inherit|currentcolor|transparent)$/i.test(paint) || /^url\(#[^)]+\)$/.test(paint)) return value;
            const color = normalizeColor(paint);
            for (const scope of [...scopes, '']) {
                const result = rules.get(`${scope}:${color}`);
                if (result) return result;
            }
            throw new Error(`Unmapped paint: ${value}`);
        }

        function remapCss(css, scopes) {
            const parsed = postcss.parse(css, { from: undefined });
            parsed.walkDecls(declaration => {
                if (paintProperties.has(declaration.prop.toLowerCase()))
                    declaration.value = remapPaint(declaration.value, scopes);
            });
            return parsed.toString();
        }
    }
}

function normalizeColor(value) {
    if (!/^#(?:[\da-f]{3}|[\da-f]{6})$/i.test(value ?? ''))
        throw new Error(`Expected a literal RGB hex paint, got: ${value}`);
    const color = value.toLowerCase();
    return color.length === 4 ? '#' + [...color.slice(1)].map(char => char + char).join('') : color;
}

export async function generateSvgVariants({ directory, check = false } = {}) {
    directory ??= path.join(projectRoot, 'src/nodejs/images/kitties');
    const names = (await readdir(directory))
        .filter(name => name.endsWith('.svg') && !name.endsWith('-dark.svg'))
        .sort();
    if (names.length === 0) throw new Error(`No source SVGs in ${directory}`);
    const outputs = new Map();
    for (const name of names) {
        try {
            const source = await readFile(path.join(directory, name), 'utf8');
            outputs.set(name.replace(/\.svg$/, '-dark.svg'), remapSvg(source));
        } catch (error) {
            throw new Error(`${name}: ${error.message}`, { cause: error });
        }
    }
    const colors = await readFile(path.join(projectRoot, 'src/nodejs/styles/colors.css'), 'utf8');
    const template = await readFile(new URL('./svg-preview.template.html', import.meta.url), 'utf8');
    const backgrounds = readThemeBackgrounds(colors);
    const cards = names
        .map(name => {
            const id = name.slice(0, -4);
            if (!/^[a-z0-9-]+$/.test(id)) throw new Error(`Unsupported SVG filename: ${name}`);
            const title = id
                .split('-')
                .map(word => word[0].toUpperCase() + word.slice(1))
                .join(' ');
            return `<article class="asset" id="${id}">
  <h2>${title}</h2>
  <div class="pair">
    <figure class="light"><a href="${name}" target="_blank" rel="noopener">
      <img src="${name}" alt="${title}, light variant" width="320" height="320"></a>
      <figcaption><span>Light</span><a href="${name}" download>${name}</a></figcaption></figure>
    <figure class="dark"><a href="${id}-dark.svg" target="_blank" rel="noopener">
      <img src="${id}-dark.svg" alt="${title}, dark variant" width="320" height="320"></a>
      <figcaption><span>Dark</span><a href="${id}-dark.svg" download>${id}-dark.svg</a></figcaption></figure>
  </div>
</article>`;
        })
        .join('\n');
    outputs.set(
        'index.html',
        template
            .replaceAll('{{light-background}}', backgrounds.light)
            .replaceAll('{{dark-background}}', backgrounds.dark)
            .replaceAll('{{count}}', String(names.length))
            .replace('{{cards}}', cards)
    );
    const stale = [];
    for (const [name, content] of outputs) {
        const file = path.join(directory, name);
        let current;
        try {
            current = await readFile(file, 'utf8');
        } catch (error) {
            if (error.code !== 'ENOENT') throw error;
        }
        if (current === content) continue;
        stale.push(name);
        if (!check) await writeFile(file, content);
    }
    if (check && stale.length) throw new Error(`Stale SVG outputs; run npm run images:kitties: ${stale.join(', ')}`);
    return { assets: names.length, changed: stale.length };
}

export function readThemeBackgrounds(css) {
    const parsed = postcss.parse(css);
    const globals = new Map();
    const themes = { light: new Map(), dark: new Map() };
    parsed.walkRules(rule => {
        const selectors = rule.selectors ?? [];
        const destinations = [];
        if (selectors.includes(':root')) destinations.push(globals);
        for (const theme of Object.keys(themes)) {
            if (selectors.includes(`.theme-${theme}`)) destinations.push(themes[theme]);
        }
        for (const declaration of rule.nodes.filter(node => node.type === 'decl')) {
            for (const destination of destinations) destination.set(declaration.prop, declaration.value);
        }
    });
    return Object.fromEntries(
        Object.entries(themes).map(([theme, overrides]) => {
            const values = new Map([...globals, ...overrides]);
            const visited = new Set();
            let value = 'var(--background-01)';
            while (/^var\(--[\w-]+\)$/.test(value)) {
                const name = value.slice(4, -1);
                if (visited.has(name)) throw new Error(`Cyclic theme color: ${name}`);
                visited.add(name);
                value = values.get(name);
            }
            return [theme, normalizeColor(value)];
        })
    );
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
    const args = process.argv.slice(2);
    if (
        args.some(arg => arg.startsWith('-') && arg !== '--check') ||
        args.filter(arg => !arg.startsWith('-')).length > 1
    )
        throw new Error('Usage: node scripts/generate-svg-variants.mjs [directory] [--check]');
    const result = await generateSvgVariants({
        directory: args.find(arg => arg !== '--check'),
        check: args.includes('--check'),
    });
    console.log(`${result.assets} SVG pairs verified; ${result.changed} generated files updated.`);
}
