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
    private static let senderNameKey = "senderName"
    private static let groupTitleKey = "groupTitle"
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

    private static func iconUrl(of content: UNMutableNotificationContent) -> URL? {
        guard let icon = content.userInfo[iconKey] as? String, !icon.isEmpty else { return nil }
        return URL(string: icon)
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
        // The chat names the banner: the icon is its picture, and each body line carries its own
        // author. A peer chat has no group title, so the other party stays the headline.
        let headline = headline(of: content)
        guard let iconData,
              let intent = makeIntent(headline: headline, content: content, iconData: iconData)
        else { return content }

        // Titling the banner ourselves keeps it right even when the update below fails -
        // which it does when the communication entitlement is missing.
        content.title = headline
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
        // The thread id is the chat tag the server groups banners under, and AppDelegate's
        // dismissal path matches delivered notifications on it. updating(from:) rewrites the
        // thread id from the conversation id, so these two must be the same string.
        let conversationId = content.threadIdentifier.isEmpty
            ? (content.userInfo[chatIdKey] as? String ?? "")
            : content.threadIdentifier
        guard !conversationId.isEmpty else { return nil }

        let image = INImage(imageData: iconData)
        // The chat is the intent's sender. speakableGroupName stays unset on purpose: iOS renders
        // it as a subtitle beneath the sender, the second line this banner doesn't want.
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

    // Empty for a notification composed before the server sent these keys, which leaves the
    // banner as the server titled it rather than rewriting it into a communication one.
    private static func headline(of content: UNMutableNotificationContent) -> String {
        if let group = content.userInfo[groupTitleKey] as? String, !group.isEmpty {
            return group
        }
        return content.userInfo[senderNameKey] as? String ?? ""
    }
}
