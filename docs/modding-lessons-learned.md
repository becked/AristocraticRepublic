# Aristocratic Republic — Modding Notes

Mod-specific patterns and debugging notes. For general XML modding reference, see [modding-guide-xml.md](modding-guide-xml.md).

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

## Debugging Tips

1. **Check logs:** `~/Library/Logs/OldWorld/Player.log`
2. **Verify mod loads:** Look for `[ModPath] Setting ModPath: .../YourMod/`
3. **No errors doesn't mean success:** The game silently ignores malformed XML
4. **Compare to working mods:** Philosophy of Science is a good reference
5. **Test incrementally:** Start with minimal event, add complexity gradually
