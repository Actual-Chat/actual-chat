const rawTokenRe = /^--([a-z]+)-(\d{2})$/;

interface TypeRole {
    role: string;
    size: string;
    lh: string;
    weights: string;
}

const typeRoles: TypeRole[] = [
    { role: 'micro', size: '0.75rem', lh: '1.25', weights: '400 · 500' },
    { role: 'caption', size: '0.875rem', lh: '1.25', weights: '400 · 500' },
    { role: 'body', size: '1rem', lh: '1.25', weights: '400 · 500' },
    { role: 'subhead', size: '1.125rem', lh: '1.22', weights: '400 · 500 · 600' },
    { role: 'title', size: '1.25rem', lh: '1.2', weights: '400 · 500' },
    { role: 'heading', size: '1.5rem', lh: '1.17', weights: '400 · 500 · 600' },
];

const semanticGroups: [string, string[]][] = [
    ['Surfaces', ['background-01', 'background-02', 'background-03', 'background-04',
        'background-05', 'background-06', 'background-07', 'background-08']],
    ['Text & icons', ['text-01', 'text-02', 'text-03', 'text-04', 'text-05', 'text-06']],
    ['Brand & semantic', ['primary', 'danger', 'danger-light', 'success', 'warning']],
    ['Interaction', ['hover', 'selected', 'selection', 'separator', 'nav-separator',
        'disabled', 'placeholder', 'input']],
    ['Buttons', ['primary', 'primary-title', 'secondary', 'secondary-title', 'cancel',
        'cancel-title', 'outline', 'danger', 'danger-title', 'round', 'round-title']],
    ['Controls', ['toggle', 'toggle-on', 'toggle-circle', 'radio-border']],
    ['Messaging', ['author-name', 'mention-own-text', 'mention-other-text', 'reaction',
        'replied-message', 'selected-message', 'bubble', 'quote', 'code']],
    ['Status', ['status', 'status-online', 'status-away', 'status-offline']],
    ['Skeletons', ['skeleton', 'skeleton-border']],
];

/** Builds the live foundations (colors, palette, type, icons) of the admin /ui kit from the app's own tokens. */
export class UiPage {
    public static init(root: HTMLElement, category: string): void {
        const content = root.querySelector<HTMLElement>('.uk-content');
        if (content == null)
            return;

        UiPage.wireThemeSwitch(root, content);
        if (category === 'colors') {
            UiPage.renderSemantic(root, content);
            UiPage.renderPalette(root, content);
        }
        else if (category === 'typography')
            UiPage.renderTypeScale(root);
        else if (category === 'icons')
            UiPage.renderIcons(root);
    }

    // Private methods

    private static wireThemeSwitch(root: HTMLElement, content: HTMLElement): void {
        const bar = root.querySelector<HTMLElement>('.uk-theme-switch');
        if (bar == null || bar.dataset.wired === 'true')
            return;

        bar.dataset.wired = 'true';
        for (const button of Array.from(bar.querySelectorAll('button'))) {
            button.addEventListener('click', () => {
                const theme = button.dataset.theme ?? 'theme-light';
                root.classList.remove('theme-light', 'theme-dark', 'theme-ash');
                root.classList.add(theme);
                for (const other of Array.from(bar.querySelectorAll('button')))
                    other.classList.toggle('is-active', other === button);
                UiPage.renderSemantic(root, content);
            });
        }
    }

    private static renderTypeScale(root: HTMLElement): void {
        const host = root.querySelector<HTMLElement>('.uk-typescale');
        if (host == null)
            return;

        host.className = 'uk-typescale uk-card';
        host.textContent = '';
        for (const t of typeRoles) {
            const row = UiPage.el('div', 'uk-type-row');
            const meta = UiPage.el('div', 'uk-type-meta');
            meta.innerHTML = `text-${t.role}<span>${t.size} · lh ${t.lh} · ${t.weights}</span>`;
            const sample = UiPage.el('div', 'uk-type-sample');
            sample.style.fontSize = t.size;
            sample.style.lineHeight = t.lh;
            sample.style.fontWeight = t.weights.includes('500') ? '500' : '400';
            sample.textContent = 'Voxt — speech and text are the same message';
            row.append(meta, sample);
            host.appendChild(row);
        }
    }

    private static renderSemantic(root: HTMLElement, content: HTMLElement): void {
        const host = root.querySelector<HTMLElement>('.uk-colors-semantic');
        if (host == null)
            return;

        host.textContent = '';
        for (const [title, tokens] of semanticGroups) {
            const card = UiPage.el('div', 'uk-card');
            card.appendChild(UiPage.el('h4', 'uk-group-title', title));
            const grid = UiPage.el('div', 'uk-swatch-grid');
            for (const name of tokens)
                grid.appendChild(UiPage.swatch(name, content));

            card.appendChild(grid);
            host.appendChild(card);
        }
    }

    private static renderPalette(root: HTMLElement, content: HTMLElement): void {
        const host = root.querySelector<HTMLElement>('.uk-colors-palette');
        if (host == null)
            return;

        host.className = 'uk-colors-palette uk-card';
        host.textContent = '';
        for (const [family, names] of UiPage.collectPalette()) {
            host.appendChild(UiPage.el('div', 'uk-scale-fam', family));
            const row = UiPage.el('div', 'uk-scale-row');
            for (const name of names) {
                const step = UiPage.el('div', 'uk-scale-step');
                step.style.background = `var(--${name})`;
                step.title = `${name} · ${UiPage.resolve(name, content)}`;
                step.addEventListener('click', () => UiPage.copy(`var(--${name})`));
                row.appendChild(step);
            }
            host.appendChild(row);
        }
    }

    private static renderIcons(root: HTMLElement): void {
        const grid = root.querySelector<HTMLElement>('.uk-icons-grid');
        const search = root.querySelector<HTMLInputElement>('.uk-icons-search');
        const count = root.querySelector<HTMLElement>('.uk-icons-count');
        if (grid == null || search == null || count == null)
            return;

        const names = UiPage.collectIconNames();
        const fill = (filter: string) => {
            const f = filter.trim().toLowerCase();
            const list = f ? names.filter(n => n.toLowerCase().includes(f)) : names;
            grid.textContent = '';
            for (const name of list) {
                const cell = UiPage.el('button', 'uk-icons-cell');
                cell.innerHTML = `<i class="icon-${name}"></i><span>${name}</span>`;
                cell.addEventListener('click', () => UiPage.copy(`icon-${name}`));
                grid.appendChild(cell);
            }
            count.textContent = `${list.length} / ${names.length} icons`;
        };
        if (search.dataset.wired !== 'true') {
            search.dataset.wired = 'true';
            search.addEventListener('input', () => fill(search.value));
        }
        fill(search.value);
    }

    private static swatch(name: string, content: HTMLElement): HTMLElement {
        const hex = UiPage.resolve(name, content);
        const cell = UiPage.el('button', 'uk-swatch');
        cell.innerHTML = `<span class="uk-chip"><i style="background:var(--${name})"></i></span>`
            + `<span class="uk-swatch-meta"><span class="uk-swatch-name">--${name}</span>`
            + `<span class="uk-swatch-val">${hex}</span></span>`;
        cell.title = `--${name} · ${hex}`;
        cell.addEventListener('click', () => UiPage.copy(`var(--${name})`));
        return cell;
    }

    private static collectPalette(): [string, string[]][] {
        const families = new Map<string, string[]>();
        for (const sheet of Array.from(document.styleSheets)) {
            let rules: CSSRuleList;
            try {
                rules = sheet.cssRules;
            } catch {
                continue;
            }
            for (const rule of Array.from(rules)) {
                if (!(rule instanceof CSSStyleRule) || rule.selectorText !== ':root')
                    continue;

                for (const prop of Array.from(rule.style)) {
                    const match = rawTokenRe.exec(prop);
                    if (match == null)
                        continue;

                    const family = match[1];
                    if (!families.has(family))
                        families.set(family, []);
                    const steps = families.get(family)!;
                    if (!steps.includes(prop.slice(2)))
                        steps.push(prop.slice(2));
                }
            }
        }
        return Array.from(families.entries());
    }

    private static collectIconNames(): string[] {
        const names = new Set<string>();
        const re = /^\.(icon-[a-z0-9-]+)::?before$/i;
        for (const sheet of Array.from(document.styleSheets)) {
            let rules: CSSRuleList;
            try {
                rules = sheet.cssRules;
            } catch {
                continue;
            }
            for (const rule of Array.from(rules)) {
                if (!(rule instanceof CSSStyleRule))
                    continue;

                for (const part of rule.selectorText.split(',')) {
                    const match = re.exec(part.trim());
                    if (match != null)
                        names.add(match[1].slice(5));
                }
            }
        }
        return Array.from(names).sort();
    }

    private static resolve(name: string, content: HTMLElement): string {
        const probe = document.createElement('span');
        probe.style.cssText = `display:none;color:var(--${name})`;
        content.appendChild(probe);
        const rgb = getComputedStyle(probe).color;
        probe.remove();
        return UiPage.toHex(rgb);
    }

    private static toHex(rgb: string): string {
        const match = rgb.match(/rgba?\(([^)]+)\)/);
        if (match == null)
            return rgb;

        const parts = match[1].split(',').map(s => s.trim());
        const hex = (n: string) => Number(n).toString(16).padStart(2, '0');
        const alpha = parts[3] != null ? Math.round(parseFloat(parts[3]) * 255) : 255;
        let out = `#${hex(parts[0])}${hex(parts[1])}${hex(parts[2])}`;
        if (alpha !== 255)
            out += hex(String(alpha));

        return out.toUpperCase();
    }

    private static copy(text: string): void {
        void navigator.clipboard.writeText(text);
        UiPage.toast(`Copied ${text}`);
    }

    private static toast(message: string): void {
        let host = document.querySelector<HTMLElement>('.uk-toast');
        if (host == null) {
            host = UiPage.el('div', 'uk-toast');
            document.body.appendChild(host);
        }
        host.textContent = message;
        host.classList.add('is-shown');
        window.clearTimeout(Number(host.dataset.timer));
        host.dataset.timer = String(window.setTimeout(() => host!.classList.remove('is-shown'), 1400));
    }

    private static el(tag: string, className: string, text?: string): HTMLElement {
        const node = document.createElement(tag);
        node.className = className;
        if (text != null)
            node.textContent = text;

        return node;
    }
}
