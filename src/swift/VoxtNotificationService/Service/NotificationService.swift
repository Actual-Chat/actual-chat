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
    // `NotificationHelper.GetTitle` composes group-chat titles as "<author> @ <chat>".
    private static let titleSeparator = " @ "
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
        let (senderName, groupName) = splitTitle(content.title)
        guard let iconData,
              let intent = makeIntent(senderName: senderName, groupName: groupName, content: content, iconData: iconData)
        else { return content }

        // Titling the banner ourselves keeps the sender/chat split readable even when the
        // update below fails - which it does when the communication entitlement is missing.
        content.title = senderName
        content.subtitle = groupName ?? ""
        let updated: UNNotificationContent
        do {
            updated = try content.updating(from: intent)
        }
        catch {
            // Overwhelmingly means the communication entitlement is missing from this build.
            log.error("Communication notification update failed: \(error.localizedDescription, privacy: .public)")
            return content
        }

        // Makes the sender addressable by Focus ("Allow notifications from") and Siri.
        let interaction = INInteraction(intent: intent, response: nil)
        interaction.direction = .incoming
        interaction.donate(completion: nil)
        return updated
    }

    private static func makeIntent(
        senderName: String,
        groupName: String?,
        content: UNMutableNotificationContent,
        iconData: Data
    ) -> INSendMessageIntent? {
        guard !senderName.isEmpty else { return nil }
        // The thread id is the chat tag the server groups banners under, and AppDelegate's
        // dismissal path matches delivered notifications on it. updating(from:) rewrites the
        // thread id from the conversation id, so these two must be the same string.
        let conversationId = content.threadIdentifier.isEmpty
            ? (content.userInfo[chatIdKey] as? String ?? "")
            : content.threadIdentifier
        guard !conversationId.isEmpty else { return nil }

        let image = INImage(imageData: iconData)
        let sender = INPerson(
            personHandle: INPersonHandle(value: conversationId, type: .unknown),
            nameComponents: nil,
            displayName: senderName,
            image: image,
            contactIdentifier: nil,
            customIdentifier: conversationId)
        let intent = INSendMessageIntent(
            recipients: nil,
            outgoingMessageType: .outgoingMessageText,
            content: nil,
            speakableGroupName: groupName.map { INSpeakableString(spokenPhrase: $0) },
            conversationIdentifier: conversationId,
            serviceName: nil,
            sender: sender,
            attachments: nil)
        // For a group or place chat the server's icon is the chat's own picture, not the
        // author's, so it belongs on the group rather than only on the sender.
        if groupName != nil {
            intent.setImage(image, forParameterNamed: \.speakableGroupName)
        }
        return intent
    }

    private static func splitTitle(_ title: String) -> (senderName: String, groupName: String?) {
        guard let range = title.range(of: titleSeparator) else { return (title, nil) }
        let groupName = String(title[range.upperBound...])
        return (String(title[..<range.lowerBound]), groupName.isEmpty ? nil : groupName)
    }
}
