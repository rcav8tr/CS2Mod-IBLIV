using Colossal.Collections;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using HarmonyLib;
using System.Reflection;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Scripting;

namespace IBLIV
{
    /// <summary>
    /// Patch for Game.Rendering.ObjectColorSystem.
    /// This system replaces the game's ObjectColorSystem logic for the Building Level infoview.
    /// </summary>
    public partial class ObjectColorSystemPatch : GameSystemBase
    {
        /// <summary>
        /// Job to set the color to default on all objects that have a color.
        /// In this way, any object not set by subsequent jobs is assured to be the default color.
        /// </summary>
        [BurstCompile]
        private partial struct SetColorsJobDefault : IJobChunk
        {
            // Color component type to update.
            public ComponentTypeHandle<Game.Objects.Color> ComponentTypeHandleColor;

            /// <summary>
            /// Job execution.
            /// </summary>
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                // Set color to default for all objects.
                NativeArray<Game.Objects.Color> colors = chunk.GetNativeArray(ref ComponentTypeHandleColor);
                for (int i = 0; i < colors.Length; i++)
                {
                    colors[i] = default;
                }
            }
        }

        /// <summary>
        /// Job to set the color of each zone building.
        /// </summary>
        [BurstCompile]
        private partial struct SetColorsJobZoneBuilding : IJobChunk
        {
            // Color component type to update (NOT ReadOnly).
            public ComponentTypeHandle<Game.Objects.Color                       > ComponentTypeHandleColor;

            // Buffer lookups.
            [ReadOnly] public BufferLookup<Renter                               > BufferLookupRenter;

            // Component lookups.
            [ReadOnly] public ComponentLookup<BuildingData                      > ComponentLookupBuildingData;
            [ReadOnly] public ComponentLookup<BuildingPropertyData              > ComponentLookupBuildingPropertyData;
            [ReadOnly] public ComponentLookup<SpawnableBuildingData             > ComponentLookupSpawnableBuildingData;

            // Component type handles for buildings.
            [ReadOnly] public ComponentTypeHandle<CommercialProperty            > ComponentTypeHandleCommercialProperty;
            [ReadOnly] public ComponentTypeHandle<IndustrialProperty            > ComponentTypeHandleIndustrialProperty;
            [ReadOnly] public ComponentTypeHandle<OfficeProperty                > ComponentTypeHandleOfficeProperty;
            [ReadOnly] public ComponentTypeHandle<ResidentialProperty           > ComponentTypeHandleResidentialProperty;

            // Component type handles for miscellaneous.
            [ReadOnly] public ComponentTypeHandle<Destroyed                     > ComponentTypeHandleDestroyed;
            [ReadOnly] public ComponentTypeHandle<InfomodeActive                > ComponentTypeHandleInfomodeActive;
            [ReadOnly] public ComponentTypeHandle<InfoviewBuildingStatusData    > ComponentTypeHandleInfoviewBuildingStatusData;
            [ReadOnly] public ComponentTypeHandle<PrefabRef                     > ComponentTypeHandlePrefabRef;
            [ReadOnly] public ComponentTypeHandle<Signature                     > ComponentTypeHandleSignature;
            [ReadOnly] public ComponentTypeHandle<UnderConstruction             > ComponentTypeHandleUnderConstruction;

            // Entity type handle.
            [ReadOnly] public EntityTypeHandle EntityTypeHandle;

            // List of active building status data chunks.
            [ReadOnly] public NativeList<ArchetypeChunk> ActiveBuildingStatusDataChunks;

            /// <summary>
            /// Job execution.
            /// </summary>
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                // Get improved building status type.
                ImprovedBuildingStatusType improvedBuildingStatusType = GetImprovedBuildingStatusType(chunk);
                if (improvedBuildingStatusType == ImprovedBuildingStatusType.None)
                {
                    // Ignore all buildings in this chunk.
                    return;
                }

                // Determine if improved building status type is active and get its index.
                GetInfomodeActiveAndIndex(improvedBuildingStatusType, out bool infomodeActive, out int infomodeIndex);
                if (!infomodeActive)
                {
                    // Ignore all buildings in this chunk.
                    return;
                }

                // Set building color based on improved building status type.
                switch (improvedBuildingStatusType)
                {
                    case ImprovedBuildingStatusType.LevelResidential:
                    case ImprovedBuildingStatusType.LevelCommercial:
                    case ImprovedBuildingStatusType.LevelIndustrial:
                    case ImprovedBuildingStatusType.LevelOffice:
                        SetBuildingColorSpawnable(chunk, infomodeIndex);
                        break;

                    case ImprovedBuildingStatusType.SignatureResidential:
                        SetBuildingColorSignatureResidential(chunk, infomodeIndex);
                        break;

                    default:
                        // Everything not handled above is signature buildings for commercial, industrial, and office.
                        SetBuildingColorSignatureCompany(chunk, infomodeIndex);
                        break;
                }

                // Check colors.
                // Logic adapted from ObjectColorSystem.UpdateObjectColorsJob.CheckColors().
                NativeArray<Game.Objects.Color> colors             = chunk.GetNativeArray(ref ComponentTypeHandleColor);
                NativeArray<PrefabRef         > prefabRefs         = chunk.GetNativeArray(ref ComponentTypeHandlePrefabRef);
                NativeArray<Destroyed         > destroyeds         = chunk.GetNativeArray(ref ComponentTypeHandleDestroyed);
                NativeArray<UnderConstruction > underConstructions = chunk.GetNativeArray(ref ComponentTypeHandleUnderConstruction);
                for (int i = 0; i < prefabRefs.Length; i++)
                {
                    // Check if should set SubColor flag on this color.
                    if ((ComponentLookupBuildingData[prefabRefs[i].m_Prefab].m_Flags & Game.Prefabs.BuildingFlags.ColorizeLot) != 0 ||
                        (CollectionUtils.TryGet(destroyeds,         i, out Destroyed destroyed) && destroyed.m_Cleared >= 0f) ||
                        (CollectionUtils.TryGet(underConstructions, i, out UnderConstruction underConstruction) && underConstruction.m_NewPrefab == Entity.Null))
                    {
                        // Set SubColor flag on the color.
                        // Not sure what the SubColor flag does.
                        Game.Objects.Color color = colors[i];
                        color.m_SubColor = true;
                        colors[i] = color;
                    }
                }
            }

            /// <summary>
            /// Get the improved building status type for the buildings in the chunk.
            /// </summary>
            private ImprovedBuildingStatusType GetImprovedBuildingStatusType(ArchetypeChunk buildingChunk)
            {
                // Check if buildings in chunk are signature.
                // The ObjectColorSystem.UpdateObjectColorsJob uses the presence of the UniqueObject component to identify signature buildings.
                // Instead, this mod uses the presence of the Signature component in case the signature buildings are ever not unique (e.g. a mod).
                bool isSignature = buildingChunk.Has(ref ComponentTypeHandleSignature);

                // Determine improved building status type.
                // Mixed Housing (i.e. has both residential and commercial) is treated as residential because residential is checked first.
                if (buildingChunk.Has(ref ComponentTypeHandleResidentialProperty))
                {
                    return isSignature ? ImprovedBuildingStatusType.SignatureResidential : ImprovedBuildingStatusType.LevelResidential;
                }
                if (buildingChunk.Has(ref ComponentTypeHandleCommercialProperty))
                {
                    return isSignature ? ImprovedBuildingStatusType.SignatureCommercial : ImprovedBuildingStatusType.LevelCommercial;
                }
                if (buildingChunk.Has(ref ComponentTypeHandleIndustrialProperty) && !buildingChunk.Has(ref ComponentTypeHandleOfficeProperty))
                {
                    return isSignature ? ImprovedBuildingStatusType.SignatureIndustrial : ImprovedBuildingStatusType.LevelIndustrial;
                }
                if (buildingChunk.Has(ref ComponentTypeHandleOfficeProperty))
                {
                    return isSignature ? ImprovedBuildingStatusType.SignatureOffice : ImprovedBuildingStatusType.LevelOffice;
                }

                // None of the above.
                return ImprovedBuildingStatusType.None;
            }

            /// <summary>
            /// Determine if the improved building status type is active and get its index.
            /// </summary>
            private void GetInfomodeActiveAndIndex(ImprovedBuildingStatusType improvedBuildingStatusType, out bool infomodeActive, out int infomodeIndex)
            {
                // Set defaults.
                infomodeActive = false;
                infomodeIndex = 0;

                // Do each active building status data chunk.
                for (int i = 0; i < ActiveBuildingStatusDataChunks.Length; i++)
                {
                    // Do each active building status data in the chunk.
                    ArchetypeChunk activeBuildingStatusDataChunk = ActiveBuildingStatusDataChunks[i];
                    NativeArray<InfoviewBuildingStatusData> activeBuildingStatusDatas = activeBuildingStatusDataChunk.GetNativeArray(ref ComponentTypeHandleInfoviewBuildingStatusData);
                    for (int j = 0; j < activeBuildingStatusDatas.Length; j++)
                    {
                        // Check if the improved building status type is active.
                        InfoviewBuildingStatusData activeBuildingStatusData = activeBuildingStatusDatas[j];
                        ImprovedBuildingStatusType activeBuildingStatusType = (ImprovedBuildingStatusType)activeBuildingStatusData.m_Type;
                        if (improvedBuildingStatusType == activeBuildingStatusType)
                        {
                            // Improved building status type (i.e. infomode) is active.
                            infomodeActive = true;

                            // Get index from corresponding active infomode.
                            NativeArray<InfomodeActive> activeInfomodes = activeBuildingStatusDataChunk.GetNativeArray(ref ComponentTypeHandleInfomodeActive);
                            infomodeIndex = activeInfomodes[j].m_Index;

                            // Found it, stop checking.
                            return;
                        }
                    }
                }
            }

            /// <summary>
            /// Set building color for spawnable buildings.
            /// </summary>
            private void SetBuildingColorSpawnable(ArchetypeChunk chunk, int infomodeIndex)
            {
                // Get arrays needed to set building color.
                NativeArray<Game.Objects.Color> colors     = chunk.GetNativeArray(ref ComponentTypeHandleColor);
                NativeArray<Entity            > entities   = chunk.GetNativeArray(EntityTypeHandle);
                NativeArray<PrefabRef         > prefabRefs = chunk.GetNativeArray(ref ComponentTypeHandlePrefabRef);

                // Do each building.
                for (int i = 0; i < entities.Length; i++)
                {
                    // Prefab must have spawnable building data, which holds the level.
                    if (ComponentLookupSpawnableBuildingData.TryGetComponent(prefabRefs[i].m_Prefab, out SpawnableBuildingData spawnableBuildingData))
                    {
                        // Compute the color percent based on building level.
                        int colorPercent = 0;
                        switch (spawnableBuildingData.m_Level)
                        {
                            case 1: colorPercent = 0; break;
                            case 2: colorPercent = Mathf.RoundToInt(50f * InfoviewsUISystemPatch.Level2And4InterpolationFactor); break;
                            case 3: colorPercent = 50; break;
                            case 4: colorPercent = Mathf.RoundToInt(50f * InfoviewsUISystemPatch.Level2And4InterpolationFactor) + 50; break;
                            case 5: colorPercent = 100; break;
                        }

                        // Set the building color.
                        colors[i] = new Game.Objects.Color((byte)infomodeIndex, (byte)math.clamp(Mathf.RoundToInt(255f * colorPercent / 100f), 0, 255));
                    }
                    else
                    {
                        // Set building color to 0.
                        // This should never happen.
                        colors[i] = new Game.Objects.Color((byte)infomodeIndex, 0);
                    }
                }
            }

            /// <summary>
            /// Set building color for signature residential buildings.
            /// </summary>
            private void SetBuildingColorSignatureResidential(ArchetypeChunk chunk, int infomodeIndex)
            {
                // Get arrays needed to set building color.
                NativeArray<Game.Objects.Color> colors     = chunk.GetNativeArray(ref ComponentTypeHandleColor);
                NativeArray<Entity            > entities   = chunk.GetNativeArray(EntityTypeHandle);
                NativeArray<PrefabRef         > prefabRefs = chunk.GetNativeArray(ref ComponentTypeHandlePrefabRef);

                // Do each building.
                for (int i = 0; i < entities.Length; i++)
                {
                    // Entity must have a renters buffer and prefab must have residential properties.
                    if (BufferLookupRenter.TryGetBuffer(entities[i], out DynamicBuffer<Renter> bufferRenters) &&
                        bufferRenters.IsCreated &&
                        ComponentLookupBuildingPropertyData.TryGetComponent(prefabRefs[i].m_Prefab, out BuildingPropertyData buildingPropertyData) &&
                        buildingPropertyData.m_ResidentialProperties > 0)
                    {
                        // Color ratio is number of renters compared to number of properties.
                        float colorRatio = (float)bufferRenters.Length / buildingPropertyData.m_ResidentialProperties;

                        // Set the building color.
                        colors[i] = new Game.Objects.Color((byte)infomodeIndex, (byte)math.clamp(Mathf.RoundToInt(255f * colorRatio), 0, 255));
                    }
                    else
                    {
                        // Set building color to 0.
                        // This should never happen.
                        colors[i] = new Game.Objects.Color((byte)infomodeIndex, 0);
                    }
                }
            }

            /// <summary>
            /// Set building color for signature company buildings.
            /// </summary>
            private void SetBuildingColorSignatureCompany(ArchetypeChunk chunk, int infomodeIndex)
            {
                // Get arrays needed to set building color.
                NativeArray<Game.Objects.Color> colors     = chunk.GetNativeArray(ref ComponentTypeHandleColor);
                NativeArray<Entity            > entities   = chunk.GetNativeArray(EntityTypeHandle);

                // Do each building.
                for (int i = 0; i < entities.Length; i++)
                {
                    // Entity must have a renters buffer.
                    if (BufferLookupRenter.TryGetBuffer(entities[i], out DynamicBuffer<Renter> bufferRenters) &&
                        bufferRenters.IsCreated)
                    {
                        // Set the building color value to either 255 or 0 based on whether or not there is a renter (i.e. occupied or vacant).
                        colors[i] = new Game.Objects.Color((byte)infomodeIndex, (byte)(bufferRenters.Length > 0 ? 255 : 0));
                    }
                    else
                    {
                        // Set building color to 0.
                        // This should never happen.
                        colors[i] = new Game.Objects.Color((byte)infomodeIndex, 0);
                    }
                }
            }
        }


        /// <summary>
        /// Job to set the color of a temp object to the color of its original.
        /// Temp objects are when cursor is hovered over an object.
        /// Logic copied exactly from Game.Rendering.ObjectColorSystem.UpdateTempObjectColorsJob except variables are renamed to improve readability.
        /// </summary>
        [BurstCompile]
        private struct SetColorsJobTempObject : IJobChunk
        {
            // Color component lookup to update.
            [NativeDisableParallelForRestriction] public ComponentLookup<Game.Objects.Color> ComponentLookupColor;

            // Component type handles.
            [ReadOnly] public ComponentTypeHandle<Temp> ComponentTypeHandleTemp;

            // Entity type handle.
            [ReadOnly] public EntityTypeHandle EntityTypeHandle;

            /// <summary>
            /// Job execution.
            /// </summary>
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                // Set color of object to color of its original.
                NativeArray<Entity> entities = chunk.GetNativeArray(EntityTypeHandle);
                NativeArray<Temp> temps = chunk.GetNativeArray(ref ComponentTypeHandleTemp);
                for (int i = 0; i < temps.Length; i++)
                {
                    if (ComponentLookupColor.TryGetComponent(temps[i].m_Original, out Game.Objects.Color originalColor))
                    {
                        ComponentLookupColor[entities[i]] = originalColor;
                    }
                }
            }
        }



        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////



        // The game's instance of this system.
        private static ObjectColorSystemPatch  _buildingColorSystem;

        // Other systems.
        private ToolSystem _toolSystem;

        // Entity queries.
        private EntityQuery _queryDefault;
        private EntityQuery _queryZoneBuilding;
        private EntityQuery _queryTempObject;
        private EntityQuery _queryActiveBuildingStatusData;

        // The component lookup and type handle for color.
        // These are used to set building color.
        private ComponentLookup    <Game.Objects.Color          > _componentLookupColor;
        private ComponentTypeHandle<Game.Objects.Color          > _componentTypeHandleColor;

        // Buffer lookups.
        private BufferLookup<Renter                             > _bufferLookupRenter;

        // Component lookups.
        private ComponentLookup<BuildingData                    > _componentLookupBuildingData;
        private ComponentLookup<BuildingPropertyData            > _componentLookupBuildingPropertyData;
        private ComponentLookup<SpawnableBuildingData           > _componentLookupSpawnableBuildingData;

        // Component type handles for buildings.
        // The presence of these on a building defines the building type.
        private ComponentTypeHandle<CommercialProperty          > _componentTypeHandleCommercialProperty;
        private ComponentTypeHandle<IndustrialProperty          > _componentTypeHandleIndustrialProperty;
        private ComponentTypeHandle<OfficeProperty              > _componentTypeHandleOfficeProperty;
        private ComponentTypeHandle<ResidentialProperty         > _componentTypeHandleResidentialProperty;

        // Component type handles for miscellaneous.
        private ComponentTypeHandle<Destroyed                   > _componentTypeHandleDestroyed;
        private ComponentTypeHandle<InfomodeActive              > _componentTypeHandleInfomodeActive;
        private ComponentTypeHandle<InfoviewBuildingStatusData  > _componentTypeHandleInfoviewBuildingStatusData;
        private ComponentTypeHandle<PrefabRef                   > _componentTypeHandlePrefabRef;
        private ComponentTypeHandle<Signature                   > _componentTypeHandleSignature;
        private ComponentTypeHandle<Temp                        > _componentTypeHandleTemp;
        private ComponentTypeHandle<UnderConstruction           > _componentTypeHandleUnderConstruction;

        // Entity type handle.
        private EntityTypeHandle _entityTypeHandle;

        /// <summary>
        /// Gets called right before OnCreate.
        /// </summary>
        protected override void OnCreateForCompiler()
        {
            base.OnCreateForCompiler();
            LogUtil.Info($"{nameof(ObjectColorSystemPatch)}.{nameof(OnCreateForCompiler)}");

            // Assign components for color.
            // These are the only ones that are read/write.
            _componentLookupColor                           = CheckedStateRef.GetComponentLookup    <Game.Objects.Color         >();
            _componentTypeHandleColor                       = CheckedStateRef.GetComponentTypeHandle<Game.Objects.Color         >();

            // Assign buffer lookups.
            _bufferLookupRenter                             = CheckedStateRef.GetBufferLookup<Renter                            >(true);

            // Assign component lookups.
            _componentLookupBuildingData                    = CheckedStateRef.GetComponentLookup<BuildingData                   >(true);
            _componentLookupBuildingPropertyData            = CheckedStateRef.GetComponentLookup<BuildingPropertyData           >(true);
            _componentLookupSpawnableBuildingData           = CheckedStateRef.GetComponentLookup<SpawnableBuildingData          >(true);

            // Assign component type handles for buildings.
            _componentTypeHandleCommercialProperty          = CheckedStateRef.GetComponentTypeHandle<CommercialProperty         >(true);
            _componentTypeHandleIndustrialProperty          = CheckedStateRef.GetComponentTypeHandle<IndustrialProperty         >(true);
            _componentTypeHandleOfficeProperty              = CheckedStateRef.GetComponentTypeHandle<OfficeProperty             >(true);
            _componentTypeHandleResidentialProperty         = CheckedStateRef.GetComponentTypeHandle<ResidentialProperty        >(true);

            // Assign component type handles for miscellaneous.
            _componentTypeHandleDestroyed                   = CheckedStateRef.GetComponentTypeHandle<Destroyed                  >(true);
            _componentTypeHandleInfomodeActive              = CheckedStateRef.GetComponentTypeHandle<InfomodeActive             >(true);
            _componentTypeHandleInfoviewBuildingStatusData  = CheckedStateRef.GetComponentTypeHandle<InfoviewBuildingStatusData >(true);
            _componentTypeHandlePrefabRef                   = CheckedStateRef.GetComponentTypeHandle<PrefabRef                  >(true);
            _componentTypeHandleSignature                   = CheckedStateRef.GetComponentTypeHandle<Signature                  >(true);
            _componentTypeHandleTemp                        = CheckedStateRef.GetComponentTypeHandle<Temp                       >(true);
            _componentTypeHandleUnderConstruction           = CheckedStateRef.GetComponentTypeHandle<UnderConstruction          >(true);

            // Assign entity type handle.
            _entityTypeHandle                               = CheckedStateRef.GetEntityTypeHandle();
        }

        /// <summary>
        /// Initialize this system.
        /// </summary>
        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            LogUtil.Info($"{nameof(ObjectColorSystemPatch)}.{nameof(OnCreate)}");

            // Save the game's instance of this system.
            _buildingColorSystem = this;

            // Get other systems.
            _toolSystem = base.World.GetOrCreateSystemManaged<ToolSystem>();

            // Query to get default objects (i.e. every object that has a color).
		    _queryDefault = GetEntityQuery
            (
                new EntityQueryDesc
		        {
			        All = new ComponentType[]
			        {
				        ComponentType.ReadOnly <Game.Objects.Object>(),
				        ComponentType.ReadWrite<Game.Objects.Color>(),
			        },
			        None = new ComponentType[]
			        {
				        ComponentType.ReadOnly<Hidden>(),
				        ComponentType.ReadOnly<Deleted>(),
			        }
		        }
            );

            // Query to get zone buildings.
            // Adapted from Game.Rendering.ObjectColorSystem.
		    _queryZoneBuilding = GetEntityQuery
            (
                new EntityQueryDesc
		        {
			        All = new ComponentType[]
			        {
				        ComponentType.ReadOnly <Game.Objects.Object>(),
				        ComponentType.ReadWrite<Game.Objects.Color>(),
                        ComponentType.ReadOnly <Building>(),
			        },
                    Any = new ComponentType[]
                    {
                        ComponentType.ReadOnly<ResidentialProperty>(),
                        ComponentType.ReadOnly<CommercialProperty>(),
                        ComponentType.ReadOnly<IndustrialProperty>(),
                        ComponentType.ReadOnly<OfficeProperty>(),
                    },
			        None = new ComponentType[]
			        {
				        ComponentType.ReadOnly<Hidden>(),       // Exclude hidden    buildings.
                        ComponentType.ReadOnly<Abandoned>(),    // Exclude abandoned buildings. 
                        ComponentType.ReadOnly<Condemned>(),    // Exclude condemned buildings.
				        ComponentType.ReadOnly<Deleted>(),      // Exclude deleted   buildings.
                        ComponentType.ReadOnly<Destroyed>(),    // Exclude destroyed buildings.
				        ComponentType.ReadOnly<Owner>(),        // Exclude subbuildings. Zone buildings never have subbuildings.
                        ComponentType.ReadOnly<Attachment>(),   // Exclude attachments.  Zone buildings never have attachments.
				        ComponentType.ReadOnly<Temp>(),         // Exclude temp (see temp objects query below).
			        }
		        }
            );

            // Query to get Temp objects.
            // Temp objects are when cursor is hovered over an object.
            // The original object gets hidden and a temp object is placed over the original.
            // Copied exactly from Game.Rendering.ObjectColorSystem.
            _queryTempObject = GetEntityQuery
            (
                new EntityQueryDesc
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly <Game.Objects.Object>(),
                        ComponentType.ReadWrite<Game.Objects.Color>(),
                        ComponentType.ReadOnly <Temp>(),
                    },
                    None = new ComponentType[]
                    {
                        ComponentType.ReadOnly<Hidden>(),
                        ComponentType.ReadOnly<Deleted>(),
                    }
                }
            );

            // Query to get active building status datas.
            // All infomodes for this mod are BuildingStatusInfomodePrefab which generates InfoviewBuildingStatusData.
            // So there is no need to check for other datas.
            _queryActiveBuildingStatusData = GetEntityQuery
            (
                new EntityQueryDesc
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<InfoviewBuildingStatusData>(),
                        ComponentType.ReadOnly<InfomodeActive>(),
                    }
                }
            );

            // Use Harmony to patch ObjectColorSystem.OnUpdate() with BuildingColorSystem.OnUpdatePrefix().
            // When the building level infoview is displayed, it is not necessary to execute ObjectColorSystem.OnUpdate.
            // By using a Harmony prefix, this system can prevent the execution of ObjectColorSystem.OnUpdate().
            // Note that ObjectColorSystem.OnUpdate() can be patched, but the jobs in ObjectColorSystem cannot be patched because they are burst compiled.
            MethodInfo originalMethod = typeof(ObjectColorSystem).GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            if (originalMethod == null)
            {
                LogUtil.Error($"Unable to find original method {nameof(ObjectColorSystem)}.OnUpdate.");
                return;
            }
            MethodInfo prefixMethod = typeof(ObjectColorSystemPatch).GetMethod(nameof(OnUpdatePrefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (prefixMethod == null)
            {
                LogUtil.Error($"Unable to find patch prefix method {nameof(ObjectColorSystemPatch)}.{nameof(OnUpdatePrefix)}.");
                return;
            }
            new Harmony(Mod.HarmonyID).Patch(originalMethod, new HarmonyMethod(prefixMethod), null);
        }

        /// <summary>
        /// Perform updates.
        /// </summary>
        protected override void OnUpdate()
        {
            // Nothing to do here, but implementation is required.
            // All work is performed in OnUpdateImpl() which is called from OnUpdatePrefix().
        }

        /// <summary>
        /// Prefix patch method for ObjectColorSystem.OnUpdate().
        /// </summary>
        private static bool OnUpdatePrefix()
        {
            // On the game's instance of this system, call the implementation of OnUpdate.
            return _buildingColorSystem.OnUpdateImpl();
        }

        /// <summary>
        /// Implementation method that potentially replaces the call to ObjectColorSystem.OnUpdate().
        /// </summary>
        private bool OnUpdateImpl()
        {
            // If no active infoview, then execute original game logic.
            if (_toolSystem.activeInfoview == null)
            {
                return true;
            }

            // If active infoview is not building level, then execute original game logic.
            if (_toolSystem.activeInfoview.name != "Level")
            {
                return true;
            }

            // Active infoview is building level.
            // Run jobs to set building colors.


            // Create a job to set default colors.
            _componentTypeHandleColor.Update(ref CheckedStateRef);
            SetColorsJobDefault setColorsJobDefault = new SetColorsJobDefault()
            {
                ComponentTypeHandleColor = _componentTypeHandleColor,
            };


            // Define a job to get active building status datas.
            NativeList<ArchetypeChunk> activeBuildingStatusDataChunks =
                _queryActiveBuildingStatusData.ToArchetypeChunkListAsync(Allocator.TempJob, out JobHandle activeBuildingStatusDataJobHandle);

            // Update buffers and components for zone building colors job.
            _componentTypeHandleColor                       .Update(ref CheckedStateRef);

            _bufferLookupRenter                             .Update(ref CheckedStateRef);

            _componentLookupBuildingData                    .Update(ref CheckedStateRef);
            _componentLookupBuildingPropertyData            .Update(ref CheckedStateRef);
            _componentLookupSpawnableBuildingData           .Update(ref CheckedStateRef);

            _componentTypeHandleCommercialProperty          .Update(ref CheckedStateRef);
            _componentTypeHandleIndustrialProperty          .Update(ref CheckedStateRef);
            _componentTypeHandleOfficeProperty              .Update(ref CheckedStateRef);
            _componentTypeHandleResidentialProperty         .Update(ref CheckedStateRef);

            _componentTypeHandleDestroyed                   .Update(ref CheckedStateRef);
            _componentTypeHandleInfomodeActive              .Update(ref CheckedStateRef);
            _componentTypeHandleInfoviewBuildingStatusData  .Update(ref CheckedStateRef);
            _componentTypeHandlePrefabRef                   .Update(ref CheckedStateRef);
            _componentTypeHandleSignature                   .Update(ref CheckedStateRef);
            _componentTypeHandleUnderConstruction           .Update(ref CheckedStateRef);

            _entityTypeHandle                               .Update(ref CheckedStateRef);

            // Create a job to set zone building colors.
            SetColorsJobZoneBuilding setColorsJobZoneBuilding = new SetColorsJobZoneBuilding()
            {
                ComponentTypeHandleColor                        = _componentTypeHandleColor,

                BufferLookupRenter                              = _bufferLookupRenter,
                
                ComponentLookupBuildingData                     = _componentLookupBuildingData,
                ComponentLookupBuildingPropertyData             = _componentLookupBuildingPropertyData,
                ComponentLookupSpawnableBuildingData            = _componentLookupSpawnableBuildingData,
                
                ComponentTypeHandleCommercialProperty           = _componentTypeHandleCommercialProperty,
                ComponentTypeHandleIndustrialProperty           = _componentTypeHandleIndustrialProperty,
                ComponentTypeHandleOfficeProperty               = _componentTypeHandleOfficeProperty,
                ComponentTypeHandleResidentialProperty          = _componentTypeHandleResidentialProperty,

                ComponentTypeHandleDestroyed                    = _componentTypeHandleDestroyed,
                ComponentTypeHandleInfomodeActive               = _componentTypeHandleInfomodeActive,
                ComponentTypeHandleInfoviewBuildingStatusData   = _componentTypeHandleInfoviewBuildingStatusData,
                ComponentTypeHandlePrefabRef                    = _componentTypeHandlePrefabRef,
                ComponentTypeHandleSignature                    = _componentTypeHandleSignature,
                ComponentTypeHandleUnderConstruction            = _componentTypeHandleUnderConstruction,
                
                EntityTypeHandle                                = _entityTypeHandle,

                ActiveBuildingStatusDataChunks                  = activeBuildingStatusDataChunks,
            };


            // Create a job to set temp object colors.
            _componentLookupColor       .Update(ref CheckedStateRef);
            _componentTypeHandleTemp    .Update(ref CheckedStateRef);
            _entityTypeHandle           .Update(ref CheckedStateRef);
            SetColorsJobTempObject setColorsJobTempObject = new SetColorsJobTempObject()
            {
                ComponentLookupColor    = _componentLookupColor,
                ComponentTypeHandleTemp = _componentTypeHandleTemp,
                EntityTypeHandle        = _entityTypeHandle,
            };


            // Schedule the jobs with dependencies so the jobs run in order.
            // Schedule each job to execute in parallel (i.e. job uses multiple threads, if available).
            // Parallel threads execute much faster than a single thread.
            JobHandle jobHandleDefault      = JobChunkExtensions.ScheduleParallel(setColorsJobDefault,      _queryDefault,      base.Dependency);
            JobHandle jobHandleZoneBuilding = JobChunkExtensions.ScheduleParallel(setColorsJobZoneBuilding, _queryZoneBuilding, JobHandle.CombineDependencies(jobHandleDefault, activeBuildingStatusDataJobHandle));
            JobHandle jobHandleTempObject   = JobChunkExtensions.ScheduleParallel(setColorsJobTempObject,   _queryTempObject,   jobHandleZoneBuilding);

            // Prevent these jobs from running again until last job is complete.
            base.Dependency = jobHandleTempObject;

            // Wait for the zone building job to complete.
            // This seems to help prevent screen flicker.
            jobHandleZoneBuilding.Complete();
            
            // Dispose of native collections no longer needed once the zone building job is complete.
            activeBuildingStatusDataChunks.Dispose();

            // Note that the temp object job could still be executing at this point, which is okay.

            // This system set building colors for the building level infoview.
            // Do not execute the original game logic.
            return false;
        }
    }
}
