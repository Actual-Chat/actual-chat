import ActivityKit

// Compiled into both targets: the shim requests activities with these attributes,
// the widget renders them. ActivityKit matches the two by type name, so the file
// must stay a single source shared by both - never copied.
@available(iOS 16.1, *)
public struct VoxtActivityAttributes: ActivityAttributes {
    public struct ContentState: Codable, Hashable {
        public var kind: Int
        public var title: String
        public var subtitle: String
        // < 0 means "no progress bar"
        public var progress: Double

        public init(kind: Int, title: String, subtitle: String, progress: Double) {
            self.kind = kind
            self.title = title
            self.subtitle = subtitle
            self.progress = progress
        }
    }

    public init() {}
}
