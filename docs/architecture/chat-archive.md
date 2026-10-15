# Chat archive and freezing

Two unrelated features share a history, so the names are worth keeping apart:

- **Archive** is per user. An archived chat leaves the user's contact list and their unread counters, and comes
  back only when the user unarchives it. Nobody else is affected.
- **Freeze** is per chat (`Chat.IsFrozen`, set by an owner). A frozen chat stops accepting messages, joins and member
  changes, and every member's contact to it is removed. It used to be called `IsArchived`.

## Storage

An archived contact moves from `Contacts` to `ArchivedContacts` (`DbArchivedContact`). The row keeps what the contact
had (name, state, pin, `TouchedAt`) plus `ArchivedAt` and `IsBlocked`, so unarchiving restores it, at the top of the
list, as if it had just been touched.

**Only an archived contact can be blocked.** Blocking a peer archives its contact with `IsBlocked = true`;
unblocking clears the flag and leaves the contact archived. `ContactsBackend.IsBlocked` and `ListBlockedIds` read the
archive table. A blocked contact stays out of the archive list and count: blocked users are managed in the privacy
settings.

`Contact.ArchivedAt` is populated on reads, so `Contact.IsArchived` is how a client learns that a chat is archived.

## Reading the archive

The list is read as a count plus pages, `Constants.Contacts.ArchiveIndexTiles` (20 items) at a time, always ordered by
`ArchivedAt` descending:

- `IContacts.GetArchivedCount(session, placeId)`
- `IContacts.ListArchivedIds(session, placeId, indexTile)`

Every archive change invalidates the count, and the tiles depend on the count, so the count and all pages reset
together. Archived place chats are listed per place, like the contact list.

## What an archived contact ignores

- A new message, a touch, or a `Create` change does not bring the contact back: `ContactsBackend_Change` returns
  the archived contact and does nothing. Only `Contacts_Unarchive` does.
- Leaving the chat or deleting the account removes the archived row too, and so do deleting and freezing the chat:
  a frozen chat is removed from every contact list and archive.
- An `Update` of an archived contact changes only its names and pin. Archiving raises `ContactChangedEvent` as a removal
  from the contact list and unarchiving as a creation, so search and usage stats follow the list, not the table.
- The announcements chat and place root chats can't be archived: the list adds them on its own.

## Notifications

Archiving keeps notifications on, unless the user ticks "Turn off notifications for this chat" in the archive modal
(it sets `ChatNotificationMode.Muted`). The Archive tab's badge counts the chats with an active message-level
notification that `Contacts.GetForChat` reports as archived (`ChatListUI.GetArchivedUnreadChatCount`), so it never loads
the archive itself. Regular unread counters are computed from the contact list and never include archived chats.

## UI

The Archive tab is the last tab of the chat list, and shows `ChatList` with `IsArchive`. Its sort button has a
one-entry menu, "By archival time", since the order is fixed. A chat in the archive shows a banner with an
Unarchive button. A frozen chat shows a banner suggesting to archive it; "Not now" hides it for a week (kept in
local settings).

## Migration of blocked contacts

Blocked contacts used to live in `Contacts` with `State = Blocked`. Reads ignore such rows, and
`BlockedContactsMigrationFlow` (started by `MigrationFlow`) moves each of them into the archive as a blocked archived
contact, or marks the already archived one as blocked, and deletes the original. Until it has run, those contacts
behave as if they never existed. Once it has completed everywhere, retire it into a no-op, as the other migration
flows were, and drop the `Blocked` filters.
