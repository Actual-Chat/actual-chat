# Speech coach: skill progress in Recent

Status: design A selected by the user on October 2, 2026; implementation pending.

## Problem and decision

The current Recent panel works well as a feed of real conversations, with findings,
marked-transcript links, and exclusion controls. It does not make changes in
individual skills easy to see without switching to Progress.

Add one compact weekly skill summary above the existing conversation cards.
Keep the current header, score/focus card, language selector, tabs, and conversation
feed. Use the existing Voxt typography, cards, colors, and spacing.

The selected approach is **A: weekly summary**. Alternative B added a focus-skill
comparison inside every conversation card; defer it because it adds repetition
and requires a separate comparison baseline for conversations.

## References and mock

- [Figma reference](https://www.figma.com/design/EJxXOzem02zhRvuqsJIool/Actual-chat-Desktop?node-id=29239-852599): skill trends, values, and week-over-week changes.
- Local review artifact: `tmp/speech-coach-review/recent-progress-mocks.html`.
- The artifact compares Current, A, and B, with light/dark switching and an
  insufficient-data state. It was visually checked in Chrome at desktop and
  390px mobile widths. Progress values are illustrative, not live analytics.
- Temporary artifacts are not durable repository documentation. Recreate the
  mock from this plan if those files are unavailable.

## Selected UI

Place the summary as the first item under the Recent tab, inside its scrollable
content. Conversation cards below it retain their current behavior.

The mock contains three compact rows:

| Skill | Indicator | Meaning |
| --- | --- | --- |
| Filler words | Current rate, previous-value rail, and directional delta | Fewer fillers per 100 words is better; express rate differences in percentage points. |
| Speaking pace | Current words/minute and marker in a comfortable range | Faster is not automatically better; show position relative to the language-appropriate range. |
| Weak words | Current rate and change label | Show improvement, worsening, or no change using the existing metric interpretation. |

Include the selected language and a clear comparison-period label. A compact
coverage caption can show eligible conversations and speaking time when those
figures are available for the exact same period and language.

The summary heading links to Progress. Tapping a skill should open Progress with
that skill visible or expanded. Values and labels must remain understandable
without relying on color or reading a bar's length.

## Data and comparison rules

- Compare the same language and metric definition. Follow the panel's selected
  language; do not mix languages into a comparison unintentionally.
- Reuse existing eligibility and minimum-speech rules. Missing or insufficient
  data is not zero, improvement, or a completed progress bar.
- When the current value exists but the previous period is insufficient, show
  the value with a short explanation instead of a delta or previous-value rail.
- Excluded conversations must not affect the summary. Exclusion and language
  changes should update it reactively.
- Use rates rather than raw word counts for comparisons between periods with
  different amounts of speech.
- The mock says “7 days compared with the previous 7 days,” but the existing
  `ICoach.GetOwnWeekDeltas` compares the current calendar week with the previous
  calendar week. For the first implementation, reuse that API and label the
  summary “This week” / “Compared with last week.” Do not present calendar-week
  values as rolling seven-day values.
- The Figma vocabulary card counts new words and their later reuse. Those are
  separate features whose data is not established by the current metrics;
  do not imply that they are part of this change.

## Reuse

### Existing abstractions

- `CoachRecentTab` and `CoachConversationCard`: retain the current feed and its
  transcript/exclusion actions.
- `ICoach.GetOwnWeekDeltas` and `CoachWeekDelta`: source the current and previous
  values, band, and improvement verdict.
- `CoachProgressBuilder.WeekDeltas`: reuse metric interpretation and eligibility
  through the existing service, rather than calculating a second set of deltas
  in the UI.
- `CoachLabels.MetricTitle`, `DeltaValue`, and `DeltaBadge`: reuse localized
  metric names and formatting where they fit the compact layout.
- `CoachUI.GetSelectedLanguage` and `SelectTab(CoachTab.Progress)`: reuse panel
  language and tab navigation. Selecting a particular skill within Progress
  still needs a small UI state/navigation extension.
- `Card`, existing coach CSS/tokens, and the typed localization catalog: reuse
  appearance and localization; add new strings across all supported languages.

### Placement of new components

A proposed `CoachRecentSkillSummary` belongs beside the other Coach components,
with its styles in `coach.css`: its content and navigation are coach-specific.
A shared Blazor component is an option only if its indicator also has another
consumer. Do not add a general progress system for this card alone.

If implementation requires genuinely reusable numeric comparison or range
logic, compare feature-local placement with `ActualChat.Core`; prefer Core for
logic independent of coach, server, and UI dependencies. Reuse existing scoring
logic first. No new server service or TypeScript component is currently required.

## Implementation and validation

1. Add the compact summary to Recent using real weekly deltas and truthful
   calendar-week labels. Keep mock values out of product code.
2. Add navigation from the heading and individual skill rows to Progress.
3. Implement current-only, insufficient-data, and unchanged states; make new
   controls accessible by keyboard and localize their text.
4. Verify the existing feed, transcript links, and exclusion controls still work.
5. Cover language changes, exclusion invalidation, improvement/worsening,
   missing baselines, and navigation with focused tests.
6. Check the actual app in Chrome: desktop and mobile, light and dark themes,
   long translations, and insufficient-data states. Confirm the new card does
   not prevent users from scanning recent conversations.

Success means users can identify which skill changed and over what period from
Recent, while still reaching their conversation evidence directly. Native device
behavior remains a separate validation step; Chrome checks do not cover it.

## Outside this change

Per-conversation comparisons, new-word/reuse tracking, comparisons with friends,
new scoring formulas, and a redesign of the Progress or Skills tabs are deferred.
The earlier filler-context work is separate: PR #5030 introduced context cards,
recommendations, separate navigation/replay actions, and a 20-occurrence hint.
