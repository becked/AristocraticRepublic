# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

**IMPORTANT: Never deploy, push, or commit without explicit instruction from the user.**

## Project Overview

This is an XML-only mod for Old World (turn-based strategy game) that implements an Aristocratic Republic government system with periodic elections every 10 turns. No C# code - uses the game's existing event system.

## Game Reference Data

`Reference/` is a symlink to the game's install directory containing:
- `Reference/XML/Infos/` — all vanilla XML data (bonuses, events, subjects, memory levels, etc.)
- `Reference/Source/Base/` — decompiled C# game source (succession logic, event system, etc.)

Use these to look up vanilla behavior, available subject types, bonus fields, memory levels, etc.

## Deployment

**Local testing** (requires `.env` with `OLDWORLD_MODS_PATH`):
```bash
./scripts/deploy.sh
```

**Steam Workshop** (requires `steamcmd`, `.env` with `STEAM_USERNAME`, `workshop.vdf` template):
```bash
./scripts/workshop-upload.sh [--dry-run] [changelog]
```

**mod.io** (requires `.env` with `MODIO_ACCESS_TOKEN`, `MODIO_GAME_ID`, `MODIO_MOD_ID`):
```bash
./scripts/modio-upload.sh [--dry-run] [changelog]
```

**Validation only:**
```bash
./scripts/validate.sh
```

All scripts run validation before deploying/uploading. All scripts read version from `ModInfo.xml` and changelog from `CHANGELOG.md` (or CLI argument). Use `--dry-run` to preview uploads without sending.

## Critical: Text Files Need UTF-8 BOM

Text files (`text*-add.xml`) **must** have a UTF-8 BOM (`ef bb bf`) at the start of the file. Without the BOM, the game silently fails to load text and events won't fire. Event and bonus XMLs do NOT need a BOM.

The pre-commit hook and `scripts/validate.sh` catch missing BOMs automatically. To set up the hook after a fresh clone: `./scripts/install-hooks.sh`

```bash
# Add BOM to a text file manually
printf '\xef\xbb\xbf' > temp.xml && cat original.xml >> temp.xml && mv temp.xml original.xml
```

## Architecture

**Difficulty Presets (3 presets, selected via GameOption toggles):**

| Preset | GameOption | Legitimacy | Re-election Threshold |
|--------|-----------|-----------|----------------------|
| **Stable** (default) | *(none — implicit default)* | +8 | Cautious+ |
| **Strained** | `GAMEOPTION_REPUBLIC_STRAINED` | +6 | Pleased+ |
| **Fragile** | `GAMEOPTION_REPUBLIC_FRAGILE` | +4 | Friendly+ |

Presets are mutually exclusive via `abDisableWhenActive` (bidirectional). Legitimacy is the same for both new leader and re-election.

**Event System (9 total events):**
- 3 candidate-count variants × 3 presets
- Naming: `EVENTSTORY_REPUBLIC_ELECTION_{candidates}_{preset}` (e.g. `_3_STABLE`, `_2_STRAINED`, `_1_FRAGILE`)
- All 9 events share cooldown via `aeEventStoryRepeatTurns` (bidirectional linking required)

**Priority Cascade (ensures correct preset fires):**
- Stable: priority 21/20/19 (3c/2c/1c) — blocked by `aeGameOptionInvalid` when STRAINED or FRAGILE is ON
- Strained: priority 18/17/16 — blocked when FRAGILE is ON
- Fragile: priority 15/14/13 — never blocked
- In default mode (no options checked), all events are eligible but Stable wins on highest priority; shared cooldown blocks the rest

**Family Opinion System:**
- Winner's family: +20 opinion (via +40 Memory, -20 MemoryAllFamilies)
- Other families: -20 opinion (via MemoryAllFamilies only)
- Duration: 20 turns (spans 2 election cycles; uses `MEMORYLEVEL_POS_MEDIUM_SHORT` and `MEMORYLEVEL_NEG_LOW_SHORT`)

## File Structure

```
AristocraticRepublic/
├── ModInfo.xml               # Mod metadata and version (single source of truth)
├── CLAUDE.md
├── logo-512.png
├── Infos/
│   ├── eventStory-add.xml    # Election event definitions (triggers, subjects, options)
│   ├── bonus-event-add.xml   # Legitimacy bonuses and memory assignments
│   ├── gameOption-add.xml    # Difficulty preset toggles (Strained, Fragile)
│   ├── memory-family-add.xml # Family opinion memory effects
│   ├── text-add.xml          # UI text strings (needs UTF-8 BOM)
│   └── text-new-add.xml      # Additional text strings (needs UTF-8 BOM)
├── docs/
│   ├── aristocratic-republic-mod.md  # Full PRD and design spec
│   ├── modding-lessons-learned.md    # Troubleshooting and modding patterns
│   ├── memory-levels.md             # Vanilla memory level reference table
│   └── event-lottery-weight-system.md
├── CHANGELOG.md              # Release notes (parsed by upload scripts)
├── scripts/
│   ├── deploy.sh             # Deploy to local mods folder
│   ├── workshop-upload.sh    # Upload to Steam Workshop via SteamCMD
│   ├── modio-upload.sh       # Upload to mod.io via API
│   ├── validate.sh           # BOM + XML validation (also used as pre-commit hook)
│   └── install-hooks.sh      # Install git pre-commit hook
└── Reference/ -> (symlink)   # Game source code and vanilla XML data
```

**Note:** Mod files must be in `Infos/` (not `XML/Infos/`) and use `-add.xml` suffix per Old World modding convention.

## Key Implementation Details

- Uses `iSeizeThroneSubject` for clean succession (avoids heir-bypass penalties)
- Priority 13-21 ensures election beats vanilla events (max vanilla priority is 9)
- Re-election threshold varies by preset: Cautious+ (Stable), Pleased+ (Strained), Friendly+ (Fragile)
  - Uses `IndexSubject` with `SUBJECT_FAMILY_MIN_CAUTIOUS` / `_PLEASED` / `_FRIENDLY`
  - 3-candidate: checks subjects 5, 6, 7; 2-candidate: checks subjects 4, 5; 1-candidate: checks subject 3
- Candidates must be: ADULT, HEALTHY, NON_LEADER, and from different families
- `aeGameOptionInvalid` blocks higher-preset events; priority cascade + shared cooldown ensures only correct preset fires

## Testing

Enable the mod in Old World and verify:
1. Elections fire every 10 turns starting at turn 10
2. Default (no options): +8 legitimacy, Cautious+ re-election threshold
3. Strained option: +6 legitimacy, Pleased+ threshold
4. Fragile option: +4 legitimacy, Friendly+ threshold
5. Checking one option greys out the other in game setup
6. Family opinions change correctly (+20 winner, -20 others)
7. Re-election option appears/hides based on family opinions
8. No "bypassed heir" penalties on leader change

## Version Management

Single source of truth: `ModInfo.xml` `<modversion>` tag. When bumping the version, also add a new `## [x.y.z] - YYYY-MM-DD` section to `CHANGELOG.md` — the upload scripts automatically extract notes for the current version.
