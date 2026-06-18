import maplibregl, { Map as MlMap, Marker as MlMarker } from 'maplibre-gl';
import 'maplibre-gl/dist/maplibre-gl.css';

interface MapMarker {
    id: string;
    latitude: number;
    longitude: number;
    label?: string;
    color?: string;
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

            const marker = new maplibregl.Marker({ color: m.color ?? '#2563eb' })
                .setLngLat([m.longitude, m.latitude]);
            if (m.label != null)
                marker.setPopup(new maplibregl.Popup().setText(m.label));
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
        this.markers.clear();
        this.map.remove();
    }
}
