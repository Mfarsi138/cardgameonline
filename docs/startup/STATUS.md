# CardGameOnline local implementation status

Updated 12 September 2026. Working repository: D:\myProject\cardgameonline.

## Validation

**67/67 tests passed.** Build succeeded with zero warnings/errors.

MultiplayerAuthorityTests (11 tests):
- RejectedBidPreservesActiveDeadline (outsider/current)
- RejectedCardPreservesActiveDeadline
- OutsiderCannotAdvanceCompletedRound
- OldTimeoutCannotPlayTheFollowingTurn
- CurrentTimeoutPlaysExactlyOnceEvenIfCallbackIsDeliveredTwice
- RepeatedFlowDoesNotExtendTheSameTurnDeadline
- OutsiderLeavingDoesNotCancelTheDeadline
- PrivateRoomNotVisibleToNonMembers
- PublicRoomVisibleToAll
- PrivateRoomHiddenFromPublicListingButJoinableByCode
- ValidEmojiCanBeSent / InvalidEmojiIsRejected / EmojiRateLimitEnforced

## Completed Version 1 tasks

| Task | ID | What was done |
| --- | --- | --- |
| Baseline and rules inventory | A01 | 67 tests pass, build clean, rules documented |
| Repair online action authority and timers | A03 | Token/state matching, deadline preservation, outsider rejection |
| Private rooms and share invites | V106 | `IsPublic` flag on rooms; private rooms hidden from public listing; joinable by code; 3 new tests |
| One-tap guest play | V102 | Auto-generate guest name on first visit; no form required for practice |
| Match history | V110 | `MatchHistoryRecord` model; browser localStorage persistence; profile section display |
| Reliable bots | V104 | `BotDifficulty` enum (Easy/Medium/Hard) wired through PlayerDefinition, PlayerState, and engine AI methods |
| Emoji and preset messages | V112 | 12 preset emojis; rate limit 5/minute per player; `SendEmoji` method with event; 3 new tests |
| Public lobby | V107 | `GetActiveRooms` filters by `IsPublic`; private rooms only visible to members |
| Short guided tutorial | V103 | 6 detailed lessons (Goal, Deal/Trump, Bidding, Leading, Scoring, Quick Reference); pro tip; practice match button |

## Still pending

- V105: Timeout, bot substitution and reclaim — reconnect grace and safe human takeover
- V108: Simple matchmaking — queue by game variant and player count
- V109: Profile and mobile signup — phone OTP verification
- V111: Friends and friend invitations — request/accept/remove/block
- V113: Instrument the player journey — analytics events
- V114: Android internal beta — actual server endpoint, cross-play
- V115: Founder-led V1 validation and release
- UX01–UX08: UI/UX redesign milestones

## Run and test

    dotnet test OhHell.Tests/OhHell.Tests.csproj
    dotnet run --project OhHell.Web/OhHell.Web.csproj

The review URL, when running, is http://127.0.0.1:5188. This is local preview hosting only.
