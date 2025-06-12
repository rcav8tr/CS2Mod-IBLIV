using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Prefabs;
using System;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace IBLIV
{
    /// <summary>
    /// System to update the building level infoview.
    /// </summary>
    public partial class BuildingLevelInfoviewSystem : GameSystemBase
    {
        // Initialization flag.
        private bool _initialized = false;

        /// <summary>
        /// Called when a game is about to be loaded.
        /// </summary>
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);

            // Initialization is performed in OnGamePreload instead of OnCreate because
            // occasionally the signature infomode prefabs were not available when this system was created.
            // It is not known why this happened only occasionally.
            
            // Initialization is performed in OnGamePreload instead of OnGameLoadingComplete because
            // the custom infoview icon does not get displayed if performed in OnGameLoadingComplete.

            // Skip if already initialized.
            if (_initialized)
            {
                return;
            }

            // Skip if not loading a game (i.e. is editor or main menu).
            if (mode != GameMode.Game)
            {
                return;
            }

            Mod.log.Info($"{nameof(BuildingLevelInfoviewSystem)}.{nameof(OnGamePreload)} initialize");

            try
            {
                // The game's infoviews must be created first.
                // That will be the normal case by the time a game is about to be loaded.
                // But perform the check anyway just in case.
                InfoviewInitializeSystem infoviewInitializeSystem = World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<InfoviewInitializeSystem>();
                if (infoviewInitializeSystem == null || infoviewInitializeSystem.infoviews.Count() == 0)
                {
                    Mod.log.Error("The game's infoviews must be created.");
                    return;
                }

                // Get the game's built-in building level infoview prefab.
                InfoviewPrefab buildingLevelInfoviewPrafab = null;
                foreach (InfoviewPrefab infoviewPrefab in infoviewInitializeSystem.infoviews)
                {
                    if (infoviewPrefab.builtin && infoviewPrefab.name == "Level")
                    { 
                        buildingLevelInfoviewPrafab = infoviewPrefab;
                        break;
                    }
                }
                if (buildingLevelInfoviewPrafab == null)
                { 
                    Mod.log.Error("Unable to find the Building Level infoview prefab.");
                    return;
                }

                // For signature buildings, the game's existing infomode prefabs are used instead of creating new infomode prefabs.
                // Get the game's signature infomode prefab for each zone type.
                PrefabSystem prefabSystem = World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<PrefabSystem>();
                BuildingStatusInfomodePrefab infomodePrefabSignatureResidential = null;
                BuildingStatusInfomodePrefab infomodePrefabSignatureCommercial  = null;
                BuildingStatusInfomodePrefab infomodePrefabSignatureIndustrial  = null;
                BuildingStatusInfomodePrefab infomodePrefabSignatureOffice      = null;
                ComponentTypeHandle<PrefabData> componentTypeHandlePrefabData = CheckedStateRef.GetComponentTypeHandle<PrefabData>(isReadOnly: true);
                componentTypeHandlePrefabData.Update(ref CheckedStateRef);
                EntityQuery buildingStatusDataQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<InfoviewBuildingStatusData>());
                NativeArray<ArchetypeChunk> buildingStatusDataChunks = buildingStatusDataQuery.ToArchetypeChunkArray(Allocator.TempJob);
                try
                {
                    // Do each chunk.
                    for (int i = 0; i < buildingStatusDataChunks.Length; i++)
                    {
                        // Do each prefab data in the chunk.
                        NativeArray<PrefabData> prefabDatas = buildingStatusDataChunks[i].GetNativeArray(ref componentTypeHandlePrefabData);
                        for (int j = 0; j < prefabDatas.Length; j++)
                        {
                            // Use the prefab data to get the BuildingStatusInfomodePrefab.
                            BuildingStatusInfomodePrefab buildingStatusInfomodePrefab = prefabSystem.GetPrefab<BuildingStatusInfomodePrefab>(prefabDatas[j]);
                            if (buildingStatusInfomodePrefab != null)
                            {
                                // Check if this is one of the signature infomode prefabs.
                                switch (buildingStatusInfomodePrefab.m_Type)
                                {
                                    case BuildingStatusType.SignatureResidential: infomodePrefabSignatureResidential = buildingStatusInfomodePrefab; break;
                                    case BuildingStatusType.SignatureCommercial:  infomodePrefabSignatureCommercial  = buildingStatusInfomodePrefab; break;
                                    case BuildingStatusType.SignatureIndustrial:  infomodePrefabSignatureIndustrial  = buildingStatusInfomodePrefab; break;
                                    case BuildingStatusType.SignatureOffice:      infomodePrefabSignatureOffice      = buildingStatusInfomodePrefab; break;
                                }
                            }
                        }
                    }
                }
                finally
                {
                    buildingStatusDataChunks.Dispose();
                }

                // Verify signature infomode prefabs.
                if (infomodePrefabSignatureResidential == null) { Mod.log.Error("Unable to find infomode prefab for Signature Residential."); return; }
                if (infomodePrefabSignatureCommercial  == null) { Mod.log.Error("Unable to find infomode prefab for Signature Commercial." ); return; }
                if (infomodePrefabSignatureIndustrial  == null) { Mod.log.Error("Unable to find infomode prefab for Signature Industrial." ); return; }
                if (infomodePrefabSignatureOffice      == null) { Mod.log.Error("Unable to find infomode prefab for Signature Office."     ); return; }

                // Create a new building status infomode prefab for the building level of each zone type.
                BuildingStatusInfomodePrefab infomodePrefabLevelResidential = CreateInfomodePrefab(ImprovedBuildingStatusType.LevelResidential);
                BuildingStatusInfomodePrefab infomodePrefabLevelCommercial  = CreateInfomodePrefab(ImprovedBuildingStatusType.LevelCommercial);
                BuildingStatusInfomodePrefab infomodePrefabLevelIndustrial  = CreateInfomodePrefab(ImprovedBuildingStatusType.LevelIndustrial);
                BuildingStatusInfomodePrefab infomodePrefabLevelOffice      = CreateInfomodePrefab(ImprovedBuildingStatusType.LevelOffice);

                // Add the building status infomode prefabs to the prefab system.
                prefabSystem.AddPrefab(infomodePrefabLevelResidential);
                prefabSystem.AddPrefab(infomodePrefabLevelCommercial);
                prefabSystem.AddPrefab(infomodePrefabLevelIndustrial);
                prefabSystem.AddPrefab(infomodePrefabLevelOffice);

                // Remove the existing infomodes from the building level infoview.
			    DynamicBuffer<InfoviewMode> infomodesBuildingLevel = prefabSystem.GetBuffer<InfoviewMode>(buildingLevelInfoviewPrafab, isReadOnly: false);
                infomodesBuildingLevel.Clear();

                // Add the infomode prefabs to the building level infoview.
                int priority = 1;
                infomodesBuildingLevel.Add(new InfoviewMode(prefabSystem.GetEntity(infomodePrefabLevelResidential    ), priority++, false, false));
                infomodesBuildingLevel.Add(new InfoviewMode(prefabSystem.GetEntity(infomodePrefabLevelCommercial     ), priority++, false, false));
                infomodesBuildingLevel.Add(new InfoviewMode(prefabSystem.GetEntity(infomodePrefabLevelIndustrial     ), priority++, false, false));
                infomodesBuildingLevel.Add(new InfoviewMode(prefabSystem.GetEntity(infomodePrefabLevelOffice         ), priority++, false, false));
                infomodesBuildingLevel.Add(new InfoviewMode(prefabSystem.GetEntity(infomodePrefabSignatureResidential), priority++, false, false));
                infomodesBuildingLevel.Add(new InfoviewMode(prefabSystem.GetEntity(infomodePrefabSignatureCommercial ), priority++, false, false));
                infomodesBuildingLevel.Add(new InfoviewMode(prefabSystem.GetEntity(infomodePrefabSignatureIndustrial ), priority++, false, false));
                infomodesBuildingLevel.Add(new InfoviewMode(prefabSystem.GetEntity(infomodePrefabSignatureOffice     ), priority++, false, false));

                // Set a new custom icon on building level infoview.
                buildingLevelInfoviewPrafab.m_IconPath = $"coui://{Mod.ImagesURI}/ImprovedBuildingLevel.svg";

                // Initialized.
                _initialized = true;
            }
            catch (Exception ex)
            {
                Mod.log.Error(ex);
            }
        }

        /// <summary>
        /// Create a building status infomode prefab.
        /// </summary>
        private BuildingStatusInfomodePrefab CreateInfomodePrefab(ImprovedBuildingStatusType improvedBuildingStatusType)
        {
            // Create a new building status infomode prefab.
            // All infomodes in this mod are of type BuildingStatusInfomodePrefab.
            // BuildingStatusInfomodePrefab results in InfoviewBuildingStatusData being generated.
            BuildingStatusInfomodePrefab infomodePrefab = ScriptableObject.CreateInstance<BuildingStatusInfomodePrefab>();

            // Set infomode prefab properties.
            infomodePrefab.m_Type = (BuildingStatusType)improvedBuildingStatusType;
            infomodePrefab.name = ModAssemblyInfo.Name + improvedBuildingStatusType.ToString();
            infomodePrefab.m_Range = new Bounds1(0f, 255f);
            infomodePrefab.m_LegendType = GradientLegendType.Gradient;

            // Set building level low and high colors.
            // See the attached BuildingLevelColors.ods file for details on how these colors and multipliers were determined.
            switch(improvedBuildingStatusType)
            {
                case ImprovedBuildingStatusType.LevelResidential:
                    infomodePrefab.m_Low    = GetColor(133, 250,  20, 1.00f);
                    infomodePrefab.m_High   = GetColor( 32,  85,  12, 0.85f);
                    break;
                
                case ImprovedBuildingStatusType.LevelCommercial:
                    infomodePrefab.m_Low    = GetColor( 67, 211, 254, 1.00f);
                    infomodePrefab.m_High   = GetColor( 10,  66,  84, 0.90f);
                    break;
                
                case ImprovedBuildingStatusType.LevelIndustrial:
                    infomodePrefab.m_Low    = GetColor(251, 205,  26, 0.95f);
                    infomodePrefab.m_High   = GetColor( 85,  69,  13, 0.77f);
                    break;
                
                case ImprovedBuildingStatusType.LevelOffice:
                    infomodePrefab.m_Low    = GetColor(120,  65, 254, 1.20f);
                    infomodePrefab.m_High   = GetColor( 32,  11,  84, 1.13f);
                    break;
            }
            
            // Compute medium color.
            // See the attached BuildingLevelColors.ods file for details on how this interpolation factor was determined.
            infomodePrefab.m_Medium = Color.Lerp(infomodePrefab.m_Low, infomodePrefab.m_High, 0.64f);

            // Return the infomode prefab.
            return infomodePrefab;
        }

        /// <summary>
        /// Get a color based on bytes, not floats, with a multiplier applied.
        /// </summary>
        private Color GetColor(byte r, byte g, byte b, float multiplier)
        {
            return new Color(Mathf.Clamp01(r * multiplier / 255f), Mathf.Clamp01(g * multiplier / 255f), Mathf.Clamp01(b * multiplier / 255f), 1f);
        }

        /// <summary>
        /// Perform updates.
        /// </summary>
        protected override void OnUpdate()
        {
            // Nothing to do here, but implementation is required.
            // All work is performed in OnCreate().
        }
    }
}
