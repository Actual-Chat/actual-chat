# Speech coach v1.1: the Coach panel redesign

Status: draft for review. Branch `feat/4829-speech-coach`, issue #4829, PR #4856.
Mockups: https://claude.ai/artifact/FcEqKBSERVCyRrbAK69P8P (private canvas, seven boards).
Builds on the v1 spec (`2026-09-25-speech-coach-design.md`, kept on `feat/4829-speech-coach-bak`):
ownership, the pipeline, the tagger, storage and the event log stay as shipped. Where this spec
changes a v1 decision it says so.

## 1. Why

The shipped v1 panel shows a bare score, a Today / Week / Month / All time selector and thirteen
metric rows. Research over rival products (Yoodli, Orai, Speeko, Poised, Gong, Read.ai, Zoom,
Teams) and over user demand (Reddit, Hacker News, app-store reviews) says:

- The unit people think in is a conversation, not a calendar day. "Today" is empty most of the
  time and means nothing after a silent day.
- Nobody trusts an opaque number. Every product that survives either explains its score or leads
  with one concrete thing to fix.
- The loudest unmet need is "do I talk too much, do I interrupt", which only a real-conversation
  app can measure. Filler counts come second and are only believed when the user can audit them
  in the transcript.
- Non-native speakers are a large segment, and a bilingual user needs each language judged on its
  own: a Russian native at 69 and the same person's English at 45 must never be averaged.
- Always-on analysis reads as surveillance unless the product says, in the UI, that only the
  user's own messages are read and only the user sees the result.

This spec turns the panel into three questions (Recent, Progress, Skills), makes every
language-bound skill per language, adds a focus skill, positive reinforcement, per-chat and
per-place scope, and a settings page built from the app's own settings vocabulary.

## 2. Scope

In:

- Panel information architecture: header, score and focus card, Recent / Progress / Skills tabs,
  new-user state, settings page behind the gear.
- Conversation cards (Recent), grouped on the server.
- Per-language aggregation, bands, score, focus and tips for language-bound skills.
- Native / learning level per spoken language.
- Explainable score, one focus skill, positive tips, milestones, days with speech.
- Coaching scope: everywhere, per-chat and per-place opt-out, one-to-one skip.
- Weekly summary as an in-app note.

Out (unchanged from v1 or deferred):

- The tagger prompt and the analysis pipeline. Marks and the tap hint stay as shipped.
- Team or place-owner coaching. Coaching is always the listener's own setting.
- Calibrating bands from our own users' distributions (phase 2, see §6.3).
- Pace for languages without word spaces (ja, zh, th, km, lo, my). They keep "no data" for pace,
  vocabulary and sentence length until a character or syllable rate exists.
- Email delivery of the weekly summary (open question, §14).

## 3. Panel structure

Width and chrome follow the existing right panel: 352 px, blurred cover, Chat | Coach mode switch
in the cover, close button, round tinted 40 px action buttons, title row, pill tabs with the
bottom hill. All figures below are the app's own tokens (TT Commons Pro, `--text-01`,
`--text-03`, `--primary`, `--danger`, `--warning`, `--success`, `--separator`).

### 3.1 Header

- Own avatar (72 px) overlapping the cover, one action button: the gear (opens settings, §9).
- Title "Speech coach" and a `status-badge` pill "Only you" with a lock icon. The pill is the
  privacy promise and is always visible.
- When the user records in more than one language, a chip row under the title lists the spoken
  languages with their level ("Russian · native", "English · learning") and the selected chip's
  30-day word count. The selection drives the score, the focus, and every language-bound skill on
  all three tabs. It is remembered per user (`UserCoachSettings.SelectedLanguage`) and defaults to
  the language with the most words in the last 30 days.

### 3.2 Score and focus card

One `card` with two `card-item` rows, the same component the chat panel uses for Summarize and
Push-to-talk:

- **Score**: "Your score — 69/100" (prefixed by the language name when more than one is spoken),
  caption "▲ 4 this week · What moves it?". Tapping opens the breakdown (§7). Changes v1: the
  badge compared the window with the trailing 30 days; it now compares this ISO week with the
  previous one (§8.3), because a week is what the Progress tab and the weekly note speak in.
- **Focus**: "Working on: filler words", caption with one concrete hint. Tapping opens the skill in
  the Skills tab, where the focus can be changed.

Below the minimum word count the score row reads "Score after N more words" and the focus row is
hidden.

### 3.3 Tabs

`TabPanel` with `BottomHill`, three tabs: **Recent**, **Progress**, **Skills**. The selected tab is
remembered per user in local settings. The v1 window selector (Today / Week / Month / All time)
is removed from the panel; a period chip row survives only inside Skills.

### 3.4 Recent

One card per conversation (§4), newest first, at most 20, independent of the calendar. Each card:

- Title (chat title, or the peer's name), when it started, minutes the user spoke.
- Three findings, each a coloured dot and one sentence in plain language, picked by §5.4.
- Two links: "Marked transcript" (navigates to the chat at the first entry of the conversation,
  the existing jump-to path) and "All numbers" (opens Skills with the period set to that
  conversation's day).
- Footer line: "Older conversations are summed up in Progress".

### 3.5 Progress

- **Better card** (mascot + one sentence). Rendered only when at least one headline skill improved
  against the previous week. Otherwise the tab starts at the score history.
- **Score, last 4 weeks**: four weekly bars.
- **This week vs last week**: one `card-item` per headline skill with "was → now" caption and a
  coloured delta on the right. Language-bound rows follow the selected language.
- **Days with speech**: seven day cells for the current week and "N of 7 · M weeks in a row".
- **Milestones**: a fixed list (§8.4) with achieved dates.

### 3.6 Skills

- Period chips: Last 7 days (default), 30 days, All time. Word count on the right.
- **Headline group** (§5.1), each row: title, band label and value on the right in the band
  colour, one explanatory caption naming the language and its range, and word chips where the
  skill has them. A row is tappable to make it the focus.
- **In conversations** group (§5.2), marked "all languages".
- **More** group: the remaining metrics as plain value rows. "Vocabulary variety 78%" becomes
  "Different words · 78 of 100" with the caption "How many of your words are not repeats. No
  target, just a trend."
- Footer caption: "Tap a marked word in any of your messages to see why it was counted."
- For a selected language without word spaces the language-bound rows read "Not measured for
  Japanese yet" instead of a value (closes the v1 ledger item about the missing per-language
  note; the summary now carries the language).

### 3.7 New user

Replaces the tabs until the first conversation card exists: mascot, "Your speech, read back to
you", one paragraph of promise, one privacy paragraph, a primary button "Record a voice message"
that focuses the recorder, three "What you will see" rows, and the Coaching toggle.

## 4. Conversations

A conversation is the unit of Recent. It is derived, not stored as a first-class entity:

- Own entries in one chat, ordered by `OccurredAt`, where the gap between consecutive own entries
  is at most `CoachScoringSettings.ConversationGap` (default 30 minutes), form one conversation.
- Its language is the language of the majority of its words; mixed conversations show a second
  language tag when the minority exceeds 25% of the words.
- Its talk share and longest monologue come from the `Run` records that overlap it.
- `ICoach.ListOwnConversations(session, count, ct)` returns `ApiArray<CoachConversation>` built
  from `CoachEvents` in one query per call; the compute method is invalidated by the same
  `CoachBackend_Record` command that writes the events. No new table.

`CoachConversation` (Api, MessagePack, array form): `ChatId`, `StartEntryLid`, `StartedAt`,
`EndedAt`, `Language`, `SecondaryLanguage?`, `Words`, `SpeechSeconds`, `Fillers`, `WeakWords`,
`Pace?`, `TalkShare?`, `LongestMonologueSeconds?`, `FillerCounts`, `WeakWordCounts`.

## 5. Skills model

### 5.1 Headline skills

Four rows, ordered by the level of the selected language:

| Level | Order |
|---|---|
| Native | Filler words, Talking pace, Talk share, Longest monologue |
| Learning | Weak words, Different words, Sentence length, Talking pace |

Talk share and longest monologue always appear, either in the headline group or first in the
"In conversations" group.

### 5.2 Language-bound versus conversation-bound

| Language-bound (per language) | Conversation-bound (all languages) |
|---|---|
| Filler words, Filled pauses, Talking pace, Weak words, Repeated words, Different words, Sentence length, Profanity, Questions | Talk share, Longest monologue, Interruptions, Patience, Pauses |

Language-bound skills are aggregated, banded, scored and tipped per language. Conversation-bound
skills sum across languages.

### 5.3 Bands per language

`CoachScoringSettings` gains per-language tables, keyed by ISO code with a `""` fallback:

- `PaceByLanguage` (exists, empty). Seed:

| Language | Slow | Fast |
|---|---|---|
| en | 130 | 170 |
| ru, uk, pl, cs | 100 | 140 |
| de | 110 | 150 |
| es, it | 150 | 200 |
| fr, pt | 140 | 180 |
| fallback | 110 | 160 |
| ja, zh, th, km, lo, my | no band, no pace |

- `FillerByLanguage` (new): good and high rate per language, seeded with the global values for
  every language. The filler band is lenient: research says only excessive rates hurt.

Skill captions state the language and the range: "110 to 140 is easy to follow in Russian".

### 5.4 Findings on a Recent card

Pick three, in this order, stop when three are chosen:

1. Any headline skill in the High / Fast / Slow / Long band for this conversation (worst first).
2. The focus skill's value, whatever its band.
3. Any headline skill in the Good band, best first ("2 filler words in 12 minutes. Best this week"
   when it is the week's best).
4. Remaining headline skills by their order.

## 6. Data model

### 6.1 Days per language

`DbCoachDay` and `CoachDay` gain a `Language` key (ISO code, `""` for entries without one). The
primary key becomes `(UserId, Day, Language)`. `CoachDayBuilder` groups records by language.
Conversation-bound fields are summed across a day's language rows when the panel needs the whole
day. The existing `Coach_RebuildOwnDays` command and the nightly rebuild backfill rows from the
event log; the migration truncates `coach_days` and rebuilds, no data is lost because the event
log is the source of truth.

`ICoach.GetOwnSummary(session, window, language, ct)` and `ListOwnDays(session, range, language,
ct)` take the language (`null` = every language, used for conversation-bound skills and the
new-user check).

### 6.2 Settings

`UserCoachSettings` gains:

| Key | Type | Default | Meaning |
|---|---|---|---|
| 4 | `CoachScope Scope` | `Everywhere` | `Everywhere` or `OnlyChosen` |
| 5 | `bool SkipPeerChats` | `false` | Leave one-to-one chats alone |
| 6 | `ApiMap<string, CoachLanguageLevel> Languages` | empty | ISO → `Native` / `Learning` / `Off`; absent = `Native` |
| 7 | `ApiMap<string, CoachMetricKind> FocusByLanguage` | empty | Absent = automatic (§8.1) |
| 8 | `string SelectedLanguage` | `""` | Chip selection; `""` = most spoken |
| 9 | `bool AreMarksEnabled` | `true` | Marks in own transcripts |
| 10 | `bool IsWeeklySummaryEnabled` | `true` | §8.5 |

`ChatUserSettings` gains `[Key(9)] bool? IsCoachingEnabled` (null = inherit). A place's flag is the
same field on the record keyed by the place's root chat id (`ChatId.RootChatId`).

### 6.3 Bands calibration (phase 2, recorded here so the schema allows it)

A nightly job computes, per language with at least 200 speakers, the 25th and 75th percentiles
of pace and filler rate over the last 90 days and writes them to a `coach_bands` table. The
scoring reads the table first and the config seed second. Not built in v1.1. This keeps the v1
rule of no cross-user ranking: the table holds two aggregate numbers per language and nothing
about any user is compared with anyone else's number.

## 7. Score explanation

Tapping the score opens a sheet listing each scored skill with its weight, its band and how many
points it adds or removes, sorted by loss. The arithmetic is the existing `CoachScoring.Score`,
exposed as `CoachScoring.Explain(day, settings, language)` returning `ApiArray<CoachScorePart>`
(`Kind`, `Weight`, `Band`, `Points`, `MaxPoints`). The score is per language; the turn-taking
part uses the day's summed conversation rows.

## 8. Focus, reinforcement, tips

### 8.1 Focus skill

Automatic focus for a language = the headline skill (for its level) with the worst band over
the last 7 days, ties broken by the headline order; needs at least `MinScoreWords` in that
language. The user overrides it by tapping a skill row in Skills; the override lives in
`FocusByLanguage` and is cleared by "Automatic" in the same sheet. The focus hint text is a
per-skill catalog string with the top word inserted where the skill has chips.

### 8.2 Positive tips

New tip kind `Clean`: fires when the last `TipWindow` (20 min) of one language has at least
`TipMinWords` words and zero fillers and weak words, at most once per calendar day, subject to the
same interval and toggles as other tips. Card copy: "20 minutes, no filler words. Keep going."

### 8.3 Weekly deltas

Delta = this ISO week versus the previous, both needing at least `MinScoreWords` in the selected
language; otherwise the row shows "not enough speech". Improvement direction per skill is fixed in
`CoachLabels` (lower is better for fillers, closer to the band for pace, closer to fair share for
talk share, shorter for monologue).

### 8.4 Milestones

Computed from days, never stored: first 1,000 / 10,000 / 100,000 words analysed; first week with
at least 5 speaking days; first week under the good filler rate; first week with no monologue
over the long threshold; first month with a rising score. The achieved date is the first day that
satisfies it.

### 8.5 Weekly summary

Monday 09:00 local (reuse the digest's delivery-time and time-zone settings): one in-app note in
the notifications panel, "Your week with the coach", holding the score delta, the focus skill's
delta and the best conversation. Sent only if the user spoke at least `MinScoreWords` that week.
Email delivery is an open question (§14).

## 9. Settings page

Behind the gear, replaces the panel body, back arrow plus "Coach settings". Built from
`TileTopic`, `Tile`, `TileItem` and `Toggle`:

1. **Coaching** toggle with caption "Tips, marks and instant analysis of your voice messages.
   Off deletes nothing." Its meaning is unchanged from v1: on = immediate tagging, tips and
   marks; off = the background per-conversation analysis still runs, so the panel has history
   the day it is switched on. Stopping analysis for a chat or place is what §9.1 is for.
2. **Where**: "Coach me in" value row (Everywhere / Only where I say, opens a picker); "Skip
   one-to-one chats" toggle; the list of switched-off chats and places, each with "Switch on".
   Caption: switch any chat or place off from its menu.
3. **Languages** with a **Manage** link in the topic row that opens Settings › Voice &
   Transcription. One row per spoken language (primary, second, third) with the 30-day word count
   and a value row Native / Learning / Do not coach.
4. **How you hear from the coach**: Marks in my messages, Live tips, At most (tip interval, adds
   "One per conversation"), Weekly summary.
5. **Your data**: privacy caption and a danger row "Delete all coaching data" (confirm dialog,
   then `Coach_DeleteOwnData`, which removes events, days, tips and settings).

The chat and place menus replace the v1 "Speech coach" entry (which opened the panel) with a
"Coach me here" toggle entry; opening the panel stays on the Chat | Coach switch.

### 9.1 Scope resolution

For an entry in chat C of place P (P may be none), the coach analyses and stores the entry only
if the chat is in scope:

`ChatUserSettings(C).IsCoachingEnabled` is true, or null and `ChatUserSettings(P.RootChatId)` is
true, or both null and (`Scope == Everywhere` and not (`SkipPeerChats` and C is a peer chat)).

Out of scope means no row on either shard, on both the immediate and the per-conversation
path. The check lives in `CoachAnalysisBackend` before analysis. A language whose level is
`Off` is analysed for code metrics (so conversation-bound skills stay whole) but never tagged,
scored or tipped. The global Coaching toggle keeps its v1 meaning (§9, item 1) and does not
affect scope. Changing a flag does not retroactively delete analyses (the user has "Delete
all" for that) and re-evaluates nothing.

## 10. API changes

`ICoach`:

- `GetOwnSummary(session, window, language?, ct)`, `ListOwnDays(session, range, language?, ct)`.
- `ListOwnConversations(session, count, ct)`, `ListOwnLanguages(session, ct)` (ISO, level, words
  in 30 days), `GetOwnWeeklyDeltas(session, language?, ct)`, `ExplainOwnScore(session,
  language?, ct)`, `ListOwnMilestones(session, ct)`.
- Commands: `Coach_SetFocus(language, kind?)`, `Coach_SetLanguageLevel(language, level)`,
  `Coach_SetScope(scope, skipPeerChats)`, `Coach_SetChatCoaching(chatId, bool?)`,
  `Coach_DeleteOwnData`. Toggles for marks, tips, interval and weekly summary go through the
  existing settings update path.

`CoachWindow` gains `Days7`, `Days30` for the Skills chips; `Today`, `Week`, `Month` stay for
compatibility and tests.

## 11. UI components

Reuse first, following the app's settings vocabulary read from the running app:

- `Tile`, `TileItem`, `TileTopic`, `Toggle`, `TabPanel` / `TabDef` with `BottomHill`,
  `Banner` / `TimerCloseButton` (tips), `AvatarCircle`, `ButtonRound`, `MenuEntry`, the chat
  panel's `card` / `card-item` markup, `status-badge`.
- New: `CoachConversationCard`, `CoachScoreCard`, `CoachSkillRow` (replaces `CoachMetricRow`),
  `CoachWeekDeltas`, `CoachMilestones`, `CoachLanguageChips`, `CoachSettingsPage` (replaces
  `CoachSettingsTile`), `CoachScoreSheet`, `CoachEmptyState`. All under
  `UI.Blazor.App/Components/Coach`; nothing here is reusable outside the coach.
- `CoachDayChart` stays for the score history and days-with-speech cells; `CoachTrends` and the
  composition donut are removed from the panel (the donut had no decision behind it). Changes
  v1: the Trends screen is folded into Progress; the v1.1 tone slot moves to Skills when tone
  ships. Mobile stays as v1: the same panel as a full-screen page from the chat header.

## 12. Localization

Every visible string is a `Coach_*` key in all 19 hand-written catalogs plus the typed member,
then `derive-bcms` and `derive-max`. New groups: tab names, card findings (one `_Format` per
skill and band), focus hints per skill, band captions per skill with a `{0} to {1}` range,
settings rows and captions, milestones, the weekly note, the score sheet, the "Coach me here"
menu entry, the new-user state.

## 13. Testing

- Unit (`Users.UnitTests/Coach`): conversation grouping (gap, majority language, secondary tag,
  run overlap); per-language day builder and backfill; scope resolution table (chat, place,
  global, peer skip); automatic focus per level; `Clean` tip rule and its once-a-day cap; score
  explanation sums to the score; weekly deltas and their direction; milestone dates.
- Integration (`Users.IntegrationTests/CoachTest`): conversations across two chats and two
  languages; day rows split by language after rebuild; scope flags stop analysis; delete removes
  everything.
- bUnit (`Chat.UI.Blazor.UnitTests`): skill row bands and captions per language; findings order
  on a card; settings rows reflect and update settings.
- Blazor integration (`Chat.UI.Blazor.IntegrationTests/CoachUITest`): tabs remembered; language
  chips switch score and skills; new-user state until the first card; gear opens settings and
  the Manage link opens Voice & Transcription; "Coach me here" from the chat menu.
- Waits follow `docs/testing/waiting.md`.

## 14. Open questions

1. `SkipPeerChats` default. Spec says `false` (coach everywhere) so bilingual and coaching data
   exists from day one; the research argues casual private talk is where coaching feels wrong.
2. Weekly summary channel: in-app note only (spec), or also the email digest.
3. Whether "Coach me in: Only where I say" is worth shipping in v1.1 or the per-chat opt-out
   alone covers it. Spec keeps it; it is one enum and one picker.
4. Carried from the v1 ledger: whether the tip's close is a dismiss or a snooze (v1 dismisses;
   with the 30 s auto-close the difference is small), and the dashed sentence underline in the
   desktop mock (still no defined meaning, still not implemented).

## 15. Reuse

Existing abstractions to reuse: `ICoach` / `CoachBackend` and the `CoachEvents` log as the single
source of truth; `CoachDayBuilder`, `CoachScoring`, `CoachTipPolicy`, `CoachLabels`,
`CoachDayRange`; `StoredSettings` / KVAS for every setting (`UserCoachSettings`,
`ChatUserSettings`); `ChatId.RootChatId` for place scope; the digest's delivery time and time
zone for the weekly note; the notifications panel for delivery; `TabPanel`, `Tile*`, `Toggle`,
`Banner`, `MenuEntry`, `AvatarCircle`, `ButtonRound`, `TimerCloseButton`; `CoachDayChart` and
`BarChart` for the score history; `TestWait`, `BlazorTester`, `FakeTagger` for tests.

New components and their placement: everything new is coach-specific and stays in
`ActualChat.Users` (backend), `ActualChat.Api` (contracts) and `UI.Blazor.App/Components/Coach`.
`CoachConversation` grouping is a pure function over records and goes next to `CoachDayBuilder`
in `Api/Users/Coach` so both server and tests share it. Nothing here belongs in `Core`.
