import Intents
import os
import UserNotifications

// Rewrites Voxt's chat pushes into iOS communication notifications, so the banner shows the
// chat or author avatar in place of the app icon. `mutable-content` is inert without an
// extension like this one, which is the only reason the target exists.
final class NotificationService: UNNotificationServiceExtension {
    // The FCM data keys `FirebaseMessagingClient` sends alongside `aps`.
    private static let iconKey = "icon"
    private static let chatIdKey = "chatId"
    private static let tagKey = "tag"
    private static let activeTagsKey = "activeTags"
    private static let activeVersionKey = "activeVersion"
    // Highest active-set version this extension has pruned against. Its own container, not the
    // app group: nothing else reads it.
    private static let lastPrunedVersionKey = "voxt.lastPrunedActiveVersion"
    // The edge node the app's RPC is on, which MauiRpcEndpointSelector writes to the app group;
    // absent while the app is on the origin.
    private static let rpcEndpointKey = "rpc_endpoint"
    // The system allows 30s, but a banner that lands seconds late is worse than an iconless
    // one - and these are 128px avatars, so a slow fetch means the network is gone anyway.
    private static let downloadTimeout: TimeInterval = 5
    private static let maxIconBytes = 512 * 1024

    // The only way to see what this extension did: it is a separate process, and the failure
    // modes (no entitlement, unreachable icon host) are invisible from the banner alone.
    private static let log = Logger(subsystem: "ai.voxt.notification-service", category: "push")

    private let lock = NSLock()
    private var contentHandler: ((UNNotificationContent) -> Void)?
    private var bestAttempt: UNMutableNotificationContent?
    private var downloadTask: URLSessionDataTask?

    override func didReceive(
        _ request: UNNotificationRequest,
        withContentHandler contentHandler: @escaping (UNNotificationContent) -> Void
    ) {
        guard let content = request.content.mutableCopy() as? UNMutableNotificationContent else {
            contentHandler(request.content)
            return
        }
        lock.lock()
        self.contentHandler = contentHandler
        bestAttempt = content
        lock.unlock()

        // Started before the icon download and deliberately not awaited: closing stale banners is
        // local and must not be lost to a slow network, and the new banner is shown by the system
        // after the handler returns either way.
        Self.pruneStaleBanners(from: content)

        guard let iconUrl = Self.iconUrl(of: content) else {
            deliver(content)
            return
        }
        downloadTask = Self.session.dataTask(with: iconUrl) { [weak self] data, response, error in
            guard let self else { return }
            if let error {
                Self.log.error("Icon download failed: \(error.localizedDescription, privacy: .public)")
            }
            self.deliver(Self.applyIcon(Self.imageData(data, response), to: content))
        }
        downloadTask?.resume()
    }

    override func serviceExtensionTimeWillExpire() {
        downloadTask?.cancel()
        lock.lock()
        let content = bestAttempt
        lock.unlock()
        if let content {
            deliver(content)
        }
    }

    // Private members

    private static let session: URLSession = {
        let configuration = URLSessionConfiguration.default
        configuration.timeoutIntervalForRequest = downloadTimeout
        configuration.timeoutIntervalForResource = downloadTimeout
        // The extension process is reused across pushes, so a chatty conversation's avatar is
        // normally served from cache - the icon URLs go through the image proxy, and honouring
        // its cache headers is enough to get that for free.
        configuration.requestCachePolicy = .useProtocolCachePolicy
        return URLSession(configuration: configuration)
    }()

    private func deliver(_ content: UNNotificationContent) {
        lock.lock()
        let handler = contentHandler
        contentHandler = nil
        bestAttempt = nil
        lock.unlock()
        handler?(content)
    }

    // Closes the banners the server no longer lists as active. This is the only clearing path that
    // needs no background push, so it is the one Doze and the standby buckets cannot hold - but it
    // only runs when a new alert arrives, so it supplements the dismissal push rather than
    // replacing it.
    private static func pruneStaleBanners(from content: UNMutableNotificationContent) {
        guard let rawTags = content.userInfo[activeTagsKey] as? String,
              let rawVersion = content.userInfo[activeVersionKey] as? String,
              let version = Int64(rawVersion)
        else { return }

        // Pushes can arrive out of order, and an active set is a snapshot, not a delta: applying an
        // older one would close banners a newer one had already accounted for.
        let defaults = UserDefaults.standard
        guard version > (defaults.object(forKey: lastPrunedVersionKey) as? Int64 ?? 0) else {
            log.info("Skipping stale active set \(version, privacy: .public)")
            return
        }
        defaults.set(version, forKey: lastPrunedVersionKey)

        let activeTags = Set(rawTags.split(separator: ",").map(String.init))
        UNUserNotificationCenter.current().getDeliveredNotifications { delivered in
            // Only banners carrying our own tag key are the active set's to close: the tray also
            // holds locally posted notifications, which the server has never heard of.
            let stale = delivered
                .filter {
                    guard let tag = $0.request.content.userInfo[tagKey] as? String, !tag.isEmpty
                    else { return false }
                    return !activeTags.contains(tag)
                }
                .map(\.request.identifier)
            guard !stale.isEmpty else { return }

            log.info("Pruning \(stale.count, privacy: .public) stale banner(s)")
            UNUserNotificationCenter.current().removeDeliveredNotifications(withIdentifiers: stale)
        }
    }

    private static func iconUrl(of content: UNMutableNotificationContent) -> URL? {
        guard let icon = content.userInfo[iconKey] as? String, !icon.isEmpty,
              let url = URL(string: icon)
        else { return nil }
        // The server sends the origin's URL, which a throttled network may not reach - the reason
        // the app moved its RPC to an edge node in the first place.
        guard let endpoint = sharedDefaults?.string(forKey: rpcEndpointKey), !endpoint.isEmpty
        else { return url }
        return contentUrl(url, endpoint: endpoint)
    }

    // The app group is named after the app's bundle id, and this appex's is the app's + ".notification".
    private static let sharedDefaults: UserDefaults? = {
        guard let bundleId = Bundle.main.bundleIdentifier,
              let dot = bundleId.lastIndex(of: ".")
        else { return nil }
        return UserDefaults(suiteName: "group.\(bundleId[..<dot]).shared")
    }()

    // Mirrors RpcEndpointSelector: an edge node is <node>.edge.<origin>, it stands in for the origin
    // itself (generated avatars are under /api), and serves the origin's subdomains under its own
    // prefix - on kz1.edge.voxt.ai, media.voxt.ai is reached as kz1.edge.media.voxt.ai.
    private static func contentUrl(_ url: URL, endpoint: String) -> URL {
        let endpoint = endpoint.lowercased()
        guard let host = url.host?.lowercased(),
              let edge = endpoint.range(of: ".edge.")
        else { return url }

        let prefix = endpoint[..<edge.upperBound]
        let origin = endpoint[edge.upperBound...]
        guard host == origin || host.hasSuffix("." + origin),
              var components = URLComponents(url: url, resolvingAgainstBaseURL: false)
        else { return url }

        components.host = host == origin ? endpoint : prefix + host
        return components.url ?? url
    }

    private static func imageData(_ data: Data?, _ response: URLResponse?) -> Data? {
        guard let data, !data.isEmpty, data.count <= maxIconBytes else { return nil }
        // INImage rasterizes at display time and iOS drops the whole notification if it can't:
        // the marble avatar endpoint answers SVG unless a format is asked for. The server does
        // ask - IconQuery.Create pins PNG - so SVG here means a server old enough to predate it.
        return response?.mimeType == "image/svg+xml" ? nil : data
    }

    private static func applyIcon(
        _ iconData: Data?,
        to content: UNMutableNotificationContent
    ) -> UNNotificationContent {
        // The server's title is the headline every other platform leads with: the sender, and
        // in a group chat the sender "@" the chat. The intent's sender carries it so that
        // updating(from:) keeps it, while the picture stays the chat's, as on Android.
        guard let iconData,
              let intent = makeIntent(headline: content.title, content: content, iconData: iconData)
        else { return content }

        let updated: UNNotificationContent
        do {
            updated = try content.updating(from: intent)
        }
        catch {
            // Overwhelmingly means the communication entitlement is missing from this build.
            log.error("Communication notification update failed: \(error.localizedDescription, privacy: .public)")
            return content
        }

        // Makes the chat addressable by Focus ("Allow notifications from") and Siri.
        let interaction = INInteraction(intent: intent, response: nil)
        interaction.direction = .incoming
        interaction.donate(completion: nil)
        return updated
    }

    private static func makeIntent(
        headline: String,
        content: UNMutableNotificationContent,
        iconData: Data
    ) -> INSendMessageIntent? {
        guard !headline.isEmpty else { return nil }
        // The thread id is the chat tag the server groups banners under, and updating(from:)
        // rewrites it from the conversation id, so these two must be the same string. AppDelegate
        // dismisses by notification id rather than by this, and updating(from:) does preserve
        // userInfo - measured on device - so that id survives to the delivered banner.
        let conversationId = content.threadIdentifier.isEmpty
            ? (content.userInfo[chatIdKey] as? String ?? "")
            : content.threadIdentifier
        guard !conversationId.isEmpty else { return nil }

        let image = INImage(imageData: iconData)
        // The person is keyed by the chat, so Focus and Siri treat the conversation as one contact
        // whatever the headline says. speakableGroupName stays unset on purpose: iOS renders it
        // only for a conversation with several recipients, and then as a second header line.
        let sender = INPerson(
            personHandle: INPersonHandle(value: conversationId, type: .unknown),
            nameComponents: nil,
            displayName: headline,
            image: image,
            contactIdentifier: nil,
            customIdentifier: conversationId)
        return INSendMessageIntent(
            recipients: nil,
            outgoingMessageType: .outgoingMessageText,
            content: nil,
            speakableGroupName: nil,
            conversationIdentifier: conversationId,
            serviceName: nil,
            sender: sender,
            attachments: nil)
    }
}
