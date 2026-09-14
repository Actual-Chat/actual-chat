In server render mode, closing a test user's browser (or a Playwright context) while a chat/thread is open leaves
its Blazor circuit retained for minutes, and that ChatView keeps advancing the user's Read position as new entries
arrive (`chat_positions.origin` shows the same client prefix with a growing sequence). NotificationsBackend then drops
the notification as already read — it looks exactly like "followers aren't notified". Seen 2026-09-11 while
verifying thread notifications.

**Why:** cost two wrong conclusions before the cause was found.

**How to apply:** before closing a test user's page, navigate away in-circuit (`Blazor.navigateTo('/chat')`), or use a
user with no open sessions as the recipient. Also: a thread_contacts row inserted straight into the DB is invisible until
restart, because `GetThreadContact` is a cached compute method. Related: [[two-users-in-one-chrome-via-playwright-context]].
