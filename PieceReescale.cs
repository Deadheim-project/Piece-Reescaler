using BepInEx;
using UnityEngine;
using HarmonyLib;
using BepInEx.Configuration;
using Jotunn.Managers;
using System;
using Jotunn.Utils;
using Jotunn.Entities;
using Jotunn.Configs;
using System.Collections.Generic;
using System.Linq;

namespace PieceReescale
{
    [BepInPlugin(PluginGUID, PluginGUID, Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class PieceReescale : BaseUnityPlugin
    {
        public const string PluginGUID = "Detalhes.PieceReescale";
        public const string Name = "PieceReescale";
        public const string Version = "1.0.0";

        public static bool listInitiliazed = false;
        public static GameObject prefabToRemove;

        Harmony harmony = new Harmony(PluginGUID);

        public static ConfigEntry<string> ScalesList;
        public static ConfigEntry<int> MaxPlaceDistance;
        public static ConfigEntry<int> CostItemsMultiplier;
        public static ConfigEntry<bool> DropItemsOnDestroy;
        public static ConfigEntry<bool> OnlyAdminCanBuild;
        public static ConfigEntry<string> PrefabTobeReescaledList;
        public static Dictionary<string, float> scructuralDictionary = new Dictionary<string, float>();

        public void Awake()
        {
            Config.SaveOnConfigSet = true;

            ScalesList = Config.Bind("Server config", "ScalesList", "2, 3, 4",
                            new ConfigDescription("Items will be reescale for each value in this list.", null,
                                     new ConfigurationManagerAttributes { IsAdminOnly = true }));

            PrefabTobeReescaledList = Config.Bind("Server config", "PrefabTobeReescaledList", "bed,guard_stone,wood_roof_45,iron_grate,stone_arch,stone_floor_2x2,stone_pillar,stone_stair,stone_wall_1x1,stone_wall_2x1",
                new ConfigDescription("PrefabTobeReescaledList", null,
                         new ConfigurationManagerAttributes { IsAdminOnly = true }));

            MaxPlaceDistance = Config.Bind("Server config", "MaxPlaceDistance", 50,
                       new ConfigDescription("Probably you will need to increase to work", null,
                                new ConfigurationManagerAttributes { IsAdminOnly = true }));

            CostItemsMultiplier = Config.Bind("Server config", "CostItemsMultiplier", 5,
           new ConfigDescription("The cost for the reescaled items is vanilla value * scaleSize * costItemsMultiplier", null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));

            DropItemsOnDestroy = Config.Bind("Server config", "DropItemsOnDestroy", true,
                       new ConfigDescription("Drop reescaled recipes items when it breaks", null,
                                new ConfigurationManagerAttributes { IsAdminOnly = true }));

            OnlyAdminCanBuild = Config.Bind("Server config", "OnlyAdminCanBuild", false,
           new ConfigDescription("OnlyAdminCanBuild", null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));

            harmony.PatchAll();
            PrefabManager.OnPrefabsRegistered += AddPieceCategories;

            SynchronizationManager.OnConfigurationSynchronized += (obj, attr) =>
            {
                if (attr.InitialSynchronization)
                {
                    AddClonedItems();
                    Jotunn.Logger.LogMessage("Initial Config sync event received");
                }
                else
                {
                    Jotunn.Logger.LogMessage("Config sync event received");
                }
            };
        }

        [HarmonyPatch(typeof(Player), "Awake")]
        public static class Player_Awake_Patch
        {
            private static void Postfix(ref Player __instance)
            {
                __instance.m_maxPlaceDistance = MaxPlaceDistance.Value;
            }
        }

        public static void AddClonedItems()
        {
            var hammer = ObjectDB.instance.m_items.FirstOrDefault(x => x.name == "Hammer");

            if (!hammer)
            {
                Debug.LogError("Piece Reescale - Hammer could not be loaded"); return;
            }

            PieceTable table = hammer.GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces;

            if (prefabToRemove)
            {
                table.m_pieces.Remove(prefabToRemove);
                UnityEngine.Object.Destroy(prefabToRemove);
            }

            foreach (string prefab in PrefabTobeReescaledList.Value.Split(','))
            {
                foreach (string reziseValue in ScalesList.Value.Split(','))
                {
                    float resize = Convert.ToInt64(reziseValue);
                    GameObject customPrefab = PrefabManager.Instance.CreateClonedPrefab(prefab + "x" + resize, prefab);
                    Piece piece = customPrefab.GetComponent<Piece>();
                    piece.m_description += "resized " + resize + "x";

                    Vector3 newScale = piece.transform.transform.localScale;
                    newScale.x *= resize;
                    newScale.y *= resize;
                    newScale.z *= resize;
                    piece.transform.localScale = newScale;

                    foreach (var req in piece.m_resources)
                    {
                        req.m_amount *= (int)resize * CostItemsMultiplier.Value;
                        req.m_recover = DropItemsOnDestroy.Value;
                    }

                    scructuralDictionary.Add(customPrefab.name, resize);

                    PieceManager.Instance.RegisterPieceInPieceTable(customPrefab, "Hammer", "Resized Pieces");

                    if (OnlyAdminCanBuild.Value)
                    {
                        if (!SynchronizationManager.Instance.PlayerIsAdmin)
                        {
                            table.m_pieces.Remove(customPrefab);
                        }
                    }

                }
            }
        }

        public static void AddPieceCategories()
        {
            CustomPiece CP = new CustomPiece("xxxCategoryShit", addZNetView: true, new PieceConfig
            {
                Name = "pieceReescaleShit",
                Description = "$piece_lul_description",
                PieceTable = "Hammer",
                Icon = Sprite.Create(new Texture2D(20, 20), new Rect(0f, 0f, 10, 10), Vector2.zero),
                ExtendStation = "piece_workbench",
                Category = "Resized Pieces"
            });
            prefabToRemove = CP.PiecePrefab;

            if (CP != null)
            {
                PieceManager.Instance.AddPiece(CP);
            }

            PrefabManager.OnPrefabsRegistered -= AddPieceCategories;
        }

        [HarmonyPatch(typeof(WearNTear), "GetMaterialProperties")]
        public static class GetMaterialProperties
        {
            private static bool Prefix(ref WearNTear __instance, out float maxSupport, out float minSupport,
                out float horizontalLoss, out float verticalLoss)
            {
                if (scructuralDictionary.TryGetValue(__instance.gameObject.name, out float prefabMultiplier))
                {
                    switch (__instance.m_materialType)
                    {
                        case WearNTear.MaterialType.Wood:
                            maxSupport = 100f * prefabMultiplier;
                            minSupport = 10f;
                            verticalLoss = 0.125f / prefabMultiplier;
                            horizontalLoss = 0.2f / prefabMultiplier;
                            return false;
                        case WearNTear.MaterialType.Stone:
                            maxSupport = 1000f * prefabMultiplier;
                            minSupport = 100f;
                            verticalLoss = 0.125f / prefabMultiplier;
                            horizontalLoss = 1f / prefabMultiplier;
                            return false;
                        case WearNTear.MaterialType.Iron:
                            maxSupport = 1500f * prefabMultiplier;
                            minSupport = 20f;
                            verticalLoss = 0.07692308f / prefabMultiplier;
                            horizontalLoss = 0.07692308f / prefabMultiplier;
                            return false;
                        case WearNTear.MaterialType.HardWood:
                            maxSupport = 140f * prefabMultiplier;
                            minSupport = 10f;
                            verticalLoss = 0.1f / prefabMultiplier;
                            horizontalLoss = 0.16666667f / prefabMultiplier;
                            return false;
                        default:
                            maxSupport = 0f;
                            minSupport = 0f;
                            verticalLoss = 0f;
                            horizontalLoss = 0f;
                            return false;
                    }
                }
                else
                {
                    maxSupport = 0.0f;
                    minSupport = 0.0f;
                    verticalLoss = 0.0f;
                    horizontalLoss = 0.0f;
                    return true;
                }
            }
        }
    }
}

