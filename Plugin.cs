using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Pigeon.Movement;
using UnityEngine;

namespace GatsWheel;

    [MycoMod(null, ModFlags.IsClientSide)]
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "hed14.gatswheel";
        public const string PluginName = "GatsWheel";
        public const string PluginVersion = "0.1.2";

        // Add this constant for the Gats fallback cost
        public const int GatsFallbackCost = 499;

        internal new static ManualLogSource Logger;

        private void Awake()
        {
            Logger = base.Logger;
            Logger.LogInfo($"Plugin {PluginGUID} is loaded!");

            var harmony = new Harmony(PluginGUID);
            harmony.PatchAll();
            Logger.LogInfo("Harmony patches applied.");
        }
    }

    [HarmonyPatch(typeof(RewardSpinner), "HasRequiredResources")]
    class HasRequiredResourcesPatch
    {
        static void Postfix(RewardSpinner __instance, ref bool __result, ref PlayerResource firstAvailable)
        {
            if (__result) return;

            var costArray = AccessTools.Field(typeof(RewardSpinner), "cost")
                .GetValue(__instance) as ResourceCost[];
            if (costArray == null)
            {
                __result = false;
                return;
            }

            // First pass: check primary resources only
            for (int i = 0; i < costArray.Length; i++)
            {
                if (firstAvailable == null)
                    firstAvailable = costArray[i].resource;

                if (PlayerData.Instance.GetResource(costArray[i].resource) >= costArray[i].count)
                {
                    firstAvailable = costArray[i].resource;
                    __result = true;
                    return;
                }
            }

            // Second pass: only check Gats fallback if no primary resource worked
            var gats = PlayerResource.GetResource("scrip");
            if (__result != true && gats != null && PlayerData.Instance.GetResource(gats) >= Plugin.GatsFallbackCost)
            {
                firstAvailable = gats;
                __result = true;
                return;
            }

            __result = false;
        }
    }

    [HarmonyPatch(typeof(RewardSpinner), "OnImmediateInteract")]
    class OnImmediateInteractPatch
    {
        static bool Prefix(RewardSpinner __instance, Player player)
        {
            // Access private fields
            bool isSpinning = (bool)AccessTools.Field(typeof(RewardSpinner), "isSpinning").GetValue(__instance);
            if (isSpinning) return false;

            var costArray = AccessTools.Field(typeof(RewardSpinner), "cost")
                .GetValue(__instance) as ResourceCost[];
            if (costArray == null) return true;

            bool flag = false;

            for (int i = 0; i < costArray.Length; i++)
            {
                var resource = costArray[i].resource;
                var amount = costArray[i].count;

                if (PlayerData.Instance.TryRemoveResource(resource, amount))
                {
                    flag = true;
                    Plugin.Logger.LogInfo($"RewardSpinner used {resource.Name} for spin.");
                    break;
                }
            }

            // Try Gats fallback with fixed cost ONLY if primary fails
            var gats = PlayerResource.GetResource("scrip");
            if (flag != true && gats != null && PlayerData.Instance.TryRemoveResource(gats, Plugin.GatsFallbackCost))
            {
                flag = true;
                Plugin.Logger.LogInfo($"RewardSpinner used Gats fallback ({Plugin.GatsFallbackCost}) for spin.");
            }

        if (!flag)
            {
                var abilityErrorSound = AccessTools.Field(typeof(Global), "AbilityErrorSound").GetValue(Global.Instance);
                AccessTools.Method(abilityErrorSound.GetType(), "Post").Invoke(abilityErrorSound, new object[] { PlayerLook.Instance.gameObject });
                return false;
            }

            // StartSpin via reflection
            var rewards = AccessTools.Field(typeof(RewardSpinner), "rewards").GetValue(__instance);
            var properties = AccessTools.Property(rewards.GetType(), "Properties").GetValue(rewards) as LevelUnlock[];
            var startSpin = AccessTools.Method(typeof(RewardSpinner), "StartSpin");
            startSpin.Invoke(__instance, new object[] { Random.Range(0, properties.Length), true });

            return false;
        }
    }

    [HarmonyPatch(typeof(RewardSpinner), "InteractText", MethodType.Getter)]
    class InteractTextPatch
    {
        static void Postfix(RewardSpinner __instance, ref string __result)
        {
            var costArray = AccessTools.Field(typeof(RewardSpinner), "cost")
                .GetValue(__instance) as ResourceCost[];
            if (costArray == null) return;

            // pass null directly as the out param placeholder
            object[] parameters = new object[] { null };
            bool hasResources = (bool)AccessTools.Method(typeof(RewardSpinner), "HasRequiredResources")
                .Invoke(__instance, parameters);
            PlayerResource firstAvailable = (PlayerResource)parameters[0];

            if (firstAvailable != null && firstAvailable.ID == "scrip")
            {
                int gatsCost = Plugin.GatsFallbackCost;
                __result = $"Use {gatsCost} Gats";
            }
        }
    }

