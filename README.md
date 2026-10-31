# Specula

Specula is Echelon Foundry's independent, read-only observer for autonomous software engineering.

Point Specula at a public GitHub repository to inspect commits, pull requests, CI results, deployments, recorded decisions, verification evidence and a replayable timeline. It never directs agents or changes the observed repository.

## Status

Requirements definition in progress. No application implementation or passing tests are claimed.

## Requirements

- [Product requirements](requirements/PRODUCT.md)
- [Engineering, security and verification requirements](requirements/ENGINEERING.md)
- [Mobile-first screen contracts and acceptance scenarios](requirements/SCREENS.md)
- [Detailed visual and interaction design](requirements/EXPERIENCE.md)
- [Capabilities, limitations and data contracts](requirements/CAPABILITIES.md)
- [Color, theme and visual token contract](requirements/VISUAL-TOKENS.md)

## Architecture

F# with Limen WebAssembly runtime and Forma UI, using released pinned Echelon components. Folio is optional for printable reports. Static deployment, no mandatory database or backend, public GitHub read APIs for the MVP.

## Initial use

Observe `kemiller2002/indy-init-rehearsal-007` during Indy Init. Specula is pre-existing infrastructure and remains independent of the timed Causa build.
