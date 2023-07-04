import { BrowserInit } from '../BrowserInit/browser-init';
import { Kvas } from 'kvas';
import { Log } from 'logging';

const { warnLog } = Log.get('KvasBackend');

export class WebKvasBackend {
    private readonly _whenInitialized: Promise<void>;
    public readonly versionKey = "(version)";

    public get name() { return this.kvas.name; }

    constructor(
        public readonly kvas: Kvas,
        public readonly baseVersion: string,
        public readonly isApiVersionDependent,
        public readonly isSessionDependent
    ) {
        this._whenInitialized = this.init();
    }

    protected async init(): Promise<void> {
        const expectedVersion = await this.getExpectedVersion();
        const version = await this.kvas.get(this.versionKey);
        if (version !== expectedVersion) {
            warnLog?.log(`${this.name}: reset ('${version}' != '${expectedVersion}')`);
            await this.kvas.clear();
            await this.kvas.set(this.versionKey, expectedVersion);
        }
    }

    public whenInitialized(): Promise<void> {
        return this._whenInitialized;
    }

    public async getMany(keys: string[]): Promise<string[]> {
        await this._whenInitialized;
        const values = await this.kvas.getMany(keys);
        return values as string[];
    }

    public async setMany(keys: string[], values: string[]): Promise<void> {
        await this._whenInitialized;
        await this.kvas.setMany(keys, values);
    }

    public async clear() {
        await this._whenInitialized;
        await this.kvas.clear();
        await this.kvas.set(this.versionKey, await this.getExpectedVersion());
    }

    protected async getExpectedVersion(): Promise<string> {
        let version = this.baseVersion;
        if (!(this.isApiVersionDependent || this.isSessionDependent))
            return version;

        await BrowserInit.whenInitialized;
        if (this.isApiVersionDependent)
            version = `${version}, ApiVersion=${BrowserInit.apiVersion}`;
        if (this.isSessionDependent)
            version = `${version}, SessionHash=${BrowserInit.sessionHash}`;
        return version;
    }
}
