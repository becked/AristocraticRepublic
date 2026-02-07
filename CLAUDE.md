# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

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
./scripts/workshop-upload.sh [changelog]
```

**mod.io** (requires `.env` with `MODIO_ACCESS_TOKEN`, `MODIO_GAME_ID`, `MODIO_MOD_ID`):
```bash
./scripts/modio-upload.sh [changelog]
```

All scripts read version from `ModInfo.xml` and changelog from `CHANGELOG.md` (or CLI argument).

## Critical: Text Files Need UTF-8 BOM

Text files (`text-*-add.xml`) **must** have a UTF-8 BOM (`ef bb bf`) at the start of the file. Without the BOM, the game silently fails to load text and events won't fire. Event and bonus XMLs do NOT need a BOM.

```bash
# Add BOM to a text file
printf '\xef\xbb\xbf' > temp.xml && cat original.xml >> temp.xml && mv temp.xml original.xml
```

## Architecture

**Event System (15 total events):**
- 3 candidate-count variants: `_3` (3 candidates, priority 15), `_2` (2 candidates, priority 14), `_1` (1 candidate, priority 13)
- 5 turn tiers for legitimacy scaling: `_T1` through `_T5`
- Naming: `EVENTSTORY_REPUBLIC_ELECTION_{candidates}_{tier}`
- All 15 events share cooldown via `aeEventStoryRepeatTurns` (bidirectional linking required)

**Turn Tiers:**
| Tier | Turns | New Leader Legitimacy | Re-election Legitimacy |
|------|-------|----------------------|----------------------|
| T1 | 10-30 | +8 | +5 |
| T2 | 31-60 | +15 | +10 |
| T3 | 61-100 | +25 | +15 |
| T4 | 101-150 | +35 | +20 |
| T5 | 151+ | +45 | +25 |

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
│   ├── memory-family-add.xml # Family opinion memory effects
│   ├── text-add.xml          # UI text strings (needs UTF-8 BOM)
│   └── text-new-add.xml      # Additional text strings (needs UTF-8 BOM)
├── docs/
│   ├── aristocratic-republic-mod.md  # Full PRD and design spec
│   ├── modding-lessons-learned.md    # Troubleshooting and modding patterns
│   ├── memory-levels.md             # Vanilla memory level reference table
│   └── event-lottery-weight-system.md
├── scripts/
│   ├── deploy.sh             # Deploy to local mods folder
│   ├── workshop-upload.sh    # Upload to Steam Workshop via SteamCMD
│   └── modio-upload.sh       # Upload to mod.io via API
└── Reference/ -> (symlink)   # Game source code and vanilla XML data
```

**Note:** Mod files must be in `Infos/` (not `XML/Infos/`) and use `-add.xml` suffix per Old World modding convention.

## Key Implementation Details

- Uses `iSeizeThroneSubject` for clean succession (avoids heir-bypass penalties)
- Priority 13-15 ensures election beats vanilla events (max vanilla priority is 9)
- Re-election requires all families at Cautious+ opinion (uses `IndexSubject` with `SUBJECT_FAMILY_MIN_CAUTIOUS`)
  - 3-candidate: checks subjects 4, 5, 6; 2-candidate: checks subjects 3, 4; 1-candidate: checks subject 2
- Candidates must be: ADULT, HEALTHY, NON_LEADER, and from different families

## Testing

Enable the mod in Old World and verify:
1. Elections fire every 10 turns starting at turn 10
2. Correct turn tier applies (check legitimacy bonus amount)
3. Family opinions change correctly (+20 winner, -20 others)
4. Re-election option appears/hides based on family opinions
5. No "bypassed heir" penalties on leader change

## Version Management

Single source of truth: `ModInfo.xml` `<modversion>` tag
