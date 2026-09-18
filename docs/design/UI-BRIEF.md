# CardGameOnline — screenshot-based UI/UX brief

Updated 8 September 2026. Companion to `../startup/ROADMAP.md`; overrides its earlier restrained luxury visual proposal. Oh Hell launches first; Hokm remains a future expansion. This is a design and implementation brief, not a finished mockup or a claim that the app has been redesigned.

## 1. Reference input is ready

The founder supplied four screenshots of the game previously called “Harvest King.” Use these images directly; do not block on a store link or ask for the same references again. Product title, motion and unseen screens are not independently verified.

| Reference | Visible evidence | Adaptation for CardGameOnline |
| --- | --- | --- |
| 01 — Play/map | Vertical numbered route, side activity buttons, selected-character strip, large yellow Fight control, raised center navigation | Illustrated Play home with dominant Play Oh Hell; a separate tutorial Journey; limited meaningful secondary actions |
| 02 — Artifacts | Warm framed inventory, repeated item tiles, colored frames, compact level/progress labels, consistent bottom navigation | Appearance collection: card backs, avatars, table themes; indicate selected/available states |
| 03 — Deck | Colorful category tabs, selected strip, category filters, portrait grid, raised selected bottom tab | Category tabs for Cards / Tables / Avatars; preview current appearance; equipped state with a text label |
| 04 — Shop | Purple backdrop, large illustrated section panels, banner headings, offer groups and persistent navigation | Distinct illustrated section headers for tutorial, appearance and later events; shop only if separately approved |

Reference images accompany this brief in `references/`. They are design references, not shipping game assets.

## 2. Art direction

Build a **playful, polished illustrated card club**. Use saturated blue/purple menu scenery, warm gold framing, thick friendly outlines, soft inner highlights and buttons that appear pressable. Apply original court-card/suit imagery, table scenes and avatars instead of vegetable characters or battle equipment.

Menus can be lively; the game table needs a calmer background so suits, bids, turns and timers remain immediately readable. Use ivory playing cards, emerald felt and selective golden highlights there. The visual connection comes from shared frames, icons, type and button shapes.

Proposed tokens: blue `#30478D`, purple `#7850BA`, gold `#FFC52E`, warm frame `#A9682C`, cyan `#36B8DF`, outline `#302519`, felt `#124D3D`, ivory `#FFF8E9`. These are starting values, not exact samples or contrast-certified pairs.

Use chunky display headings sparingly, with a dark outline/shadow where legible. Body copy and Persian text need a readable licensed font at normal weight; do not force all text into the screenshot's condensed uppercase style. Reserve high saturation for actions and categories. Icons and color always have a text/state counterpart.

## 3. Navigation and sections

Proposed mobile shell, left to right in the initial LTR prototype:

```text
Collection    Friends    PLAY    Journey    Profile
                        raised
```

For Persian, test intentional mirroring of the side destinations while preserving center Play. Within a match, hide the menu shell and preserve the table space. Desktop uses the same destinations with an adapted layout rather than stretching a phone screenshot.

- **Play:** the main home. Player/guest identity at top; a large illustrated Oh Hell table scene; dominant Play Oh Hell; clear With friends and Practice actions. A resume panel takes precedence when a match is active.
- **Collection:** real card-back/table/avatar choices, preview and equipped state. Reuse current supported themes. In V1, no hero powers, invented rarity economy or purchasable card advantages.
- **Friends:** invitations and social list when implemented. A useful initial state can explain share-room play, but must not imply an unavailable friends backend exists.
- **Journey:** tutorial and practice milestones in V1. A small route map may show genuine completed lessons. Advanced challenges, XP and rewards wait for their V2 implementation.
- **Profile:** guest upgrade/account, history when implemented, and settings. No fake levels or phone verification success.

This is a target shell. Ship a destination only once it has useful working content. Do not add a sixth Shop tab or multiple competing bottom bars to mimic the reference's density.

## 4. First screen compositions

### Play home

Top: avatar/name or Guest, compact settings control. Main illustration: a welcoming card table with original suit/court motifs. Middle: concise Oh Hell description or current match resume. Primary action: large gold Play Oh Hell. Secondary actions: Play with friends and Practice, followed by public-table access where useful. Bottom: raised center Play navigation.

Do not show energy cost, gems, premium pass, advertising badges or time-limited offers. The reference demonstrates visual richness; it does not establish these as requested product features.

### Waiting room

Use a framed table preview with seats, readable names and explicit human/bot indicators. Show room privacy and occupancy, a clear invite/share button, host status and Start match. Explain any missing requirement next to the disabled Start action. Place the invite code in an LTR-isolated text field even in Persian UI.

### Game table

Top/center: public table, trump, round and opponents' public seat information. Bottom: player's hand with exposed ranks/suits and sufficient touch separation. Use a raised bid panel only during bidding. Current-player highlight and server-based time ring must be distinguishable from card selection. Keep score/rules/settings small but accessible. No collection banners or promotional buttons in active play.

### Collection

Illustrated header and three large tabs: Cards, Tables, Avatars. Selected appearance preview above a responsive tile grid. Two or three columns on narrow devices if needed for readable labels; the reference's dense four-column arrangement is not a fixed requirement. Each tile has name, preview, state and one clear select action.

### Journey and result

Journey uses a short lesson path with meaningful names such as Learn bidding and Follow suit. Connect completion only to actual tutorial state. Match result uses a celebratory original card illustration, placements, score explanation and dominant Rematch, with Back to lobby secondary. Show XP later only when the server actually awards it.

## 5. Patterns to adopt carefully

- Raised active tabs: retain the visible selected state and consistent icon sizing, with labels on all destinations.
- Dimensional gold buttons: use a shared pressed/disabled/loading treatment; avoid shifting layout on press.
- Collection frames: indicate appearance categories and selection. Decorative borders must not suggest gameplay strength.
- Illustration-led panels: give each section a distinct identity while retaining consistent spacing and controls.
- Notification badges: show only actionable real state such as an incoming invite; clear when resolved. Do not scatter permanent exclamation marks.
- Progress bars: show actual tutorial progress in V1; XP and season progress require the later backend.

The screenshots do not authorize copying characters/assets, adding ads, paid upgrades, loot draws, energy restrictions, false scarcity or competitive advantages. Keep these outside implementation scope unless the founder explicitly requests them later.

## 6. Build and review sequence

1. Read this brief and the roadmap; capture the current app's actual screens.
2. Produce a reference-to-screen map and tokens. Preserve the existing .NET/Blazor/MAUI architecture.
3. Create inspectable original mockups or an interactive prototype for Play home, Waiting room, Game table, Collection and Journey. Start with the first three before expanding. Include mobile and desktop views and real Oh Hell cards.
4. Present that concrete design for founder feedback before applying it broadly. Do not treat this written brief as a completed visual prototype.
5. Implement a small vertical slice through reusable Razor components and shared styles. Keep engine behavior unchanged; verify existing flows.
6. Finish remaining screens and actual error/loading/reconnect states. Test keyboard, touch, RTL, enlarged text and reduced motion.
7. Capture real rendered implementation screenshots and compare with the agreed proposal. Record untested Android/device behavior explicitly.

Suggested components: `GameNav`, `IllustratedSectionHeader`, `PrimaryGameButton`, `CategoryTabs`, `AppearanceTile`, `PlayerSeat`, `RoomInvitePanel`, `BidPanel`, `TurnIndicator`, `ScoreSheet`, `ConnectionBanner`. Names are proposals, not existing repository classes.

Keep vector/UI primitives code-native where practical. If original raster illustration is needed, obtain or create appropriately licensed assets and keep a provenance record. User-supplied reference JPEGs belong in design documentation, never in production's shipped artwork.

## 7. Design acceptance criteria

- The menu feels colorful, illustrated and tactile, matching the supplied reference direction rather than the earlier subdued luxury proposal.
- Play is the unmistakable primary action and the table remains readable through an entire Oh Hell match.
- Founder can inspect actual rendered proposals, not just descriptions.
- No dead buttons, fictional progress or unsupported purchases are presented as working.
- Cards, tabs and controls remain legible at narrow phone widths; input targets aim for at least 44×44 CSS pixels.
- Header/navigation respects Android safe areas; the table does not inherit every decorative menu panel.
- Persian text and LTR room codes work together; table direction follows rules rather than automatic RTL mirroring.
- Static references are not cited as proof of animation quality or interaction usability.
- Existing rules and multiplayer regression checks pass after component refactoring; visual review does not replace functional tests.

## 8. Hermes handoff

The design reference is now supplied. Update UX01–UX08 in the startup roadmap accordingly. Start UX01 and then create the first three mockups under UX02 using this illustrated direction. Continue independent reliability fixes while the visual proposal is being reviewed. Use one active agent at a time and the existing free-model policy. Do not require another reference link before beginning.
