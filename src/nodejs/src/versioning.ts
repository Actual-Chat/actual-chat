import { Log } from 'logging';

const { debugLog, warnLog } = Log.get('Versioning');

export class Versioning {
    private static _artifactVersions: Map<string, string> = null;

    public static get artifactVersions(): Map<string, string> {
        if (this._artifactVersions)
            return this._artifactVersions;

        this.init();
        return this._artifactVersions;
    }

    public static init(artifactVersions: Map<string, string> = null) {
        if (this._artifactVersions) {
            warnLog?.log('init: skipped (already initialized)');
            return;
        }

        if (artifactVersions === null) {
            const origin = document.location.origin;
            const stripOrigin = (s: string) => s.startsWith(origin) ? s.slice(origin.length) : s;

            artifactVersions = new Map<string, string>();
            for (const e of document.head.children) {
                if (e.localName !== 'link')
                    continue;
                const href = e['href'] as string;
                if (!href || !href.includes('?v='))
                    continue;

                const [key, value] = stripOrigin(href).split('?v=');
                artifactVersions.set(key, value);
            }
        }
        this._artifactVersions = artifactVersions;
        debugLog?.log('init: artifact versions:', artifactVersions);
    }

    public static mapPath(path: string): string {
        const version = this.artifactVersions.get(path);
        if (version) {
            const result = path + '?v=' + version;
            debugLog?.log(`mapPath: '${path}' -> v'${version}'`);
            return result;
        }
        else {
            warnLog?.log(`mapPath: '${path}' - no version found`);
            return path;
        }
    }
}

globalThis['Versioning'] = Versioning;
