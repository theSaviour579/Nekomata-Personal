# Nekomata Personal 0.5.0

This release brings the personal edition much closer to a proactive daily assistant, with Microsoft 365 task awareness, Copilot conversations, improved follow-up support and a clearer start-to-finish workday flow.

## New features

### Microsoft 365 Copilot conversations

- Use an eligible Microsoft 365 Copilot licence for Guardian conversations.
- Choose between Microsoft 365 Copilot, organisation-provided Microsoft AI, OpenAI or no AI.
- Test Copilot access directly from Personal Settings.
- Successful testing now selects Copilot automatically; saving remains an explicit user action.
- Optional web grounding is controlled separately and remains off unless enabled.
- Copilot never silently falls back to a chargeable provider.

### Microsoft To Do and Planner

- Import open Microsoft To Do tasks into Guardian's workspace.
- Import basic Planner tasks assigned to the signed-in work or school user.
- Rank imported tasks alongside local objectives and use them in Guardian conversations and daily planning.
- Collapse likely duplicates exposed through both To Do and Planner, preferring the Planner record.
- Show To Do and Planner counts and connection health in Integrations and Diagnostics.
- Task access is read-only in this release.

### More autonomous daily support

- Proactive next-action recommendations based on current priorities and available time.
- Adaptive replanning when circumstances or availability change.
- Morning pre-flight route map with progress and completion states.
- Daily wrap-up and work reconciliation.
- Retrospective work logging for objectives completed outside the original plan.
- Decision-learning support so Guardian can improve future suggestions from accepted or changed recommendations.
- Meeting preparation prompts based on available calendar and workspace evidence.

### Follow-ups and Outlook

- Review inbox and sent-mail evidence when determining whether something genuinely needs chasing.
- Distinguish acknowledgements from substantive replies.
- Link the correct conversation to an item for better future matching.
- Prepare editable reply-all drafts using the active Microsoft 365 desktop Outlook experience.
- Keep sending under the user's control.

### Personal integrations

- Attach supported text and data files to Guardian conversations.
- Choose Spotify, YouTube Music or a favourite radio web player as the preferred music source.
- Save a station or music-page URL and open it from the dashboard.
- Improved Spotify connection and arrival-playlist controls.

### Onboarding, privacy and diagnostics

- Clearer first-run explanation of Microsoft access, Copilot and optional AI providers.
- Personal-focused Guardian language and removal of work-only health warnings.
- Scrollable first-run experience for smaller displays.
- Expanded connection diagnostics for Microsoft tasks and optional services.
- Credentials remain outside workspace backups and diagnostic reports.

## Microsoft administrator setup

Read-only task import requires the delegated Microsoft Graph permission `Tasks.Read` on the Nekomata Entra application.

Microsoft 365 Copilot is a preview integration for eligible work or school accounts and requires the additional delegated permissions documented in the application's README. Administrator consent policies may require approval before users can connect.

## Compatibility notes

- Microsoft To Do supports eligible personal and organisational Microsoft accounts.
- Planner import requires a work or school account and currently covers basic Planner tasks exposed by Microsoft Graph; Planner Premium tasks are not included.
- Microsoft 365 Copilot currently provides conversational responses. Structured workspace automation continues to require Microsoft AI or OpenAI.

## Verification

- 153 automated tests passed.
- Clean Windows self-contained publish validated.
- Windows installer build validated.
- Release assets include SHA-256 checksums.
