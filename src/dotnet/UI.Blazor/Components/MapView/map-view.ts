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

export class MapView {
    private readonly map: MlMap;
    private readonly markers = new Map<string, MlMarker>();
    private readonly resizeObserver: ResizeObserver;

    public static create(element: HTMLElement, options: MapViewOptions): MapView {
        return new MapView(element, options);
    }

    constructor(element: HTMLElement, options: MapViewOptions) {
        this.map = new maplibregl.Map({
            container: element,
            style: options.styleUrl,
            center: [options.centerLongitude, options.centerLatitude],
            zoom: options.zoom,
            interactive: options.interactive,
            attributionControl: false,
        });
        // The map is often created while its container is still 0-sized (e.g. inside a
        // modal that's mid-open-animation); MapLibre then loads no tiles and stays blank.
        // Re-measure whenever the container resizes so tiles load once it has real size.
        this.resizeObserver = new ResizeObserver(() => this.map.resize());
        this.resizeObserver.observe(element);
    }

    // Markers are DOM overlays, not style layers, so they survive setStyle — only
    // the tile/glyph/sprite layers are swapped when the app theme changes.
    public setStyle(styleUrl: string): void {
        this.map.setStyle(styleUrl);
    }

    public setMarkers(markers: MapMarker[]): void {
        const seen = new Set<string>();
        for (const m of markers) {
            seen.add(m.id);
            const existing = this.markers.get(m.id);
            if (existing != null) {
                existing.setLngLat([m.point.longitude, m.point.latitude]);
                if (m.isOwnLocation)
                    MapView.applyHeading(existing.getElement(), m.point.bearing);
                continue;
            }

            // Own location is a centered dot; other markers are a bubble-above-dot pin
            // anchored so the dot center (8px above the element bottom) hits the point.
            const options: maplibregl.MarkerOptions = m.isOwnLocation
                ? { element: MapView.createMarkerElement(m) }
                : { element: MapView.createMarkerElement(m), anchor: 'bottom', offset: [0, 8] };
            const marker = new maplibregl.Marker(options)
                .setLngLat([m.point.longitude, m.point.latitude]);
            marker.addTo(this.map);
            this.markers.set(m.id, marker);
        }
        for (const [id, marker] of this.markers) {
            if (seen.has(id))
                continue;

            marker.remove();
            this.markers.delete(id);
        }
    }

    public flyTo(latitude: number, longitude: number): void {
        this.map.flyTo({ center: [longitude, latitude] });
    }

    public dispose(): void {
        this.resizeObserver.disconnect();
        this.markers.clear();
        this.map.remove();
    }

    // Private methods

    // Two marker styles: the viewer's own live position (blue dot + heading fan), or a
    // bubble (circle + tail, holding the author avatar or a pin glyph) floating above
    // a ringed dot that sits on the geo point.
    private static createMarkerElement(m: MapMarker): HTMLElement {
        const el = document.createElement('div');
        el.className = 'map-marker';
        if (m.isOwnLocation) {
            el.appendChild(MapView.createOwnLocationElement(m));
            return el;
        }

        const pin = document.createElement('div');
        pin.className = 'c-pin';
        const dot = document.createElement('map-marker-dot');
        dot.className = 'c-dot';
        const bubble = document.createElement('map-marker-bubble');
        bubble.className = 'c-bubble';
        bubble.appendChild(MapView.createAvatarElement(m) ?? document.createElement('map-marker-pin'));
        pin.append(dot, bubble);
        el.appendChild(pin);
        return el;
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
