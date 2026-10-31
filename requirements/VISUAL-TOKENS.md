# Specula Color and Visual Token Contract v0.1

Status: requirements, not implementation. Source of truth: `kemiller2002/forma` token source `tokens/echelon.tokens.json`, `docs/COLOR.md`, and the color contract shipped with the exact pinned Forma release. Echelon Foundry site is the visual reference. Verify the release version and shipped theme/brand manifest before implementation.

## Brand foundation

These are documented Forma source primitives, NOT permission to hard-code their hex values throughout Specula:

| Primitive | Source value | Intended identity |
| --- | --- | --- |
| Foundry charcoal | #202421 | Primary dark brand surface |
| Forged iron | #3a403c | Secondary dark surface |
| Parchment | #f2efe7 | Warm light brand surface |
| Oxide bronze | #905831 | Brand accent |
| Oxide bronze light | #a87e5e | Dark-surface accent candidate |
| Verdigris | #47756b | Secondary brand accent |
| Verdigris light | #698d84 | Dark-surface accent candidate |
| Carbon | #171a18 | Deep dark surface |
| Graphite | #686d68 | Muted neutral |
| Stone | #e3e0d7 | Light border/neutral |

Use the shipped Echelon brand/theme mapping from the pinned Forma package. Do not copy primitives into Specula CSS or substitute raw hex literals for public semantic tokens. If an intended primitive lacks an exported semantic role, resolve it through Forma's brand manifest, not an undocumented `--ef-*` token.

## Required public semantic roles

- Page and card surfaces: `--ef-color-surface-primary`, `--ef-color-surface-secondary`.
- Text: `--ef-color-text-primary`, `--ef-color-text-inverse`.
- Primary action and active selection: `--ef-color-surface-inverse` with `--ef-color-text-inverse`.
- Links: `--ef-color-accent-primary`; ensure supported pairing for actual surface.
- Focus: `--ef-color-focus-ring` with `--ef-color-focus-gap`.
- Boundaries: `--ef-color-border-functional`.
- Success: `--ef-color-status-success`.
- Failure: `--ef-color-status-danger`.
- Warning/stale/partial: `--ef-color-status-warning`.
- Notice/pending/unknown: `--ef-color-status-info`.

Use only token names present in the pinned release's machine-readable color contract. Forma's `docs/COLOR.md` explicitly forbids invented public token names even with CSS fallbacks.

## Screen-by-screen application

### Repository picker
- Page: primary surface, high-contrast primary text.
- Input and selection rows: secondary surface with functional boundary.
- Primary 'Observe repository' action: inverse surface/inverse text.
- Validation errors: danger role with explicit error text.
- Recent repository choices: neutral secondary action, not success color.

### Overview
- Header/repository identity: primary surface and primary text; active repository indicator uses selection role.
- HEAD CI status: success only if check evidence is tied to current HEAD; danger for failed; info for running/unknown; warning for partial/stale.
- Evidence coverage: neutral/info for unavailable, warning for partial, success only when explicitly verified.
- Metrics: neutral cards. Do not color all metric backgrounds by status.
- Needs attention: danger or warning icon/text with semantic status role, neutral card surface. No full red or green recommendation panel.
- Activity: neutral rows; status glyphs may carry semantic color alongside labels.

### Activity
- Neutral feed with subtle functional boundaries.
- Filter selection uses inverse surface and inverse text; unselected filters use secondary action pairing.
- Event type is conveyed by icon and text; do not assign arbitrary categorical colors.
- Workflow outcome uses status semantic role only when verified.

### Verification
- Current SHA check status uses the semantic roles above.
- Failures and blockers appear before success; neutral cards with semantic labels/icons.
- Unknown or absent evidence is NOT success and must never be colored green.
- Links use accent-primary and are visibly recognizable without relying on color alone.

### Replay
- Planned milestones: neutral/info with dashed boundary or explicit 'Planned' label.
- Observed events: neutral source-linked event markers; status roles only for known outcomes.
- Presentational emphasis is typography and layout, not decorative color gradients.
- Judge view preserves source links, evidence caveats and accessible contrast.

## Theme and accessibility requirements

- Support the actual shipped light and dark Echelon brand theme, including system preference where supported.
- Every semantic foreground/surface combination must meet the pinned Forma color contract; status foregrounds are contrast-gated at 4.5:1 on primary surface according to Forma documentation.
- Test focus indicators on every effective surface, including nested cards and dialogs.
- Never communicate meaning solely through hue; provide text, icon, pattern or state.
- Test forced colors, grayscale, common color-vision simulations, 200% text scaling, mobile safe areas, and reduced motion.
- Validate application CSS against the pinned package's color contract; use its documented `forma-color validate` command or equivalent package API.
- Any translucent or mixed colors must be checked after browser composition, not merely against their source token.
- No raw CSS color literals in application components except justified, reviewed external content or a separately versioned brand manifest.
- Screenshot-test Overview, Activity, Verification, Replay, picker, loading, empty, partial, failure, offline and rate-limited states in light and dark themes.

## Implementation gate

Before writing Specula UI:
1. Pin a released Forma package version and read its actual `contracts/color-contract.json`, brand manifests and supported token pairings.
2. Confirm Echelon Foundry site's active brand/theme mapping; note that the site's current package dependency may lag Forma's repository version.
3. Record selected theme/brand manifest and exact public token names in implementation evidence.
4. Use Forma primitives for spacing, typography, radius and elevation; derive a component-role table from the pinned package rather than inventing numbers.
5. Verify visual screenshots and CSS contract validation. A compiling app is not sufficient visual acceptance.
