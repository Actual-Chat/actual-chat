import { Log } from 'logging';

const { debugLog, warnLog } = Log.get('Versioning');

interface ImportMap {
    imports: Record<string, string>;
    integrity: Record<string, string>;
}

export class Versioning {
    private static _assetMap?: Map<string, string>;

    public static get assetMap(): Map<string, string> {
        if (this._assetMap)
            return this._assetMap;

        this.init();
        return this._assetMap!;
    }

    public static init(assetMap?: Map<string, string>) {
        if (this._assetMap) {
            warnLog?.log('init: skipped (already initialized)');
            return;
        }

        if (!assetMap) {
            const origin = document.location.origin;
            const stripOrigin = (s: string) => s.startsWith(origin) ? s.slice(origin.length) : s;

            const processAsset = (key: string, value: string) => {
                const assetMatch = /\.(?<hash>[a-z0-9]{10})\.(js|wasm)$/.exec(value);
                if (!assetMatch)
                    return;

                const assetPath = stripOrigin(value);
                const simpleKey = assetPath.replace(assetMatch.groups.hash+'.', '');
                assetMap.set(simpleKey, assetPath);
                if (!simpleKey.startsWith('/')) assetMap.set('/' + simpleKey, assetPath);
            };

            assetMap = new Map<string, string>();
            for (const e of document.head.children) {
                if (e.localName !== 'link')
                    continue;

                const href = e.href as string;
                processAsset(href, href);
            }
            const importMapInnerHtml = document.head.querySelector('script[type="importmap"]')?.innerHTML;
            if (importMapInnerHtml) {
                const importMap = JSON.parse(importMapInnerHtml) as ImportMap;
                for (const [key, value] of Object.entries(importMap.imports).filter(([k]) => k.startsWith('/dist/')))
                    processAsset(key, value);
            }
        }
        this._assetMap = assetMap;
        debugLog?.log('init: artifact versions:', assetMap);
    }

    public static mapPath(path: string): string {
        const versionedPath = this.assetMap.get(path);
        if (versionedPath) {
            debugLog?.log(`mapPath: '${path}' -> '${versionedPath}'`);
            return versionedPath;
        }
        else {
            warnLog?.log(`mapPath: '${path}' - no version found`);
            return path;
        }
    }
}

globalThis.Versioning = Versioning;
