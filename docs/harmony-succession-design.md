# Harmony Succession System — Design Document

## Problem Statement

The XML-only Aristocratic Republic mod has limitations because the game's succession system is hardcoded in C#:

1. **The game tracks a dynastic heir between elections.** The UI always shows "Heir: [character]" based on the active succession law. The succession list is recalculated constantly — on births, deaths, trait changes, law changes, and every turn.

2. **Heir-bypassed events fire during republic games.** As the succession list shifts, `doSuccessionBypass()` (Player.cs:12599) creates `RELATIONSHIP_BYPASSED_BY` relationships. Seven vanilla `EVENTSTORY_HEIR_BYPASSED_*` events then fire — causing murders, coups, imprisonments, and exile that are nonsensical in a republic.

3. **Mid-term leader death falls back to dynastic succession.** If the leader dies between elections, `chooseNextLeader()` installs the dynastic heir automatically. If no heir exists under the current law, it tries ALL succession orders and auto-switches to one that produces an heir — completely bypassing the election system.

4. **Vanilla succession events interfere.** 44 events triggered by `EVENTTRIGGER_SUCCESSION_US` fire after every leader change (including elections). Many reference crowns, thrones, heirs, regents, and coronations — thematically incompatible with a republic.

## Solution Overview

| Component | Approach | Purpose |
|-----------|----------|---------|
| Elective law | `law-add.xml` + `successionOrder-add.xml` | Visible "Elective" law in laws UI |
| Law auto-assignment | Harmony postfix on `Game.start()` | Set LAW_ELECTIVE for human players at game start |
| Suppress heir list | Harmony prefix on `Player.findHeir()` | Return null for ALL succession orders when elective |
| Suppress SUCCESSION_US events | Harmony prefix/postfix on `Player.addLeader()` | Flag-based suppression during leader installation |
| Block vanilla ORDER laws | Harmony postfix on `Player.canStartLaw()` | Prevent switching away from elective |
| Emergency election | XML events on `EVENTTRIGGER_SUCCESSION_FAIL` | Handle mid-term leader death |
| Disable heir-bypass events | `eventStory-change.xml` | `iMinTurns=200` on 8 vanilla events |

---

## Detailed Design

### 1. Custom Elective Law (XML)

**`Infos/successionOrder-add.xml`**
```xml
<Entry>
  <zType>SUCCESSIONORDER_ELECTIVE</zType>
  <Name>TEXT_SUCCESSIONORDER_ELECTIVE</Name>
  <Help>TEXT_SUCCESSIONORDER_ELECTIVE_HELP</Help>
</Entry>
```

**`Infos/law-add.xml`**
```xml
<Entry>
  <zType>LAW_ELECTIVE</zType>
  <Name>TEXT_LAW_ELECTIVE</Name>
  <zIconName>LAW_SENIORITY</zIconName>
  <iCostBase>0</iCostBase>
  <iCostPerChange>0</iCostPerChange>
  <LawClass>LAWCLASS_ORDER</LawClass>
  <SuccessionOrder>SUCCESSIONORDER_ELECTIVE</SuccessionOrder>
</Entry>
```

- `iCostBase=0` — free to enact (auto-assigned by Harmony at game start)
- No `iSwitchCostBase` — switching is blocked by Harmony Patch 4 (CanStartLawPatch) instead of cost
- `SUCCESSIONORDER_ELECTIVE` — custom order; the game's `findHeir()` dispatcher would fall through to other orders, but our Harmony prefix intercepts for ALL orders

### 2. Harmony Patches

All patches live in `Source/RepublicMod.cs`. Five patch classes plus the mod entry point.

#### Entry Point: `RepublicMod : ModEntryPointAdapter`

```csharp
public class RepublicMod : ModEntryPointAdapter
{
    private static Harmony _harmony;
    private const string HarmonyId = "com.aristocraticrepublic";

    // Cached type lookups (resolved lazily via EnsureTypesResolved)
    internal static LawType ElectiveLawType = LawType.NONE;
    internal static LawClassType OrderLawClass = LawClassType.NONE;
    internal static EventTriggerType SuccessionUsTrigger = EventTriggerType.NONE;

    // Flag for Patch 3: suppress SUCCESSION_US events during addLeader
    [ThreadStatic]
    internal static bool SuppressSuccessionEvents;
}
```

**Initialization flow:**

- `Initialize()` — applies Harmony patches (attribute-based via `PatchAll()` + manual registration for `addLeader`)
- `Game.start()` postfix (Patch 1) — resolves types from `game.infos()` and assigns LAW_ELECTIVE to human players
- `FindHeirPatch` (Patch 2) — lazy type resolution fallback for save/load (where `Game.start()` doesn't fire)

**Key design decision:** `OnGameServerReady` fires before the client has a Game reference (reflection through `AppMain.gApp.Client.Game` returns null). `Game.start()` is the correct hook — the Game instance is passed directly as `__instance`.

#### Patch 1: Game Initialization (`GameStartPatch`)

**Target:** `Game.start()`
**Type:** Postfix (attribute-based)
**Purpose:** Resolve custom type IDs and auto-assign LAW_ELECTIVE to human players.

Calls `InitializeGameState(Game)` which:
1. Resolves `ElectiveLawType`, `OrderLawClass`, `SuccessionUsTrigger` from `game.infos()`
2. Caches `makeActiveLaw` MethodInfo via reflection (protected method)
3. Iterates human players and assigns LAW_ELECTIVE if not already active

For save/load, `Game.start()` doesn't fire — type resolution falls back to lazy init in Patch 2.

#### Patch 2: Suppress Heir List (`FindHeirPatch`)

**Target:** `Player.findHeir(SuccessionOrderType, SuccessionGenderType, List<int>)`
**Type:** Prefix (attribute-based)
**Purpose:** Return null for ALL succession orders when the player is elective.

```csharp
static bool Prefix(Player __instance, ref Character __result)
{
    RepublicMod.EnsureTypesResolved(__instance.game().infos()); // lazy fallback
    if (!RepublicMod.IsElective(__instance)) return true;

    __result = null;
    return false; // Skip original — succession list stays empty
}
```

**Why ALL orders, not just ELECTIVE?** `chooseNextLeader()` iterates every succession order calling `findHeir()` for each. If we only intercepted `SUCCESSIONORDER_ELECTIVE`, Primogeniture/Lateral/etc. would return heirs, and the game would auto-switch to that law. By returning null for all orders, `chooseNextLeader()` naturally falls through to `SUCCESSION_FAIL_EVENTTRIGGER` — which triggers our emergency election.

This is the lynchpin — it cascades through the entire system:
- `updateSuccession()` → `findSuccessionList()` → `findHeir()` → null → succession list empty
- `heir()` returns null → UI shows no heir
- `doSuccessionBypass()` compares empty to empty → no bypass relationships form
- `chooseNextLeader()` finds no heir under any law → fires SUCCESSION_FAIL

#### Patch 3a: Set Flag During addLeader (`AddLeaderPatch`)

**Target:** `Player.addLeader(int)` — protected virtual, registered manually in `Initialize()`
**Type:** Prefix + Postfix
**Purpose:** Set `SuppressSuccessionEvents = true` for elective players during leader installation.

Manual registration is required because `addLeader` is protected — Harmony's `[HarmonyPatch]` attribute can't resolve it.

#### Patch 3b: Skip SUCCESSION_US Events (`DoEventTriggerPatch`)

**Target:** `Player.doEventTrigger` (9-parameter overload)
**Type:** Prefix (attribute-based)
**Purpose:** When `SuppressSuccessionEvents` is set, skip `SUCCESSION_US_EVENTTRIGGER`.

Only suppresses `SUCCESSION_US` — allows `SUCCESSION_THEM` so AI players can react to our leadership changes.

#### Patch 4: Block Vanilla ORDER Laws (`CanStartLawPatch`)

**Target:** `Player.canStartLaw(LawType, bool, bool, bool)`
**Type:** Postfix (attribute-based)
**Purpose:** Prevent elective players from switching to vanilla succession laws.

```csharp
static void Postfix(Player __instance, LawType eLaw, ref bool __result)
{
    if (!__result) return; // Already blocked
    if (!RepublicMod.IsElective(__instance)) return;

    Infos infos = __instance.game().infos();
    if (infos.law(eLaw).meLawClass == RepublicMod.OrderLawClass
        && eLaw != RepublicMod.ElectiveLawType)
    {
        __result = false;
    }
}
```

Returns false for any ORDER-class law that isn't LAW_ELECTIVE. Vanilla laws still appear in the UI (greyed out) because `ChooseInheritancePopup` lists all laws in the class and uses `canStartLaw` only for the enabled/disabled state.

### 3. Emergency Election Events (XML)

Triggered by `EVENTTRIGGER_SUCCESSION_FAIL` when the leader dies mid-term.

9 events: 3 candidate-count variants × 3 difficulty presets. Naming pattern:
`EVENTSTORY_REPUBLIC_EMERGENCY_{candidates}_{preset}` (e.g., `_3_STABLE`, `_2_STRAINED`)

Same priority cascade and `aeGameOptionInvalid` gating as regular elections. Reuses the `BONUS_REPUBLIC_ELECT_*` bonuses (with `iSeizeThroneSubject`). No re-election option since the leader is dead.

### 4. Vanilla Event Suppression (XML)

**`Infos/eventStory-change.xml`** — Uses `iMinTurns=200` (Mohawk's standard suppression pattern):

1. `EVENTSTORY_HEIR_BYPASSED_SITUATION`
2. `EVENTSTORY_HEIR_BYPASSED_MURDER`
3. `EVENTSTORY_HEIR_BYPASSED_DOUBLE_MURDER`
4. `EVENTSTORY_HEIR_BYPASSED_DOUBLE_MURDER_NO_SPOUSE`
5. `EVENTSTORY_HEIR_BYPASSED_LEADER_MURDER`
6. `EVENTSTORY_HEIR_BYPASSED_LEADER_IMPRISON`
7. `EVENTSTORY_HEIR_BYPASSED_LEADER_EXILE`
8. `EVENTSTORY_FAMILY_SUCCESSION`

The 44 `SUCCESSION_US` events are handled by Harmony Patch 3b — more robust than XML because it covers all events plus any added by future DLC.

---

## XML Loading Pitfall

**Critical lesson learned:** `-add.xml` files register ALL `zType` entries in an `addedTypes` set during type registration. When base game data is read, types in `addedTypes` are **skipped** (their fields reset to defaults). This means partial `-add.xml` entries for vanilla types destroy their base data.

Example: adding vanilla law types to `law-add.xml` to change their cost caused `meLawClass` to reset to `NONE` (-1), which crashed `Player.getActiveLaw(LawClassType)` with `IndexOutOfRangeException` — hanging the game between turns as AI `doLawPlanning()` looped on the error.

Solution: Use `-change.xml` for modifying existing entries, or use Harmony patches. We chose Harmony (CanStartLawPatch) to block law switching entirely rather than just inflating costs.

---

## Succession Flow With Patches

```
Normal gameplay (between elections):
  updateSuccession() → findSuccessionList() → findHeir()
                                                  ↓
                                     [Patch 2: return null for elective player]
                                                  ↓
                                     Succession list = empty
                                     heir() = null → UI shows no heir
                                     doSuccessionBypass() → no-op (empty == empty)

Regular election (every 10 turns):
  EVENTTRIGGER_NEW_TURN → election event fires →
  player picks candidate → BONUS_REPUBLIC_ELECT_* →
  iSeizeThroneSubject → abdicateStart() → makeNextLeader() → addLeader()
                                                                    ↓
                                                         [Patch 3a: set flag]
                                                         [Patch 3b: suppress
                                                          SUCCESSION_US trigger]
                                                         [Patch 3a: clear flag]

Mid-term leader death:
  Character.die() → Player.chooseNextLeader()
                          ↓
                   Iterates all succession orders
                   All return null from Patch 2
                          ↓
                   Fires SUCCESSION_FAIL_EVENTTRIGGER
                          ↓
                   EVENTSTORY_REPUBLIC_EMERGENCY_* fires
                          ↓
                   Player picks candidate → iSeizeThroneSubject → addLeader()
                                                                       ↓
                                                            [Patches 3a/3b: suppress]
```

---

## Project Structure

```
AristocraticRepublic/
├── AristocraticRepublic.csproj      # C# build file (net472, Lib.Harmony 2.4.2)
├── Source/
│   └── RepublicMod.cs               # Entry point + all Harmony patches
├── ModInfo.xml
├── Infos/
│   ├── eventStory-add.xml           # Regular + emergency election events
│   ├── eventStory-change.xml        # Suppress 8 vanilla events
│   ├── law-add.xml                  # LAW_ELECTIVE
│   ├── successionOrder-add.xml      # SUCCESSIONORDER_ELECTIVE
│   ├── bonus-event-add.xml          # Legitimacy bonuses and memory assignments
│   ├── gameOption-add.xml           # Difficulty preset toggles
│   ├── memory-family-add.xml        # Family opinion memory effects
│   ├── text-add.xml                 # UI text strings (needs UTF-8 BOM)
│   └── text-new-add.xml             # Additional text strings (needs UTF-8 BOM)
├── scripts/
│   └── deploy.sh                    # Build + deploy (dotnet build, copy DLLs + XML)
└── docs/
    └── harmony-succession-design.md # This document
```

### Build

- Target: `net472`
- NuGet: `Lib.Harmony` 2.4.2
- References: `TenCrowns.GameCore.dll`, `Mohawk.SystemCore.dll`, `UnityEngine.dll`, `UnityEngine.CoreModule.dll` (all from game's Managed directory)
- Build: `dotnet build -p:OldWorldPath="$OLDWORLD_PATH"`
- Deploy: `./scripts/deploy.sh` (builds, copies DLLs + mod files to game's mods directory)
