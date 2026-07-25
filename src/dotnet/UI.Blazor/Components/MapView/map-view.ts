import maplibregl, { Map as MlMap, Marker as MlMarker } from 'maplibre-gl';
import 'maplibre-gl/dist/maplibre-gl.css';

interface GeoPoint {
    latitude: number;
    longitude: number;
    accuracy?: number | null;
    bearing?: number | null;
}

interface MapMarker {
    id: string;
    point: GeoPoint;
    label?: string;
    avatarUrl?: string;
    avatarKey?: string;
    isOwnLocation?: boolean;
}

interface MapViewOptions {
    styleUrl: string;
    centerLatitude: number;
    centerLongitude: number;
    zoom: number;
    interactive: boolean;
}

// A chat can show dozens of MapView instances at once (each location message renders one),
// but browsers cap live WebGL contexts per page (~16 in Chrome) and silently kill the oldest
// beyond that, leaving those maps blank and unresponsive. So the MapLibre map only exists
// while its container is near the viewport, and is rebuilt if its context still gets evicted.
export class MapView {
    private readonly element: HTMLElement;
    private readonly options: MapViewOptions;
    private readonly markers = new Map<string, MlMarker>();
    private readonly resizeObserver: ResizeObserver;
    private readonly visibilityObserver: IntersectionObserver;
    private map: MlMap | null = null;
    private lastMarkers: MapMarker[] = [];
    private isVisible = false;
    private isDisposed = false;
    private recreateTimer: ReturnType<typeof setTimeout> | null = null;

    public static create(element: HTMLElement, options: MapViewOptions): MapView {
        return new MapView(element, options);
    }

    constructor(element: HTMLElement, options: MapViewOptions) {
        this.element = element;
        this.options = options;
        // The map is often created while its container is still 0-sized (e.g. inside a
        // modal that's mid-open-animation); MapLibre then loads no tiles and stays blank.
        // Re-measure whenever the container resizes so tiles load once it has real size.
        this.resizeObserver = new ResizeObserver(() => this.map?.resize());
        this.resizeObserver.observe(element);
        this.visibilityObserver = new IntersectionObserver(
            entries => this.onVisibilityChange(entries[entries.length - 1].isIntersecting),
            { rootMargin: '50% 0px' });
        this.visibilityObserver.observe(element);
    }

    // Markers are DOM overlays, not style layers, so they survive setStyle — only
    // the tile/glyph/sprite layers are swapped when the app theme changes.
    public setStyle(styleUrl: string): void {
        this.options.styleUrl = styleUrl;
        this.map?.setStyle(styleUrl);
    }

    public setMarkers(markers: MapMarker[]): void {
        this.lastMarkers = markers;
        if (this.map != null)
            this.applyMarkers(markers);
    }

    public flyTo(latitude: number, longitude: number): void {
        this.options.centerLatitude = latitude;
        this.options.centerLongitude = longitude;
        this.map?.flyTo({ center: [longitude, latitude] });
    }

    public dispose(): void {
        this.isDisposed = true;
        if (this.recreateTimer != null)
            clearTimeout(this.recreateTimer);
        this.visibilityObserver.disconnect();
        this.resizeObserver.disconnect();
        this.destroyMap();
    }

    // Private methods

    private onVisibilityChange(isVisible: boolean): void {
        this.isVisible = isVisible;
        if (isVisible)
            this.createMap();
        else
            this.destroyMap();
    }

    private createMap(): void {
        if (this.map != null || this.isDisposed)
            return;

        this.map = new maplibregl.Map({
            container: this.element,
            style: this.options.styleUrl,
            center: [this.options.centerLongitude, this.options.centerLatitude],
            zoom: this.options.zoom,
            interactive: this.options.interactive,
            attributionControl: false,
        });
        this.map.on('webglcontextlost', () => this.onContextLost());
        this.applyMarkers(this.lastMarkers);
    }

    private destroyMap(): void {
        const map = this.map;
        if (map == null)
            return;

        this.map = null;
        // Keep the user's pan/zoom, so the map reappears where they left it.
        const center = map.getCenter();
        this.options.centerLatitude = center.lat;
        this.options.centerLongitude = center.lng;
        this.options.zoom = map.getZoom();
        for (const marker of this.markers.values())
            marker.remove();
        this.markers.clear();
        map.remove();
    }

    // The delay lets a burst of evictions settle before rebuilding.
    private onContextLost(): void {
        this.destroyMap();
        if (this.recreateTimer != null)
            clearTimeout(this.recreateTimer);
        this.recreateTimer = setTimeout(() => {
            this.recreateTimer = null;
            if (this.isVisible && !this.isDisposed)
                this.createMap();
        }, 250);
    }

    private applyMarkers(markers: MapMarker[]): void {
        const map = this.map;
        if (map == null)
            return;

        const seen = new Set<string>();
        for (const m of markers) {
            seen.add(m.id);
            let existing = this.markers.get(m.id);
            // MapLibre markers can't swap their element, so recreate the marker when its
            // visual shape changed (e.g. the avatar loaded after the first render).
            if (existing != null && existing.getElement().dataset.key !== MapView.markerKey(m)) {
                existing.remove();
                this.markers.delete(m.id);
                existing = undefined;
            }
            if (existing != null) {
                existing.setLngLat([m.point.longitude, m.point.latitude]);
                if (m.isOwnLocation)
                    MapView.applyHeading(existing.getElement(), m.point.bearing);
                continue;
            }

            // Avatar-less own location is a centered dot; other markers are a bubble-above-dot
            // pin anchored so the dot center (8px above the element bottom) hits the point.
            const isCenteredDot = m.isOwnLocation && !MapView.hasAvatar(m);
            const options: maplibregl.MarkerOptions = isCenteredDot
                ? { element: MapView.createMarkerElement(m) }
                : { element: MapView.createMarkerElement(m), anchor: 'bottom', offset: [0, 8] };
            const marker = new maplibregl.Marker(options)
                .setLngLat([m.point.longitude, m.point.latitude]);
            marker.addTo(map);
            this.markers.set(m.id, marker);
        }
        for (const [id, marker] of this.markers) {
            if (seen.has(id))
                continue;

            marker.remove();
            this.markers.delete(id);
        }
    }

    // Marker styles: the avatar-less own position is a lone blue dot (+ heading fan),
    // avatar-less others are the complete <map-marker-pin>, and any marker with an
    // avatar is a <map-marker-bubble> floating above its dot — the own blue dot or
    // a <map-marker-dot> — on the geo point.
    private static createMarkerElement(m: MapMarker): HTMLElement {
        const el = document.createElement('div');
        el.className = 'map-marker';
        el.dataset.key = MapView.markerKey(m);
        const avatar = MapView.createAvatarElement(m);
        if (m.isOwnLocation && avatar == null) {
            el.appendChild(MapView.createOwnLocationElement(m));
            return el;
        }

        const pin = document.createElement('div');
        pin.className = 'c-pin';
        if (avatar == null)
            pin.appendChild(document.createElement('map-marker-pin'));
        else {
            const dot = m.isOwnLocation
                ? MapView.createOwnLocationElement(m)
                : MapView.createDotElement();
            const bubble = document.createElement('map-marker-bubble');
            bubble.className = 'c-bubble';
            bubble.appendChild(avatar);
            pin.append(dot, bubble);
        }
        el.appendChild(pin);
        return el;
    }

    private static createDotElement(): HTMLElement {
        const dot = document.createElement('map-marker-dot');
        dot.className = 'c-dot';
        return dot;
    }

    private static hasAvatar(m: MapMarker): boolean {
        return (m.avatarUrl != null && m.avatarUrl !== '') || (m.avatarKey != null && m.avatarKey !== '');
    }

    private static markerKey(m: MapMarker): string {
        return `${m.isOwnLocation ? 1 : 0}|${m.avatarUrl ?? ''}|${m.avatarKey ?? ''}`;
    }

    private static createOwnLocationElement(m: MapMarker): HTMLElement {
        const own = document.createElement('div');
        own.className = 'c-own-location';
        const heading = document.createElement('div');
        heading.className = 'c-own-heading';
        const pulse = document.createElement('div');
        pulse.className = 'c-own-pulse';
        const dot = document.createElement('div');
        dot.className = 'c-own-dot';
        own.append(heading, pulse, dot);
        MapView.applyHeading(own, m.point.bearing);
        return own;
    }

    // Heading is only known while moving; hide the fan when it's absent so a stale
    // direction isn't shown, and otherwise rotate it (0° = north) around the dot.
    private static applyHeading(element: HTMLElement, bearing?: number | null): void {
        const heading = element.querySelector<HTMLElement>('.c-own-heading');
        if (heading == null)
            return;

        if (bearing == null) {
            heading.style.display = 'none';
            return;
        }

        heading.style.display = '';
        heading.style.transform = `rotate(${bearing}deg)`;
    }

    private static createAvatarElement(m: MapMarker): HTMLElement | null {
        if (m.avatarUrl != null && m.avatarUrl !== '') {
            const img = document.createElement('img');
            img.className = 'c-avatar';
            img.src = m.avatarUrl;
            img.alt = m.label ?? '';
            return img;
        }

        if (m.avatarKey != null && m.avatarKey !== '') {
            const beam = document.createElement('beam-avatar');
            beam.className = 'c-avatar';
            beam.setAttribute('key', m.avatarKey);
            return beam;
        }

        return null;
    }
}
