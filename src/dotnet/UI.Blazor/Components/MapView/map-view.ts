import maplibregl, { Map as MlMap, Marker as MlMarker } from 'maplibre-gl';
import 'maplibre-gl/dist/maplibre-gl.css';

interface MapMarker {
    id: string;
    latitude: number;
    longitude: number;
    label?: string;
    avatarUrl?: string;
    avatarKey?: string;
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
                existing.setLngLat([m.longitude, m.latitude]);
                continue;
            }

            const marker = new maplibregl.Marker({ element: MapView.createMarkerElement(m) })
                .setLngLat([m.longitude, m.latitude]);
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

    // Two marker styles: a plain dot when there's no avatar (in-chat preview), or an
    // author avatar in a white-bordered circle (the map modal) — Telegram-style.
    private static createMarkerElement(m: MapMarker): HTMLElement {
        const el = document.createElement('div');
        el.className = 'map-marker';
        const avatar = MapView.createAvatarElement(m);
        const pin = document.createElement('div');
        pin.className = avatar != null ? 'c-pin c-pin-avatar' : 'c-pin c-pin-dot';
        if (avatar != null)
            pin.appendChild(avatar);
        el.appendChild(pin);
        return el;
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
