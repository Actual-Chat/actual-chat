import ActivityKit
import SwiftUI
import WidgetKit

@main
struct VoxtActivitiesWidgetBundle: WidgetBundle {
    var body: some Widget { VoxtLiveActivityWidget() }
}

struct VoxtLiveActivityWidget: Widget {
    var body: some WidgetConfiguration {
        ActivityConfiguration(for: VoxtActivityAttributes.self) { context in
            LockScreenView(state: context.state)
        } dynamicIsland: { context in
            DynamicIsland {
                DynamicIslandExpandedRegion(.leading) {
                    Image(systemName: iconName(context.state.kind))
                }
                DynamicIslandExpandedRegion(.center) {
                    VStack(alignment: .leading) {
                        Text(context.state.title).font(.headline).lineLimit(1)
                        Text(context.state.subtitle).font(.caption).foregroundStyle(.secondary)
                    }
                }
                DynamicIslandExpandedRegion(.bottom) {
                    if context.state.progress >= 0 {
                        ProgressView(value: context.state.progress)
                    }
                }
            } compactLeading: {
                Image(systemName: iconName(context.state.kind))
            } compactTrailing: {
                if context.state.progress >= 0 {
                    ProgressView(value: context.state.progress).progressViewStyle(.circular)
                } else {
                    Image(systemName: "waveform")
                }
            } minimal: {
                Image(systemName: iconName(context.state.kind))
            }
        }
    }
}

private struct LockScreenView: View {
    let state: VoxtActivityAttributes.ContentState

    var body: some View {
        HStack {
            Image(systemName: iconName(state.kind)).font(.title2)
            VStack(alignment: .leading) {
                Text(state.title).font(.headline).lineLimit(1)
                Text(state.subtitle).font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            if state.progress >= 0 {
                ProgressView(value: state.progress).frame(width: 60)
            }
        }
        .padding()
    }
}

// Mirrors ActivityKind in UI.Blazor: 0 Replaying, 1 Listening, 2 Recording, 3 Armed,
// 4 Uploading, 5 SharingLocation, 6 Downloading.
private func iconName(_ kind: Int) -> String {
    switch kind {
    case 0: return "play.circle"
    case 1: return "ear"
    case 2: return "mic.fill"
    case 3: return "flipphone"
    case 4: return "arrow.up.circle"
    case 5: return "location.fill"
    case 6: return "arrow.down.circle"
    default: return "app.badge"
    }
}
