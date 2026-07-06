# External Source Research

Pattern harvests from MIT-licensed RimWorld LLM mods. All four sources permit code adoption with attribution. Each doc: architecture summary, key patterns with `path:line` citations, answers to our audit gaps, ADOPT/AVOID lists.

| Doc | Source | Feeds | Gaps addressed |
|---|---|---|---|
| [rimmind-actions.md](rimmind-actions.md) | RimWorld-RimMind-Mod-Actions | M2 Action layer (raf-ad9): main-thread execution, whitelist schema, risk tiers, target resolution | G-03, G-13, G-21, G-28 |
| [rimai-core.md](rimai-core.md) | oidahdsah0/Rimworld_AI_Core | M1 Perception cadence + orchestration + persistence; RimAIApi usage patterns | G-02, G-23, G-24 |
| [rimmind-context.md](rimmind-context.md) | RimWorld-RimMind-Mod-Core | M1 ColonySnapshot trimmer: context filter presets, token budgeting, request queue | G-02 |
| [rimmind-memory.md](rimmind-memory.md) | RimWorld-RimMind-Mod-Memory | M4/M5 Memory layer: capture, injection, decay, save persistence | — |

Consumption rule: before implementing a milestone, the implementer reads the relevant doc(s) and reconciles ADOPT items into the layer spec (00–05) as amendments. Where a research doc conflicts with a spec, the spec owner decides and records the decision in the spec.
