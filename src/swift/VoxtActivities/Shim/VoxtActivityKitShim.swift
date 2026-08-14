import ActivityKit
import Foundation

// The three @_cdecl symbols below are P/Invoked from IosActivitiesBackend; renaming
// one silently breaks the managed side at runtime, not at build time.

@available(iOS 16.1, *)
private enum Holder {
    // Reached from whichever thread the runtime P/Invokes on; ActivityKit is itself
    // thread-safe, and the managed side serializes calls.
    nonisolated(unsafe) static var current: Activity<VoxtActivityAttributes>?
}

@_cdecl("voxt_activities_enabled")
public func voxtActivitiesEnabled() -> Int32 {
    guard #available(iOS 16.1, *) else { return 0 }
    return ActivityAuthorizationInfo().areActivitiesEnabled ? 1 : 0
}

// Live Activities outlive process death, so a relaunch starts with Holder.current == nil while
// ActivityKit may still be running (or, after a crash, several duplicates of) the activity the
// previous process created. Adopting the first one and ending the rest keeps start_or_update from
// creating a duplicate and end() from leaking an orphan that outlives the app.
@available(iOS 16.2, *)
private func adoptExistingActivities() {
    guard Holder.current == nil else { return }
    let activities = Activity<VoxtActivityAttributes>.activities
    guard let first = activities.first else { return }
    Holder.current = first
    for extra in activities.dropFirst() {
        Task { await extra.end(nil, dismissalPolicy: .immediate) }
    }
}

// Optional pointers: same C ABI as non-optional, but a null from the managed side maps to ""
// instead of trapping inside String(cString:).
@_cdecl("voxt_activity_start_or_update")
public func voxtActivityStartOrUpdate(
    _ kind: Int32,
    _ title: UnsafePointer<CChar>?,
    _ subtitle: UnsafePointer<CChar>?,
    _ progress: Double
) -> Int32 {
    guard #available(iOS 16.2, *) else { return 0 }
    adoptExistingActivities()
    let state = VoxtActivityAttributes.ContentState(
        kind: Int(kind),
        title: title.map { String(cString: $0) } ?? "",
        subtitle: subtitle.map { String(cString: $0) } ?? "",
        progress: progress)
    let content = ActivityContent(state: state, staleDate: nil)
    // A dismissed or expired activity still hangs off Holder, and updating it is a silent
    // no-op - so anything but .active means we must request a fresh one.
    if let activity = Holder.current {
        if activity.activityState == .active {
            Task { await activity.update(content) }
            return 1
        }
        Holder.current = nil
    }
    do {
        Holder.current = try Activity<VoxtActivityAttributes>.request(
            attributes: VoxtActivityAttributes(),
            content: content,
            pushType: nil)
        return 1
    } catch {
        // Background start, activities disabled, or the per-app activity budget is spent.
        return 0
    }
}

@_cdecl("voxt_activity_end")
public func voxtActivityEnd() {
    guard #available(iOS 16.2, *) else { return }
    adoptExistingActivities()
    guard let activity = Holder.current else { return }
    Holder.current = nil
    Task { await activity.end(nil, dismissalPolicy: .immediate) }
}
