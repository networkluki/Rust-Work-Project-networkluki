# Repository structure and design improvements

This repository currently mixes server assets, plugin source files, and top-level project metadata.
A lightweight structure can improve maintainability without changing runtime behavior.

## Current pain points
- Most content is placed at the repository root.
- Plugin files are all in one folder without domain grouping.
- There is no clear convention for naming, ownership, or validation checks.

## Suggested target structure

```text
.
├── README.md
├── LICENSE
├── docs/
│   ├── REPO_STRUCTURE.md
│   ├── CONTRIBUTING.md
│   └── PLUGIN_GUIDELINES.md
├── assets/
│   └── images/
│       └── frostraid.png
├── server/
│   └── Server-Info
└── oxide/
    └── plugins/
        ├── gameplay/
        ├── ui/
        ├── utilities/
        └── debug/
```

## Practical conventions

### 1) Plugin grouping by responsibility
- `gameplay/`: gameplay mechanics (e.g., loadouts, vehicles, raid mechanics)
- `ui/`: UI, panels, clocks, display-oriented plugins
- `utilities/`: generic helpers and infrastructure plugins
- `debug/`: temporary or diagnostic plugins

### 2) File naming
- Keep one plugin per file and match filename to plugin class name.
- Prefer descriptive names over abbreviations.
- Use `*Debug.cs` suffix only for debug-only plugins.

### 3) Documentation baseline
Add (or maintain) these sections in `README.md`:
- Purpose and audience
- Repository layout
- How to install/deploy plugins
- Safe change process (test on staging server first)

### 4) Quality gates
Even without a full build pipeline, define a small validation checklist:
- Compile/load plugins on a staging server
- Verify no startup errors in Oxide logs
- Smoke test chat/console commands for touched plugins

### 5) Change management
- Keep plugin moves and behavior changes in separate commits.
- For structure-only changes, avoid code edits.
- Add migration notes if file paths/scripts depend on old locations.

## Minimal next steps (low-risk)
1. Add this design document under `docs/`.
2. Expand `README.md` with a clear layout section.
3. In a separate PR, move files into grouped folders with no code changes.

These steps improve readability and onboarding while preserving current behavior.
