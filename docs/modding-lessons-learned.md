# Old World Modding Lessons Learned

This document captures troubleshooting findings from developing the Aristocratic Republic mod.

## Critical: UTF-8 BOM Required for Text Files

Text files (`text-*-add.xml`) **must** have a UTF-8 BOM (byte order mark: `ef bb bf`) at the start of the file. Without the BOM, the game silently fails to load the text and events won't fire.

**Symptom:** Events never fire, no errors in logs.

**Fix:** Add BOM to text files:
```bash
printf '\xef\xbb\xbf' > temp.xml && cat original.xml >> temp.xml && mv temp.xml original.xml
```

**Verify BOM exists:**
```bash
xxd yourfile.xml | head -1
# Should start with: efbb bf3c 3f78 6d6c
```

Note: `eventStory-add.xml` does NOT need a BOM, only text files.

## File Naming Conventions

| File Type | Correct Name | Notes |
|-----------|--------------|-------|
| Text strings | `text-new-add.xml` | `text-add.xml` does NOT work; needs UTF-8 BOM |
| Event stories | `eventStory-add.xml` | No BOM needed |
| Event bonuses | `bonus-event-add.xml` | No `encoding="utf-8"` in XML declaration |
| Family memories | `memory-family-add.xml` | |
| Event options | `eventOption-add.xml` | Only if using separate file (not recommended) |

## XML Format: Old vs New Syntax

The game supports two XML formats. Use the **new format** for reliability:

### New Format (Recommended)
```xml
<Subjects>
    <Subject alias="leader">
        <Type>SUBJECT_LEADER_US</Type>
    </Subject>
    <Subject alias="candidate">
        <Type>SUBJECT_FAMILY_HEAD_US</Type>
        <Extra>SUBJECT_ADULT</Extra>
        <Extra>SUBJECT_HEALTHY</Extra>
    </Subject>
</Subjects>
<EventOptions>
    <EventOption>
        <Text>TEXT_EVENTOPTION_EXAMPLE</Text>
    </EventOption>
</EventOptions>
```

### Old Format (Avoid)
```xml
<aeSubjects>
    <zValue>SUBJECT_LEADER_US</zValue>
    <zValue>SUBJECT_FAMILY_HEAD_US</zValue>
</aeSubjects>
<aeOptions>
    <zValue>EVENTOPTION_EXAMPLE</zValue>
</aeOptions>
```

## Text String Format

Use `<en-US>` tag for English text, **not** `<English>`:

```xml
<Entry>
    <zType>TEXT_EVENTSTORY_EXAMPLE</zType>
    <en-US>Your text here with {CHARACTER-0} references.</en-US>
</Entry>
```

## Character/Subject References in Text

Use **numeric indices** based on subject order, not alias names:

| Subject Index | Reference | Short Form |
|---------------|-----------|------------|
| 0 | `{CHARACTER-0}` | `{CHARACTER-SHORT-0}` |
| 1 | `{CHARACTER-1}` | `{CHARACTER-SHORT-1}` |
| 2 (if family) | `{FAMILY-2}` | - |

**Wrong:** `{leader:name}`, `{candidate}`
**Correct:** `{CHARACTER-0}`, `{CHARACTER-1}`

## Event Triggers

### The Trigger=NONE Problem

Events without an explicit `<Trigger>` element default to `EVENTTRIGGER_NONE`. These events go through a **random probability gate** based on event level:

| Event Level | Chance Per Turn |
|-------------|-----------------|
| High | 1/4 = 25% |
| Moderate | 1/6 = ~17% |
| Low | 1/8 = 12.5% |

**Symptom:** Event fires sporadically, not every turn as expected.

### Solution: Use Explicit Trigger

Add `<Trigger>EVENTTRIGGER_NEW_TURN</Trigger>` for reliable per-turn firing:

```xml
<Trigger>EVENTTRIGGER_NEW_TURN</Trigger>
<iMinTurns>1</iMinTurns>
<iPriority>10</iPriority>
<iWeight>10</iWeight>
<iProb>100</iProb>
<iRepeatTurns>2</iRepeatTurns>
```

`EVENTTRIGGER_NEW_TURN` has no probability gate - it fires every turn.

## Event Priority and Weight

| Field | Description | Recommended Value |
|-------|-------------|-------------------|
| `iPriority` | Higher beats lower; vanilla max is 9 | 10+ to beat vanilla |
| `iWeight` | Relative weight in lottery | 1-20 |
| `iProb` | % chance to enter pool (0 or 100 = always) | 100 |
| `iRepeatTurns` | Turns between firings; -1 = once ever | As needed |

## Conditional Event Options

Use `<IndexSubject>` to show/hide options based on subject conditions:

```xml
<EventOption>
    <Text>TEXT_EVENTOPTION_KEEP_LEADER</Text>
    <IndexSubject>
        <Pair>
            <First>2</First>  <!-- Subject index -->
            <Second>SUBJECT_FAMILY_MIN_PLEASED</Second>  <!-- Condition -->
        </Pair>
    </IndexSubject>
</EventOption>
```

This option only appears if subject at index 2 meets `SUBJECT_FAMILY_MIN_PLEASED`.

### Useful Family Opinion Subjects

| Subject | Meaning |
|---------|---------|
| `SUBJECT_FAMILY_MIN_PLEASED` | Pleased or better (positive) |
| `SUBJECT_FAMILY_MIN_CAUTIOUS` | Cautious or better (neutral+) |
| `SUBJECT_FAMILY_MAX_CAUTIOUS` | Cautious or worse (neutral-) |

## Debugging Tips

1. **Check logs:** `~/Library/Logs/OldWorld/Player.log`
2. **Verify mod loads:** Look for `[ModPath] Setting ModPath: .../YourMod/`
3. **No errors doesn't mean success:** The game silently ignores malformed XML
4. **Compare to working mods:** Philosophy of Science is a good reference
5. **Test incrementally:** Start with minimal event, add complexity gradually

## iSeizeThroneSubject (Leader Succession)

To make a character become the new leader via event:

1. Add `SUBJECT_PLAYER_US` as a subject (e.g., at index 0)
2. Create a bonus with `<iSeizeThroneSubject>0</iSeizeThroneSubject>` pointing to that player subject
3. Apply the bonus TO the character who should become leader (via `<SubjectBonuses>`)

```xml
<!-- In eventStory-add.xml -->
<Subjects>
    <Subject alias="player">
        <Type>SUBJECT_PLAYER_US</Type>
    </Subject>
    <Subject alias="candidate">
        <Type>SUBJECT_FAMILY_HEAD_US</Type>
    </Subject>
</Subjects>
<EventOptions>
    <EventOption>
        <SubjectBonuses>
            <Pair>
                <First>candidate</First>
                <Second>BONUS_SEIZE_THRONE</Second>
            </Pair>
        </SubjectBonuses>
    </EventOption>
</EventOptions>

<!-- In bonus-event-add.xml -->
<Entry>
    <zType>BONUS_SEIZE_THRONE</zType>
    <iSeizeThroneSubject>0</iSeizeThroneSubject>
</Entry>
```

**Key insight**: The bonus is applied to the Character (who becomes leader), while `iSeizeThroneSubject` points to the Player (whose throne is seized).

## Don't Mix Inline and Separate Event Options

When using inline `<EventOptions>` with `<SubjectBonuses>` in eventStory-add.xml, do NOT also define the same options in a separate eventOption-add.xml file. They can conflict.

- **Inline format** (recommended): Uses aliases like `<First>candidate</First>`
- **Separate file format** (old): Uses positional `<zValue>` elements where position = subject index

## Linking Subjects with Relations

Use `<Relations>` inside a Subject to link it to another subject. Useful for linking families to their candidates:

```xml
<Subjects>
    <Subject alias="candidate1">
        <Type>SUBJECT_FAMILY_HEAD_US</Type>
    </Subject>
    <Subject alias="family1">
        <Type>SUBJECT_FAMILY_US</Type>
        <Relations>
            <Relation>
                <Type>SUBJECTRELATION_FAMILY_SAME</Type>
                <Target>candidate1</Target>
            </Relation>
        </Relations>
    </Subject>
</Subjects>
```

This ensures `family1` is always the same family as `candidate1`.

## Ensuring Different Families (SubjectNotRelations)

To ensure multiple candidates come from different families, use `<SubjectNotRelations>` with `<Triple>`:

```xml
<SubjectNotRelations>
    <Triple><First>2</First><Second>SUBJECTRELATION_FAMILY_SAME</Second><Third>3</Third></Triple>
    <Triple><First>2</First><Second>SUBJECTRELATION_FAMILY_SAME</Second><Third>4</Third></Triple>
    <Triple><First>3</First><Second>SUBJECTRELATION_FAMILY_SAME</Second><Third>4</Third></Triple>
</SubjectNotRelations>
```

This prevents subjects at indices 2, 3, and 4 from being in the same family.

## Family Memory System (Opinion Changes)

To give the winner's family +20 opinion and other families -20:

1. Create two memories with different levels:
```xml
<Entry>
    <zType>MEMORYFAMILY_ELEVATED</zType>
    <MemoryLevel>MEMORYLEVEL_POS_MEDIUM_SHORT</MemoryLevel>
</Entry>
<Entry>
    <zType>MEMORYFAMILY_PASSED_OVER</zType>
    <MemoryLevel>MEMORYLEVEL_NEG_LOW_SHORT</MemoryLevel>
</Entry>
```

2. Apply both in the bonus:
```xml
<Entry>
    <zType>BONUS_ELECT</zType>
    <Memory>MEMORYFAMILY_ELEVATED</Memory>
    <MemoryAllFamilies>MEMORYFAMILY_PASSED_OVER</MemoryAllFamilies>
</Entry>
```

- `<Memory>` applies to the character's family (winner only)
- `<MemoryAllFamilies>` applies to ALL families

Net effect: Winner gets both (+X - Y), others get only (-Y).

## Shared Cooldown Between Events

To make multiple events share a cooldown (e.g., 15 election variants), use `aeEventStoryRepeatTurns` with **bidirectional linking**:

```xml
<!-- In EVENTSTORY_A -->
<aeEventStoryRepeatTurns>
    <zValue>EVENTSTORY_B</zValue>
    <zValue>EVENTSTORY_C</zValue>
</aeEventStoryRepeatTurns>

<!-- In EVENTSTORY_B -->
<aeEventStoryRepeatTurns>
    <zValue>EVENTSTORY_A</zValue>
    <zValue>EVENTSTORY_C</zValue>
</aeEventStoryRepeatTurns>

<!-- In EVENTSTORY_C -->
<aeEventStoryRepeatTurns>
    <zValue>EVENTSTORY_A</zValue>
    <zValue>EVENTSTORY_B</zValue>
</aeEventStoryRepeatTurns>
```

Each event must list all OTHER events. One-way linking does NOT work.

## Common Pitfalls

1. **Missing BOM on text file** - Events silently fail
2. **Wrong text file name** - `text-add.xml` doesn't work, use `text-new-add.xml`
3. **Using `<English>` instead of `<en-US>`** - Text won't load
4. **Missing explicit trigger** - Event fires randomly instead of reliably
5. **Using alias names in text** - Use numeric indices like `{CHARACTER-0}`
6. **Extra directories in mod** - Can cause loading errors (keep only `Infos/`, `ModInfo.xml`, images)
7. **Mixing inline and separate event options** - Can cause bonuses to apply to wrong subjects
8. **One-way cooldown linking** - Must be bidirectional for shared cooldowns to work
