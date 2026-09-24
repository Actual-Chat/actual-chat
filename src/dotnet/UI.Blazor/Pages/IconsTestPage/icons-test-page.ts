const iconSelectorRe = /^\.(icon-[a-z0-9-]+)::?before$/i;

export class IconsTestPage {
    public static render(container: HTMLElement): void {
        const names = IconsTestPage.collectIconNames();
        container.textContent = '';
        for (const name of names) {
            const cell = document.createElement('button');
            cell.type = 'button';
            cell.className = 'icons-cell';
            cell.title = `Click to copy "${name}"`;
            cell.addEventListener('click', () => { void navigator.clipboard.writeText(name); });

            const glyph = document.createElement('i');
            glyph.className = name;
            cell.appendChild(glyph);

            const label = document.createElement('span');
            label.className = 'icons-name';
            label.textContent = name;
            cell.appendChild(label);

            container.appendChild(cell);
        }
    }

    private static collectIconNames(): string[] {
        const names = new Set<string>();
        for (const sheet of Array.from(document.styleSheets)) {
            let rules: CSSRuleList;
            try {
                rules = sheet.cssRules;
            } catch {
                // Cross-origin stylesheets throw on cssRules access
                continue;
            }
            for (const rule of Array.from(rules)) {
                const selector = (rule as CSSStyleRule).selectorText;
                if (!selector)
                    continue;
                for (const part of selector.split(',')) {
                    const match = iconSelectorRe.exec(part.trim());
                    if (match)
                        names.add(match[1]);
                }
            }
        }
        return Array.from(names).sort();
    }
}
