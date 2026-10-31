# Specula UI refinement report

Date: 2026-10-31  
Deployed application: <https://specula.echelonfoundry.com/>  
Implementation PR: <https://github.com/kemiller2002/specula/pull/5>  
Implementation commit: `1bb5881aae84a95095ee968279cdca1d55cebbf2`  
Merged and deployment-verified commit: `5bcfbe54e98c7e6fcd964afa511211ba7f123f42`

## Outcome

Specula now presents the Echelon light theme to first-time visitors, while preserving explicit dark and system preferences. The synchronous theme bootstrap runs before the application becomes visible, so a saved dark preference does not flash through light and a new visitor does not flash through dark.

The interface now has a deliberate information hierarchy: the page purpose and current HEAD lead, blockers receive restrained emphasis, primary engineering facts use quieter surfaces, and metadata and source actions remain secondary. Activity rows are denser, filters use segmented controls with a non-color selected indicator, and repository context remains visible without overwhelming the header.

Replay and Settings retain native date/time inputs for reliable platform behavior, but group them with Forma form rows, labels, descriptions, surfaces, focus states, and 44px minimum targets. The theme selector uses the released Forma select pattern. No application behavior, GitHub API interpretation, evidence semantics, route, or engineering metric changed.

F# remains responsible for application time interpretation. Display values now use the viewer's local zone with unambiguous human-readable forms such as `Oct 31, 2026 · 8:42 AM (UTC−04:00)`, plus `Today` and `Yesterday` where appropriate. Relative event times retain exact ISO-backed, accessible labels. UTC timestamps and ordering are unchanged.

## Forma assessment

Specula already pinned Forma `0.13.0`, the newest published stable release inspected during this work and the repository's verified compatible version. Its public tokens and released form, select, segmented-control, feed, surface, metric, and key-value patterns covered the refinement. The CSS color contract confirms no raw color literals were added.

No reusable Forma defect was found, so no upstream Forma change or release was required. The existing dirty local Forma checkout was inspected but not modified.

## Verification results

Final local verification, run from a clean build of the implementation commit:

| Check | Result |
| --- | --- |
| Dependency baseline | Passed; 6 components consistent |
| Forma color contract | Passed; 1 CSS file valid against Forma 0.13.0, no raw color literals |
| F# unit tests | Passed; 66 passed, 0 failed, 0 skipped |
| Release WebAssembly build | Passed; `dist/` produced |
| Chromium acceptance and layout suite | Passed; 42 passed, 0 failed |
| Mobile WebKit suite | Passed; 2 passed, 0 failed |
| Whitespace validation | Passed; `git diff --check` clean |

The browser suite covers 320, 375, 390, 430, 768, 1024, and 1440 CSS-pixel widths; every primary screen, Settings, and About; both themes; first-visit light and saved dark/system preferences; no horizontal overflow; 200% text scaling; labelled controls and unique IDs; keyboard focus; computed foreground/background contrast; reduced motion; native date/time containment; and a 390px/1440px screenshot matrix.

Focused iPhone WebKit coverage verifies that native Replay date/time values remain editable and contained, and that the Settings inputs, buttons, and select remain at least 44px high, legible, and free of horizontal overflow in dark mode.

GitHub Actions run <https://github.com/kemiller2002/specula/actions/runs/41205426299> passed all four jobs:

- `build-and-test`, including F#, Chromium, and mobile WebKit
- `live-github`, against the public GitHub API
- `deploy`, publishing GitHub Pages
- `deployed-smoke`, confirming the exact commit, WebAssembly content type, and live application behavior

## Visual evidence

Baseline screenshots were captured from the previously deployed application before mutation at 390px and 1440px. After screenshots use deterministic GitHub fixtures and cover every screen in light and dark at both sizes. Every generated image was inspected, not only asserted geometrically.

| Screen | Before | After light | After dark |
| --- | --- | --- | --- |
| Overview | [1440px](screenshots/ui-refinement/before/overview-1440-light.png) | [1440px](screenshots/ui-refinement/after/overview-1440-light.png) | [1440px](screenshots/ui-refinement/after/overview-1440-dark.png) |
| Activity | [390px](screenshots/ui-refinement/before/activity-390-light.png) | [390px](screenshots/ui-refinement/after/activity-390-light.png) | [390px](screenshots/ui-refinement/after/activity-390-dark.png) |
| Verification | [1440px](screenshots/ui-refinement/before/verification-1440-light.png) | [1440px](screenshots/ui-refinement/after/verification-1440-light.png) | [1440px](screenshots/ui-refinement/after/verification-1440-dark.png) |
| Replay | [390px](screenshots/ui-refinement/before/replay-390-light.png) | [390px](screenshots/ui-refinement/after/replay-390-light.png) | [390px](screenshots/ui-refinement/after/replay-390-dark.png) |
| Settings | [1440px](screenshots/ui-refinement/before/settings-1440-light.png) | [1440px](screenshots/ui-refinement/after/settings-1440-light.png) | [1440px](screenshots/ui-refinement/after/settings-1440-dark.png) |
| About | [390px](screenshots/ui-refinement/before/about-390-light.png) | [390px](screenshots/ui-refinement/after/about-390-light.png) | [390px](screenshots/ui-refinement/after/about-390-dark.png) |

The complete evidence set is in [`evidence/screenshots/ui-refinement`](screenshots/ui-refinement/).

## Deployed visual verification

After Pages served `5bcfbe54e98c7e6fcd964afa511211ba7f123f42`, the deployed application was opened in a fresh browser session and visually inspected with live `kemiller2002/specula` data.

- The repository picker opened directly in the warm light theme and displayed build `5bcfbe5`.
- Overview displayed the current merged HEAD, clear page purpose, restrained attention surface, and passing HEAD state.
- Activity displayed compact source-linked event cards, secondary human-readable timestamps, and restrained segmented filters.
- Replay displayed intentional native Start and End controls with local-zone context and legible calendar affordances.
- Settings displayed contained competition inputs and the Forma select pattern.
- Selecting Dark changed the computed Echelon surface to `rgb(23, 26, 24)` with foreground `rgb(242, 239, 231)`; a full reload retained the saved dark preference.

## Remaining issues

No blocking UI or functional defects remain from this scope.

The build continues to emit existing FSharp.Core linker warnings, and GitHub Actions currently emits upstream Node 20 deprecation annotations for several pinned actions. Both are non-failing toolchain notices and were not introduced or weakened by the UI work. Physical-device Mobile Safari was not available; the native controls were instead verified with Playwright's iPhone WebKit profile, alongside Chromium coverage and live production inspection.
