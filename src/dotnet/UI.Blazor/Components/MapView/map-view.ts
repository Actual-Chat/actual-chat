import maplibregl, { Map as MlMap, Marker as MlMarker } from 'maplibre-gl';
import 'maplibre-gl/dist/maplibre-gl.css';

interface MapMarker {
    id: string;
    latitude: number;
    longitude: number;
    label?: string;
    color?: string;
    subLabel?: string;
}

interface MapViewOptions {
    styleUrl: string;
    centerLatitude: number;
    centerLongitude: number;
    zoom: number;
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
        });
        // The map is often created while its container is still 0-sized (e.g. inside a
        // modal that's mid-open-animation); MapLibre then loads no tiles and stays blank.
        // Re-measure whenever the container resizes so tiles load once it has real size.
        this.resizeObserver = new ResizeObserver(() => this.map.resize());
        this.resizeObserver.observe(element);
    }

    public setMarkers(markers: MapMarker[]): void {
        const seen = new Set<string>();
        for (const m of markers) {
            seen.add(m.id);
            const existing = this.markers.get(m.id);
            if (existing != null) {
                existing.setLngLat([m.longitude, m.latitude]);
                MapView.setSubLabel(existing.getElement(), m.subLabel ?? '');
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

    public dispose(): void {
        this.resizeObserver.disconnect();
        this.markers.clear();
        this.map.remove();
    }

    // Private methods

    // A colored pin with an always-visible caption below it (the default MapLibre
    // marker only shows the name in a click-to-open popup).
    private static createMarkerElement(m: MapMarker): HTMLElement {
        const el = document.createElement('div');
        el.className = 'map-marker';
        const pin = document.createElement('div');
        pin.className = 'c-pin';
        pin.style.backgroundColor = m.color ?? '#2563eb';
        el.appendChild(pin);
        const caption = document.createElement('div');
        caption.className = 'c-caption';
        el.appendChild(caption);
        if (m.label != null && m.label !== '') {
            const label = document.createElement('div');
            label.className = 'c-label';
            label.textContent = m.label;
            caption.appendChild(label);
        }
        MapView.setSubLabel(el, m.subLabel ?? '');
        return el;
    }

    private static setSubLabel(el: HTMLElement, text: string): void {
        const caption = el.querySelector('.c-caption');
        if (caption == null)
            return;

        let sub = caption.querySelector<HTMLElement>('.c-sublabel');
        if (text === '') {
            sub?.remove();
            return;
        }
        if (sub == null) {
            sub = document.createElement('div');
            sub.className = 'c-sublabel';
            caption.appendChild(sub);
        }
        sub.textContent = text;
    }
}
