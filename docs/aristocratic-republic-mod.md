# Aristocratic Republic Mod - Product Requirements Document

## Overview

This mod introduces an Aristocratic Republic government system to Old World where leadership changes occur through periodic elections among the ruling elite rather than dynastic succession. Every X turns (configurable, default 10), players choose their next leader from a pool of eligible candidates drawn from family heads (oligarchs), religious leaders, and notable characters.

**Historical Parallels:**
- **Roman Republic** - Consuls elected annually from senatorial families
- **Carthage** - Suffetes elected by powerful merchant families
- **Venice** - Doge elected by aristocratic council

## Design Goals

1. **XML-Only Implementation**: No C# code required - uses existing event system
2. **Compatibility**: Works alongside other mods (no GameFactory override)
3. **Legitimacy Rework**: Uses base legitimacy bonuses instead of cognomen-based system to avoid decay issues
4. **Flexible Candidate Pool**: Gracefully handles early-game scenarios with fewer eligible candidates
5. **Clean Leader Transitions**: Uses `iSeizeThroneSubject` to avoid heir-bypass opinion penalties
6. **Thematic Consistency**: Uses "oligarch" terminology to match existing Old World flavor
7. **Political Consequences**: Family opinion effects create meaningful political dynamics (+20 winner, -20 losers)

---

## Core Mechanics

### Election Cycle

- Elections trigger every 10 turns via high-priority `EVENTTRIGGER_NEW_TURN` events
- Priority 10+ ensures election events beat all vanilla events (max vanilla priority is 9)
- `iRepeatTurns=10` creates the election cycle
- Multiple event variants handle different candidate pool sizes

### Candidate Pool Sources

| Priority | Source | Subject Type | In-Game Role |
|----------|--------|--------------|--------------|
| 1 | Family Heads | `SUBJECT_FAMILY_HEAD_US` | Oligarchs |
| 2 | Religion Heads | `SUBJECT_RELIGION_HEAD_US` | High Priests |
| 3 | Rising Stars | `SUBJECT_RISING_STAR` | Ambitious Nobles |
| 4 | Power Hungry | `SUBJECT_POWER_HUNGRY` | Aspiring Leaders |
| 5 | Courtiers | `SUBJECT_COURTIER_US` | Court Officials |

### Legitimacy System

**Solution**: Grant flat `iLegitimacy` bonuses that go to `LegitimacyBase` (which does NOT decay). The bonus amount depends on the selected difficulty preset:

| Preset | Legitimacy (elect & re-elect) | Re-election Threshold |
|--------|------------------------------|----------------------|
| **Stable** (default) | +8 | Cautious+ |
| **Strained** | +6 | Pleased+ |
| **Fragile** | +4 | Friendly+ |

Presets are selected via `GameOption` toggles in game setup. The priority cascade mechanism (see Event Architecture) ensures only the correct preset's events fire.

### Leader Transition

Uses `iSeizeThroneSubject` bonus which:
- Directly calls `makeNextLeader()`
- Skips `doSuccessionBypass()` - no "bypassed heir" relationship penalty (-80 opinion avoided)
- Old leader abdicates cleanly

---

## Event Architecture

### Multiple Event Variants

Events vary by **candidate count** (how many candidates available) and **difficulty preset** (legitimacy and opinion threshold).

**Candidate Count Variants** (within each preset, priority determines fallback):

| Suffix | Candidates | Priority Offset |
|--------|------------|-----------------|
| `_3` | 3 | +0 (highest) |
| `_2` | 2 | -1 |
| `_1` | 1 | -2 (lowest) |

**Difficulty Preset Variants** (priority cascade ensures correct preset fires):

| Preset | Legitimacy | Threshold | Priority (3c/2c/1c) | `aeGameOptionInvalid` |
|--------|-----------|-----------|--------------------|-----------------------|
| Stable | +8 | Cautious+ | 21/20/19 | STRAINED, FRAGILE |
| Strained | +6 | Pleased+ | 18/17/16 | FRAGILE |
| Fragile | +4 | Friendly+ | 15/14/13 | *(none)* |

**Combined naming**: `EVENTSTORY_REPUBLIC_ELECTION_{candidates}_{preset}`
- Example: `EVENTSTORY_REPUBLIC_ELECTION_3_STABLE` = 3 candidates, Stable preset

**Total events**: 3 candidate variants × 3 presets = **9 election events**

### Bidirectional Cooldown Links

**Critical**: `aeEventStoryRepeatTurns` is ONE-DIRECTIONAL. Each event must list ALL other variants (all 10) to ensure only one election fires per cycle.

When any variant fires, all variants check against it and respect the 10-turn cooldown.

### Subject Requirements

All candidate subjects require:
- `SUBJECT_ADULT` (age 18+)
- `SUBJECT_HEALTHY` (not dying)
- `SUBJECT_NON_LEADER` (not current leader)

Candidates must be different people:
- Use `SUBJECTRELATION_CHARACTER_DIFF` between candidate subjects

---

## Detailed Specifications

### Election Event Structure

```
Trigger: EVENTTRIGGER_NEW_TURN
Priority: 12-13 (beats vanilla max of 9, varies by candidate count)
iMinTurns/iMaxTurns: Defines turn tier (T1-T5)
iRepeatTurns: 10 (election cycle length)
bForceChoice: 1 (must select a candidate)

Subjects:
  0: SUBJECT_LEADER_US (current leader, for re-election)
  1-3: Candidate subjects with filters
  4-6: SUBJECT_FAMILY_US (for re-election opinion check)

Options:
  - Re-election option (requires all families Cautious+)
  - One per candidate, each using turn-tier-specific bonus
```

### Legitimacy Bonuses (Turn-Scaled)

| Tier | Turn Range | New Leader Bonus | Re-election Bonus |
|------|------------|------------------|-------------------|
| T1 | 10-30 | +8 | +3 |
| T2 | 31-60 | +15 | +5 |
| T3 | 61-100 | +25 | +8 |
| T4 | 101-150 | +35 | +12 |
| T5 | 151+ | +45 | +15 |

Re-election bonus is ~1/3 of new leader bonus (stability without the "mandate" boost).

### Family Opinion Effects

When a candidate wins the election:
- **Winner's family**: +20 opinion ("Elevated Our Candidate")
- **Other families**: -20 opinion ("Passed Over for Leadership")

**Implementation**: Uses the memory system with custom `MEMORYFAMILY_*` types:
- `Memory` field applies +40 to winner's family (via character subject → family extraction)
- `MemoryAllFamilies` field applies -20 to ALL families
- Net effect: Winner = +40-20 = **+20**, Others = **-20**
- Duration: 20 turns (spans 2 election cycles; uses vanilla `MEMORYLEVEL_POS_MEDIUM_SHORT` and `MEMORYLEVEL_NEG_LOW_SHORT`)

### Re-election Mechanics

The current leader can run for re-election if **all 3 families have at least Cautious opinion** (0+).

**Implementation:**
- Add 3 `SUBJECT_FAMILY_US` subjects to the event (one per family)
- Use `SubjectNotRelations` to ensure they're all different families
- Re-election option uses `IndexSubject` to require all 3 family subjects meet `SUBJECT_FAMILY_MIN_CAUTIOUS`
- `bHideInvalid=1` hides the re-election option when any family is upset

**Re-election bonus:** If the current leader is re-elected, they receive a continuity bonus (+3 legitimacy) instead of the standard mandate.

### Opinion Filtering (Optional Enhancement)

Candidates can be filtered by opinion using `SubjectExtras`:
- `SUBJECT_FAMILY_MIN_PLEASED` - only candidates from pleased families
- `SUBJECT_CHARACTER_MIN_PLEASED` - only characters pleased with current leader

This could create a "legitimacy crisis" if all families are upset - no valid candidates!

---

## File Structure

```
AristocraticRepublic/
├── ModInfo.xml
├── XML/
│   └── Infos/
│       ├── eventStory-republic.xml    # Election events
│       ├── eventOption-republic.xml   # Election options
│       ├── bonus-republic.xml         # Legitimacy + opinion bonuses
│       ├── memory-family-republic.xml # Family opinion memories
│       └── text-republic.xml          # Display text
```

---

## Example XML Implementation

### eventStory-republic.xml

```xml
<?xml version="1.0"?>
<Root>
    <!--
    REPUBLIC ELECTION - 3 CANDIDATES, TIER 2 (Turns 31-60)
    Each turn tier has its own event pointing to tier-specific options/bonuses.
    This example shows T2; replicate for T1, T3, T4, T5 with different turn ranges.
    -->
    <Entry>
        <zType>EVENTSTORY_REPUBLIC_ELECTION_3_T2</zType>
        <Name>TEXT_EVENTSTORY_REPUBLIC_ELECTION_TITLE</Name>
        <Text>TEXT_EVENTSTORY_REPUBLIC_ELECTION_3</Text>
        <zBackgroundName>crowning_02</zBackgroundName>
        <zAudioTrigger>AUDIO_UI_EVENT_SHATTERING_PRESENCE</zAudioTrigger>

        <!-- Subjects: Leader + 3 candidates + 3 families (for opinion check) -->
        <aeSubjects>
            <zValue>SUBJECT_LEADER_US</zValue>           <!-- 0: Current leader (for re-election) -->
            <zValue>SUBJECT_FAMILY_HEAD_US</zValue>      <!-- 1: Family head candidate 1 -->
            <zValue>SUBJECT_FAMILY_HEAD_US</zValue>      <!-- 2: Family head candidate 2 -->
            <zValue>SUBJECT_RELIGION_HEAD_US</zValue>    <!-- 3: Religion head candidate -->
            <zValue>SUBJECT_FAMILY_US</zValue>           <!-- 4: Family 1 (opinion check) -->
            <zValue>SUBJECT_FAMILY_US</zValue>           <!-- 5: Family 2 (opinion check) -->
            <zValue>SUBJECT_FAMILY_US</zValue>           <!-- 6: Family 3 (opinion check) -->
        </aeSubjects>

        <!-- Candidate requirements -->
        <SubjectExtras>
            <Pair><First>1</First><Second>SUBJECT_ADULT</Second></Pair>
            <Pair><First>1</First><Second>SUBJECT_HEALTHY</Second></Pair>
            <Pair><First>1</First><Second>SUBJECT_NON_LEADER</Second></Pair>
            <Pair><First>2</First><Second>SUBJECT_ADULT</Second></Pair>
            <Pair><First>2</First><Second>SUBJECT_HEALTHY</Second></Pair>
            <Pair><First>2</First><Second>SUBJECT_NON_LEADER</Second></Pair>
            <Pair><First>3</First><Second>SUBJECT_ADULT</Second></Pair>
            <Pair><First>3</First><Second>SUBJECT_HEALTHY</Second></Pair>
            <Pair><First>3</First><Second>SUBJECT_NON_LEADER</Second></Pair>
        </SubjectExtras>

        <!-- Ensure candidates are different people AND families are different -->
        <SubjectNotRelations>
            <Triple><First>1</First><Second>SUBJECTRELATION_CHARACTER_SAME</Second><Third>2</Third></Triple>
            <Triple><First>1</First><Second>SUBJECTRELATION_CHARACTER_SAME</Second><Third>3</Third></Triple>
            <Triple><First>2</First><Second>SUBJECTRELATION_CHARACTER_SAME</Second><Third>3</Third></Triple>
            <Triple><First>4</First><Second>SUBJECTRELATION_FAMILY_SAME</Second><Third>5</Third></Triple>
            <Triple><First>4</First><Second>SUBJECTRELATION_FAMILY_SAME</Second><Third>6</Third></Triple>
            <Triple><First>5</First><Second>SUBJECTRELATION_FAMILY_SAME</Second><Third>6</Third></Triple>
        </SubjectNotRelations>

        <!-- TIER-SPECIFIC OPTIONS: T2 uses _T2 bonuses (+15 legitimacy) -->
        <aeOptions>
            <zValue>EVENTOPTION_REPUBLIC_REELECT_T2</zValue>
            <zValue>EVENTOPTION_REPUBLIC_CANDIDATE_1_T2</zValue>
            <zValue>EVENTOPTION_REPUBLIC_CANDIDATE_2_T2</zValue>
            <zValue>EVENTOPTION_REPUBLIC_CANDIDATE_3_T2</zValue>
        </aeOptions>

        <!-- Event configuration -->
        <Trigger>EVENTTRIGGER_NEW_TURN</Trigger>
        <iPriority>13</iPriority>
        <iWeight>1</iWeight>

        <!-- TURN TIER: T2 = turns 31-60 -->
        <iMinTurns>31</iMinTurns>
        <iMaxTurns>60</iMaxTurns>
        <iRepeatTurns>10</iRepeatTurns>     <!-- 10-turn election cycle -->
        <bForceChoice>1</bForceChoice>      <!-- Must pick someone -->

        <!-- Link to ALL other election variants for shared cooldown (all 10 events) -->
        <aeEventStoryRepeatTurns>
            <!-- Same candidate count, other tiers -->
            <zValue>EVENTSTORY_REPUBLIC_ELECTION_3_T1</zValue>
            <zValue>EVENTSTORY_REPUBLIC_ELECTION_3_T3</zValue>
            <zValue>EVENTSTORY_REPUBLIC_ELECTION_3_T4</zValue>
            <zValue>EVENTSTORY_REPUBLIC_ELECTION_3_T5</zValue>
            <!-- Other candidate count, all tiers -->
            <zValue>EVENTSTORY_REPUBLIC_ELECTION_2_T1</zValue>
            <zValue>EVENTSTORY_REPUBLIC_ELECTION_2_T2</zValue>
            <zValue>EVENTSTORY_REPUBLIC_ELECTION_2_T3</zValue>
            <zValue>EVENTSTORY_REPUBLIC_ELECTION_2_T4</zValue>
            <zValue>EVENTSTORY_REPUBLIC_ELECTION_2_T5</zValue>
        </aeEventStoryRepeatTurns>

        <iImageSubject>1</iImageSubject>
        <iImageExtra>2</iImageExtra>
    </Entry>

    <!--
    Additional events needed (not shown for brevity):
    - EVENTSTORY_REPUBLIC_ELECTION_3_T1 (turns 10-30, +8 legitimacy)
    - EVENTSTORY_REPUBLIC_ELECTION_3_T3 (turns 61-100, +25 legitimacy)
    - EVENTSTORY_REPUBLIC_ELECTION_3_T4 (turns 101-150, +35 legitimacy)
    - EVENTSTORY_REPUBLIC_ELECTION_3_T5 (turns 151+, +45 legitimacy)
    - EVENTSTORY_REPUBLIC_ELECTION_2_T1 through _T5 (2-candidate fallbacks)

    Each uses the same subject structure but different:
    - iMinTurns/iMaxTurns for turn gating
    - aeOptions pointing to tier-specific options (_T1, _T2, etc.)
    -->
</Root>
```

### eventOption-republic.xml

```xml
<?xml version="1.0"?>
<Root>
    <!--
    TIER-SPECIFIC OPTIONS
    Each turn tier has its own set of options pointing to tier-specific bonuses.
    This example shows T2 (turns 31-60, +15 legitimacy for new leader, +5 for re-election).
    Replicate for T1, T3, T4, T5 with different bonus references.
    -->

    <!-- RE-ELECTION OPTION - T2 -->
    <Entry>
        <zType>EVENTOPTION_REPUBLIC_REELECT_T2</zType>
        <Text>TEXT_EVENTOPTION_REPUBLIC_REELECT</Text>
        <aeBonuses>
            <zValue>BONUS_REPUBLIC_REELECT_T2</zValue>
        </aeBonuses>
        <!-- Require all 3 families to have Cautious+ opinion -->
        <IndexSubject>
            <Pair><First>4</First><Second>SUBJECT_FAMILY_MIN_CAUTIOUS</Second></Pair>
            <Pair><First>5</First><Second>SUBJECT_FAMILY_MIN_CAUTIOUS</Second></Pair>
            <Pair><First>6</First><Second>SUBJECT_FAMILY_MIN_CAUTIOUS</Second></Pair>
        </IndexSubject>
        <bHideInvalid>1</bHideInvalid>
    </Entry>

    <!-- Elect Candidate 1 - T2 -->
    <Entry>
        <zType>EVENTOPTION_REPUBLIC_CANDIDATE_1_T2</zType>
        <Text>TEXT_EVENTOPTION_REPUBLIC_CANDIDATE_1</Text>
        <aeBonuses>
            <zValue>BONUS_REPUBLIC_SEIZE_THRONE_1_T2</zValue>
        </aeBonuses>
    </Entry>

    <!-- Elect Candidate 2 - T2 -->
    <Entry>
        <zType>EVENTOPTION_REPUBLIC_CANDIDATE_2_T2</zType>
        <Text>TEXT_EVENTOPTION_REPUBLIC_CANDIDATE_2</Text>
        <aeBonuses>
            <zValue>BONUS_REPUBLIC_SEIZE_THRONE_2_T2</zValue>
        </aeBonuses>
    </Entry>

    <!-- Elect Candidate 3 - T2 -->
    <Entry>
        <zType>EVENTOPTION_REPUBLIC_CANDIDATE_3_T2</zType>
        <Text>TEXT_EVENTOPTION_REPUBLIC_CANDIDATE_3</Text>
        <aeBonuses>
            <zValue>BONUS_REPUBLIC_SEIZE_THRONE_3_T2</zValue>
        </aeBonuses>
        <bHideInvalid>1</bHideInvalid>
    </Entry>

    <!--
    Additional options needed (not shown for brevity):
    - EVENTOPTION_REPUBLIC_REELECT_T1 through _T5
    - EVENTOPTION_REPUBLIC_CANDIDATE_1_T1 through _T5
    - EVENTOPTION_REPUBLIC_CANDIDATE_2_T1 through _T5
    - EVENTOPTION_REPUBLIC_CANDIDATE_3_T1 through _T5

    All share the same Text entries but point to tier-specific bonuses.
    -->
</Root>
```

### bonus-republic.xml

```xml
<?xml version="1.0"?>
<Root>
    <!--
    TIER-SPECIFIC BONUSES
    Each turn tier has different legitimacy values to offset cognomen decay.
    Family opinion effects (+40 winner, -20 all) remain constant across tiers.

    Legitimacy values by tier:
    T1 (turns 10-30):  New Leader +8,  Re-election +3
    T2 (turns 31-60):  New Leader +15, Re-election +5
    T3 (turns 61-100): New Leader +25, Re-election +8
    T4 (turns 101-150): New Leader +35, Re-election +12
    T5 (turns 151+):   New Leader +45, Re-election +15
    -->

    <!-- ===== TIER 2 BONUSES (turns 31-60) ===== -->

    <!-- New Leader - Candidate 1 - T2 -->
    <Entry>
        <zType>BONUS_REPUBLIC_SEIZE_THRONE_1_T2</zType>
        <Name>TEXT_BONUS_REPUBLIC_MANDATE</Name>
        <iSeizeThroneSubject>1</iSeizeThroneSubject>
        <iLegitimacy>15</iLegitimacy>
        <Memory>MEMORYFAMILY_REPUBLIC_ELEVATED</Memory>
        <MemoryAllFamilies>MEMORYFAMILY_REPUBLIC_PASSED_OVER</MemoryAllFamilies>
    </Entry>

    <!-- New Leader - Candidate 2 - T2 -->
    <Entry>
        <zType>BONUS_REPUBLIC_SEIZE_THRONE_2_T2</zType>
        <Name>TEXT_BONUS_REPUBLIC_MANDATE</Name>
        <iSeizeThroneSubject>2</iSeizeThroneSubject>
        <iLegitimacy>15</iLegitimacy>
        <Memory>MEMORYFAMILY_REPUBLIC_ELEVATED</Memory>
        <MemoryAllFamilies>MEMORYFAMILY_REPUBLIC_PASSED_OVER</MemoryAllFamilies>
    </Entry>

    <!-- New Leader - Candidate 3 - T2 -->
    <Entry>
        <zType>BONUS_REPUBLIC_SEIZE_THRONE_3_T2</zType>
        <Name>TEXT_BONUS_REPUBLIC_MANDATE</Name>
        <iSeizeThroneSubject>3</iSeizeThroneSubject>
        <iLegitimacy>15</iLegitimacy>
        <Memory>MEMORYFAMILY_REPUBLIC_ELEVATED</Memory>
        <MemoryAllFamilies>MEMORYFAMILY_REPUBLIC_PASSED_OVER</MemoryAllFamilies>
    </Entry>

    <!-- Re-election - T2 (no leader change, no family opinion changes) -->
    <Entry>
        <zType>BONUS_REPUBLIC_REELECT_T2</zType>
        <Name>TEXT_BONUS_REPUBLIC_CONTINUITY</Name>
        <iLegitimacy>5</iLegitimacy>
    </Entry>

    <!--
    Additional bonuses needed (not shown for brevity):

    T1 (turns 10-30):
    - BONUS_REPUBLIC_SEIZE_THRONE_1_T1 (+8 legitimacy)
    - BONUS_REPUBLIC_SEIZE_THRONE_2_T1 (+8 legitimacy)
    - BONUS_REPUBLIC_SEIZE_THRONE_3_T1 (+8 legitimacy)
    - BONUS_REPUBLIC_REELECT_T1 (+3 legitimacy)

    T3 (turns 61-100):
    - BONUS_REPUBLIC_SEIZE_THRONE_1_T3 (+25 legitimacy)
    - BONUS_REPUBLIC_SEIZE_THRONE_2_T3 (+25 legitimacy)
    - BONUS_REPUBLIC_SEIZE_THRONE_3_T3 (+25 legitimacy)
    - BONUS_REPUBLIC_REELECT_T3 (+8 legitimacy)

    T4 (turns 101-150):
    - BONUS_REPUBLIC_SEIZE_THRONE_1_T4 (+35 legitimacy)
    - BONUS_REPUBLIC_SEIZE_THRONE_2_T4 (+35 legitimacy)
    - BONUS_REPUBLIC_SEIZE_THRONE_3_T4 (+35 legitimacy)
    - BONUS_REPUBLIC_REELECT_T4 (+12 legitimacy)

    T5 (turns 151+):
    - BONUS_REPUBLIC_SEIZE_THRONE_1_T5 (+45 legitimacy)
    - BONUS_REPUBLIC_SEIZE_THRONE_2_T5 (+45 legitimacy)
    - BONUS_REPUBLIC_SEIZE_THRONE_3_T5 (+45 legitimacy)
    - BONUS_REPUBLIC_REELECT_T5 (+15 legitimacy)
    -->
</Root>
```

### memory-family-republic.xml

```xml
<?xml version="1.0"?>
<Root>
    <!--
    Custom family memories for election results.
    Uses vanilla MemoryLevel presets. See docs/memory-levels.md for all values.
    -->

    <!-- Applied to winner's family: +40 opinion for 20 turns (MEMORYLEVEL_POS_MEDIUM_SHORT) -->
    <!-- Combined with -20 from MemoryAllFamilies = net +20 -->
    <Entry>
        <zType>MEMORYFAMILY_REPUBLIC_ELEVATED</zType>
        <Text>TEXT_MEMORYFAMILY_REPUBLIC_ELEVATED</Text>
        <MemoryLevel>MEMORYLEVEL_POS_MEDIUM_SHORT</MemoryLevel>
    </Entry>

    <!-- Applied to ALL families: -20 opinion for 20 turns (MEMORYLEVEL_NEG_LOW_SHORT) -->
    <!-- Winner gets this too, but +40-20 = net +20 -->
    <Entry>
        <zType>MEMORYFAMILY_REPUBLIC_PASSED_OVER</zType>
        <Text>TEXT_MEMORYFAMILY_REPUBLIC_PASSED_OVER</Text>
        <MemoryLevel>MEMORYLEVEL_NEG_LOW_SHORT</MemoryLevel>
    </Entry>
</Root>
```

### text-republic.xml

```xml
<?xml version="1.0"?>
<Root>
    <!-- Event titles and body text -->
    <Entry>
        <zType>TEXT_EVENTSTORY_REPUBLIC_ELECTION_TITLE</zType>
        <en-US>Council of Oligarchs</en-US>
    </Entry>

    <Entry>
        <zType>TEXT_EVENTSTORY_REPUBLIC_ELECTION_3</zType>
        <en-US>The oligarchs have gathered to select the next leader of our republic. Three candidates have emerged from among the noble houses, each with the backing of powerful factions. The council chambers echo with whispered negotiations and promises of favor.

Who shall the oligarchs elevate to lead us?</en-US>
    </Entry>

    <Entry>
        <zType>TEXT_EVENTSTORY_REPUBLIC_ELECTION_2</zType>
        <en-US>The council convenes to choose a new leader. Though our republic is young and the ranks of the oligarchs are thin, two worthy candidates have put themselves forward.

Who shall guide our people?</en-US>
    </Entry>

    <!-- Option text - uses character name substitution -->
    <Entry>
        <zType>TEXT_EVENTOPTION_REPUBLIC_REELECT</zType>
        <en-US>Re-elect {CHARACTER-0} for another term</en-US>
    </Entry>

    <Entry>
        <zType>TEXT_EVENTOPTION_REPUBLIC_CANDIDATE_1</zType>
        <en-US>The oligarchs support {CHARACTER-1} of {FAMILY-1}</en-US>
    </Entry>

    <Entry>
        <zType>TEXT_EVENTOPTION_REPUBLIC_CANDIDATE_2</zType>
        <en-US>The oligarchs support {CHARACTER-2} of {FAMILY-2}</en-US>
    </Entry>

    <Entry>
        <zType>TEXT_EVENTOPTION_REPUBLIC_CANDIDATE_3</zType>
        <en-US>The oligarchs support {CHARACTER-3} of {FAMILY-3}</en-US>
    </Entry>

    <Entry>
        <zType>TEXT_EVENTOPTION_REPUBLIC_CANDIDATE_4</zType>
        <en-US>The oligarchs support {CHARACTER-4} of {FAMILY-4}</en-US>
    </Entry>

    <!-- Bonus text -->
    <Entry>
        <zType>TEXT_BONUS_REPUBLIC_MANDATE</zType>
        <en-US>Mandate of the Oligarchs</en-US>
    </Entry>

    <Entry>
        <zType>TEXT_BONUS_REPUBLIC_CONTINUITY</zType>
        <en-US>Continuity of Leadership</en-US>
    </Entry>

    <!-- Memory text - shown in family opinion tooltip -->
    <Entry>
        <zType>TEXT_MEMORYFAMILY_REPUBLIC_ELEVATED</zType>
        <en-US>Elevated Our Candidate to Leadership</en-US>
    </Entry>

    <Entry>
        <zType>TEXT_MEMORYFAMILY_REPUBLIC_PASSED_OVER</zType>
        <en-US>Passed Over for Leadership</en-US>
    </Entry>
</Root>
```

### ModInfo.xml

```xml
<?xml version="1.0"?>
<ModInfo xmlns:xsd="http://www.w3.org/2001/XMLSchema"
         xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
    <displayName>Aristocratic Republic</displayName>
    <description>Transforms your nation into an Aristocratic Republic where leadership is determined by election among the oligarchs rather than dynastic succession.

Inspired by the Roman Republic, Carthage, and Venice - where powerful families selected leaders from among themselves.

[b]Features:[/b]
[list]
[*] Elections every 10 turns
[*] 2-4 candidates from family heads, religious leaders, and notable characters
[*] Re-election option available if all families have neutral+ opinion
[*] Turn-scaled legitimacy to offset cognomen decay (+8 to +45 based on game progress)
[*] Political consequences: Winner's family gains +20 opinion, other families lose -20
[*] No heir-bypass penalties - clean leadership transitions
[*] Uses existing "oligarch" terminology from Old World
[/list]

[b]Compatibility:[/b] XML-only mod, compatible with most other mods.</description>
    <author>YourName</author>
    <modversion>1.0.0</modversion>
    <modbuild>1.0.81098</modbuild>
    <tags>GameInfo</tags>
    <singlePlayer>true</singlePlayer>
    <multiplayer>false</multiplayer>
    <scenario>false</scenario>
    <scenarioToggle>false</scenarioToggle>
    <blocksMods>false</blocksMods>
    <modDependencies />
    <modIncompatibilities />
    <modWhitelist />
    <gameContentRequired>NONE</gameContentRequired>
</ModInfo>
```

---

## Future Enhancements

### Phase 2: Opinion-Based Filtering
- Only allow candidates from families with minimum opinion threshold
- Creates political tension - must keep families happy to have candidates

### Phase 3: Election History Tracking
- Use memories to track how many terms a leader has served
- Display term count in event text

### Phase 4: Election Consequences
- Losing candidates gain "Slighted" or opinion penalties
- Family of winner gains opinion bonus
- Trigger follow-up events based on election results

### Phase 5: Term Limits
- Use memories to track terms served
- Block characters who have served N terms
- Create "elder statesman" role for term-limited leaders

### Phase 6: Faction Events
- "Restless Oligarchs" - families demand more candidates
- "Oligarch Ambitions" - powerful family tries to establish dynasty
- "Wisdom of the Oligarchs" - council grants bonuses

---

## Known Limitations

1. **Cannot dynamically generate candidate pool** - must define fixed subject slots
2. **Event may not fire if minimum candidates unavailable** - 2-candidate fallback helps
3. **No "campaign" period** - election is instant
4. **AI nations unaffected** - would need `bAI=1` and AI decision logic
5. **No visual indicator of upcoming election** - players must track turns

---

## Testing Checklist

### Basic Functionality
- [ ] Election fires on turn 10, 20, 30, etc.
- [ ] 3-candidate event fires when 2+ family heads exist
- [ ] 2-candidate fallback fires in early game
- [ ] Only one election event fires per cycle (cooldown works across all 10 variants)
- [ ] Leader changes correctly with no errors
- [ ] No "bypassed heir" opinion penalties
- [ ] Old leader remains in court (doesn't die/vanish)

### Turn-Scaled Legitimacy
- [ ] **T1 (turns 10-30)**: New leader gets +8, re-election gets +3
- [ ] **T2 (turns 31-60)**: New leader gets +15, re-election gets +5
- [ ] **T3 (turns 61-100)**: New leader gets +25, re-election gets +8
- [ ] **T4 (turns 101-150)**: New leader gets +35, re-election gets +12
- [ ] **T5 (turns 151+)**: New leader gets +45, re-election gets +15
- [ ] Correct tier event fires based on current turn

### Family Opinion Effects
- [ ] Winner's family gains +20 opinion (check tooltip for "Elevated Our Candidate")
- [ ] Other families lose -20 opinion (check tooltip for "Passed Over")
- [ ] Memory effects expire after 20 turns
- [ ] Re-election does NOT change family opinions

### Re-election
- [ ] Re-election option appears when all 3 families are Cautious+
- [ ] Re-election option hidden when any family is Upset or worse

### Compatibility
- [ ] Works with all nations
- [ ] No conflict with vanilla succession events

---

## References

- `Reference/Source/Base/Game/GameCore/Player.cs` - Succession and legitimacy logic
- `Reference/Source/Base/Game/GameCore/PlayerEvent.cs` - Event selection system
- `Reference/XML/Infos/eventStory.xml` - Event examples
- `Reference/XML/Infos/bonus.xml` - Bonus field definitions
- `Reference/XML/Infos/subject.xml` - Subject type definitions
- `docs/modding-guide.md` - General modding reference
- `docs/event-lottery-weight-system.md` - Event selection mechanics
