# Specula

Specula is Echelon Foundry's independent, read-only observer for autonomous software engineering.

Point Specula at a public GitHub repository to inspect commits, pull requests, CI results, deployments, recorded decisions, verification evidence and a replayable timeline. It never directs agents or changes the observed repository.

**Live:** https://specula.echelonfoundry.com/

## What it shows

- **Overview** — current HEAD, SHA-bound check status, evidence coverage, what needs attention, and three separate dimensions: *Activity* (what happened), *Verification* (what passed) and *Efficiency* (what was measurably achieved).
- **Activity** — source-linked commits, pull requests, workflow runs, HEAD check runs and deployments, filterable, with bounded "load more".
- **Verification** — HEAD checks only for the exact HEAD SHA; build vs deployment vs deployed-application smoke test judged separately; requirements, decisions and corrections only from supported, versioned evidence; metric definitions.
- **Replay** — a chosen time window, planned versus observed milestones, competition clock and a presentation view.
- **Settings / About** — competition clock per repository, theme, saved preferences, build provenance and pinned component versions.

Unknown is never shown as success. Missing evidence is reported as unavailable, never as zero.

## Architecture

- **F#** application engine (`src/Specula.Engine`): state, transitions, GitHub normalization, SHA binding, evidence adapters, metrics and the view projection — all pure functions.
- **Limen 0.9.1** browser kernel: the engine runs as .NET WebAssembly behind Limen's typed boundary; HTTP, `localStorage` and navigation are Limen effects. `src/Specula.Wasm/Program.cs` is a one-method `[JSExport]` marshalling shim.
- **Forma 0.13.0** (`@echelon-foundry/design-system`) tokens, components and the Echelon brand; `web/app.css` uses only public Forma tokens (validated against the shipped color contract).
- Static site on GitHub Pages; public unauthenticated GitHub REST API from the browser; no backend, database or token.

Pinned versions and their verification are recorded in [`dependency-baseline.json`](dependency-baseline.json).

## Build and test from a fresh clone

Requirements: .NET SDK 10.0.x with the `wasm-tools` workload, Node 22.

```bash
dotnet workload install wasm-tools
npm ci
npm run check:deps          # manifest, lockfile, fsproj and vendored packages agree
npm run check:colors        # app CSS against Forma's color contract
npm run test:unit           # F# unit tests (xUnit)
npm run build               # dist/ (static site)
npx playwright install chromium
npm run test:browser        # browser acceptance with deterministic GitHub fixtures
npm run test:live           # live GitHub integration against the local build
npm run serve               # http://127.0.0.1:4173/
```

`SPECULA_URL=https://specula.echelonfoundry.com/ npm run test:live` runs the live checks against the deployed site.

CI (`.github/workflows/ci.yml`) runs build-and-test, live-github, deploy (main only) and deployed-smoke, which waits until the deployed `build.json` reports the commit SHA and then smoke-tests the live site with real GitHub data.

## Requirements

- [Product requirements](requirements/PRODUCT.md)
- [Engineering, security and verification requirements](requirements/ENGINEERING.md)
- [Mobile-first screen contracts and acceptance scenarios](requirements/SCREENS.md)
- [Detailed visual and interaction design](requirements/EXPERIENCE.md)
- [Capabilities, limitations and data contracts](requirements/CAPABILITIES.md)
- [Color, theme and visual token contract](requirements/VISUAL-TOKENS.md)
- [Requirements gap closure and implementation priorities](requirements/GAP-CLOSURE.md)

Implementation traceability: [`evidence/IMPLEMENTATION-CHECKLIST.md`](evidence/IMPLEMENTATION-CHECKLIST.md). Final report: [`evidence/FINAL-REPORT.md`](evidence/FINAL-REPORT.md).

## Initial use

Observe `kemiller2002/indy-init-rehearsal-007` during Indy Init. Specula is pre-existing infrastructure and remains independent of the timed Causa build.
