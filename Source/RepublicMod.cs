using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TenCrowns.AppCore;
using TenCrowns.GameCore;
using UnityEngine;

namespace AristocraticRepublic
{
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

        // Reflection cache for makeActiveLaw (protected method)
        private static MethodInfo _makeActiveLawMethod;

        public override void Initialize(ModSettings modSettings)
        {
            base.Initialize(modSettings);

            if (_harmony != null) return; // Triple-load guard

            try
            {
                _harmony = new Harmony(HarmonyId);

                // Apply attribute-based patches (FindHeirPatch, DoEventTriggerPatch)
                _harmony.PatchAll();

                // Manual patch for addLeader (protected virtual — can't use attributes)
                var addLeaderMethod = AccessTools.Method(typeof(Player), "addLeader", new Type[] { typeof(int) });
                if (addLeaderMethod != null)
                {
                    _harmony.Patch(
                        addLeaderMethod,
                        prefix: new HarmonyMethod(typeof(AddLeaderPatch), nameof(AddLeaderPatch.Prefix)),
                        postfix: new HarmonyMethod(typeof(AddLeaderPatch), nameof(AddLeaderPatch.Postfix))
                    );
                    Debug.Log("[AristocraticRepublic] addLeader patch applied");
                }
                else
                {
                    Debug.LogWarning("[AristocraticRepublic] Could not find Player.addLeader(int)");
                }

                Debug.Log("[AristocraticRepublic] Harmony patches applied");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AristocraticRepublic] Failed to apply patches: {ex}");
            }
        }

        /// <summary>Resolve custom type IDs from Infos. Idempotent — skips if already resolved.</summary>
        internal static void EnsureTypesResolved(Infos infos)
        {
            if (ElectiveLawType != LawType.NONE) return;

            ElectiveLawType = infos.getType<LawType>("LAW_ELECTIVE");
            if (ElectiveLawType == LawType.NONE) return; // XML not loaded yet

            OrderLawClass = infos.law(ElectiveLawType).meLawClass;
            SuccessionUsTrigger = infos.Globals.SUCCESSION_US_EVENTTRIGGER;

            Debug.Log($"[AristocraticRepublic] Resolved types: Law={ElectiveLawType}, LawClass={OrderLawClass}, Trigger={SuccessionUsTrigger}");
        }

        /// <summary>Full initialization for new games: resolve types + assign law to human players.</summary>
        internal static void InitializeGameState(Game game)
        {
            EnsureTypesResolved(game.infos());
            if (ElectiveLawType == LawType.NONE)
            {
                Debug.LogError("[AristocraticRepublic] LAW_ELECTIVE not found in infos");
                return;
            }

            // Cache makeActiveLaw reflection
            if (_makeActiveLawMethod == null)
            {
                _makeActiveLawMethod = AccessTools.Method(typeof(Player), "makeActiveLaw", new Type[] { typeof(LawType), typeof(bool) });
                if (_makeActiveLawMethod == null)
                {
                    Debug.LogError("[AristocraticRepublic] Could not find Player.makeActiveLaw(LawType)");
                    return;
                }
            }

            // Auto-assign LAW_ELECTIVE to human players
            foreach (Player player in game.getPlayers())
            {
                if (player == null) continue;
                if (!player.isHuman()) continue;

                if (!player.isActiveLaw(ElectiveLawType))
                {
                    _makeActiveLawMethod.Invoke(player, new object[] { ElectiveLawType, false });
                    Debug.Log($"[AristocraticRepublic] Assigned LAW_ELECTIVE to player {player.getPlayer()}");
                }
            }
        }

        public override void Shutdown()
        {
            ElectiveLawType = LawType.NONE;
            OrderLawClass = LawClassType.NONE;
            SuccessionUsTrigger = EventTriggerType.NONE;
            _makeActiveLawMethod = null;
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
            base.Shutdown();
        }

        /// <summary>Check if a player uses elective succession.</summary>
        internal static bool IsElective(Player player)
        {
            if (ElectiveLawType == LawType.NONE) return false;
            return player.isActiveLaw(ElectiveLawType);
        }

    }

    // =========================================================================
    // Patch 1: Initialize types and assign law once Game is ready
    // Game.start() fires for new games after all players are initialized.
    // For save/load, FindHeirPatch handles lazy type resolution.
    // =========================================================================
    [HarmonyPatch(typeof(Game), nameof(Game.start))]
    public static class GameStartPatch
    {
        static void Postfix(Game __instance)
        {
            try
            {
                RepublicMod.InitializeGameState(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AristocraticRepublic] GameStartPatch error: {ex}");
            }
        }
    }

    // =========================================================================
    // Patch 2: Suppress heir list for elective players
    // Target: Player.findHeir(SuccessionOrderType, SuccessionGenderType, List<int>)
    //
    // Returns null for ALL succession orders when the player is elective.
    // chooseNextLeader iterates all succession orders — all return null —
    // and the method naturally falls through to SUCCESSION_FAIL_EVENTTRIGGER.
    // =========================================================================
    [HarmonyPatch(typeof(Player), nameof(Player.findHeir),
        new Type[] { typeof(SuccessionOrderType), typeof(SuccessionGenderType), typeof(List<int>) })]
    public static class FindHeirPatch
    {
        static bool Prefix(Player __instance, ref Character __result)
        {
            try
            {
                // Lazy type resolution for save/load (Game.start() doesn't fire)
                RepublicMod.EnsureTypesResolved(__instance.game().infos());

                if (!RepublicMod.IsElective(__instance)) return true;

                __result = null;
                return false; // Skip original — succession list stays empty
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AristocraticRepublic] FindHeirPatch error: {ex}");
                return true; // Fail open
            }
        }
    }

    // =========================================================================
    // Patch 3a: Set/clear flag during addLeader for elective players
    // Target: Player.addLeader(int) — protected virtual, registered manually
    // =========================================================================
    public static class AddLeaderPatch
    {
        public static void Prefix(Player __instance)
        {
            try
            {
                RepublicMod.SuppressSuccessionEvents = RepublicMod.IsElective(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AristocraticRepublic] AddLeaderPatch error: {ex}");
            }
        }

        public static void Postfix()
        {
            RepublicMod.SuppressSuccessionEvents = false;
        }
    }

    // =========================================================================
    // Patch 3b: Skip SUCCESSION_US events when flag is set
    // Target: Player.doEventTrigger (9-param overload in PlayerEvent.cs partial class)
    //
    // Only suppresses SUCCESSION_US — allows SUCCESSION_THEM so AI can react
    // to our leadership changes.
    // =========================================================================
    [HarmonyPatch(typeof(Player), nameof(Player.doEventTrigger),
        new Type[] {
            typeof(EventTriggerType), typeof(int), typeof(bool), typeof(ulong),
            typeof(EventClassType), typeof(EventLinkType), typeof(bool), typeof(bool),
            typeof(List<object>) })]
    public static class DoEventTriggerPatch
    {
        static bool Prefix(EventTriggerType eTrigger, ref bool __result)
        {
            try
            {
                if (!RepublicMod.SuppressSuccessionEvents) return true;
                if (eTrigger != RepublicMod.SuccessionUsTrigger) return true;

                // Suppress SUCCESSION_US events for elective players
                __result = false;
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AristocraticRepublic] DoEventTriggerPatch error: {ex}");
                return true; // Fail open
            }
        }
    }

    // =========================================================================
    // Patch 4: Block vanilla ORDER laws for elective players
    // Target: Player.canStartLaw(LawType, bool, bool, bool)
    //
    // Prevents elective players from switching to vanilla succession laws.
    // Returns false for any ORDER-class law that isn't LAW_ELECTIVE.
    // =========================================================================
    [HarmonyPatch(typeof(Player), nameof(Player.canStartLaw),
        new Type[] { typeof(LawType), typeof(bool), typeof(bool), typeof(bool) })]
    public static class CanStartLawPatch
    {
        static void Postfix(Player __instance, LawType eLaw, ref bool __result)
        {
            try
            {
                if (!__result) return; // Already blocked — nothing to do
                if (RepublicMod.OrderLawClass == LawClassType.NONE) return;
                if (!RepublicMod.IsElective(__instance)) return;

                // Block non-elective ORDER laws
                Infos infos = __instance.game().infos();
                if (infos.law(eLaw).meLawClass == RepublicMod.OrderLawClass && eLaw != RepublicMod.ElectiveLawType)
                {
                    __result = false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AristocraticRepublic] CanStartLawPatch error: {ex}");
            }
        }
    }
}
