using Colossal.UI.Binding;
using Game;
using Game.Prefabs;
using Game.UI.InGame;
using Game.UI.Localization;
using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace IBLIV
{
    /// <summary>
    /// Patch for Game.UI.InGame.InfoviewsUISystem.BindInfomodeGradientLegend().
    /// </summary>
    public partial class InfoviewsUISystemPatch : GameSystemBase
    {
        // The game's instance of this system.
        private static InfoviewsUISystemPatch  _infoviewsUISystemPatch;

        // Interpolation factor for computing colors and percents for building levels 2 and 4.
        // See the attached BuildingLevelColors.ods file for details on how this interpolation factor was determined.
        public const float Level2And4InterpolationFactor = 0.57f;

        /// <summary>
        /// Initialize this system.
        /// </summary>
        protected override void OnCreate()
        {
            base.OnCreate();
            LogUtil.Info($"{nameof(InfoviewsUISystemPatch)}.{nameof(OnCreate)}");

            // Save the game's instance of this system.
            _infoviewsUISystemPatch = this;

            // Use Harmony to patch InfoviewsUISystem.BindInfomodeGradientLegend method with BindInfomodeGradientLegendPrefix.
            MethodInfo originalMethod = typeof(InfoviewsUISystem).GetMethod("BindInfomodeGradientLegend", BindingFlags.Instance | BindingFlags.NonPublic);
            if (originalMethod == null)
            {
                LogUtil.Error($"Unable to find original method {nameof(InfoviewsUISystem)}.BindInfomodeGradientLegend.");
                return;
            }
            MethodInfo prefixMethod = typeof(InfoviewsUISystemPatch).GetMethod(nameof(BindInfomodeGradientLegendPrefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (prefixMethod == null)
            {
                LogUtil.Error($"Unable to find patch prefix method {nameof(InfoviewsUISystemPatch)}.{nameof(BindInfomodeGradientLegendPrefix)}.");
                return;
            }
            new Harmony(Mod.HarmonyID).Patch(originalMethod, new HarmonyMethod(prefixMethod), null);
        }

        /// <summary>
        /// Prefix patch method for InfoviewsUISystem.BindInfomodeGradientLegend().
        /// </summary>
        private static bool BindInfomodeGradientLegendPrefix(IJsonWriter writer, IGradientInfomode gradientInfomode)
        {
            // On the game's instance of this system, call the implementation of BindInfomodeGradientLegend.
            return _infoviewsUISystemPatch.BindInfomodeGradientLegendImpl(writer, gradientInfomode);
        }

        /// <summary>
        /// Implementation method that potentially replaces the call to InfoviewsUISystem.BindInfomodeGradientLegend().
        /// </summary>
        private bool BindInfomodeGradientLegendImpl(IJsonWriter writer, IGradientInfomode gradientInfomode)
        {
            // Parameter gradientInfomode is actually an InfomodePrefab.
            // Check if it is a building status infomode prefab.
            if (gradientInfomode.GetType() == typeof(BuildingStatusInfomodePrefab))
            {
                // Check if the infomode prefab is one of this mod's building status infomode prefabs for building levels.
                BuildingStatusInfomodePrefab buildingStatusInfomodePrefab = gradientInfomode as BuildingStatusInfomodePrefab;
                if (buildingStatusInfomodePrefab.m_Type == (BuildingStatusType)ImprovedBuildingStatusType.LevelResidential ||
                    buildingStatusInfomodePrefab.m_Type == (BuildingStatusType)ImprovedBuildingStatusType.LevelCommercial  ||
                    buildingStatusInfomodePrefab.m_Type == (BuildingStatusType)ImprovedBuildingStatusType.LevelIndustrial  ||
                    buildingStatusInfomodePrefab.m_Type == (BuildingStatusType)ImprovedBuildingStatusType.LevelOffice)
                {
                    // Logic adapted from Game.UI.InGame.InfoviewsUISystem.BindInfomodeGradientLegend() with the following differences:
                    // Difference 1:
                    //      Replace the game's default "Low" and "High" labels with "Level 1" and "Level 5".
                    // Difference 2:
                    //      Replace the game's default 3 gradient stops with 10 gradient stops.
                    //      The 10 gradient stops are 5 pairs of stops.
                    //      Each pair of stops is for the color of one building level.
                    //      The color for building level 2 is interpolated between the low and medium colors.
                    //      The color for building level 4 is interpolated between the medium and high colors.
		            writer.TypeBegin("infoviews.InfomodeGradientLegend");
		            writer.PropertyName("lowLabel");
                    writer.Write(new LocalizedString("LevelInfoPanel.LEVEL1", null, null));
		            writer.PropertyName("highLabel");
                    writer.Write(new LocalizedString("LevelInfoPanel.LEVEL5", null, null));
		            writer.PropertyName("gradient");
		            writer.TypeBegin("infoviews.Gradient");
		            writer.PropertyName("stops");
                    writer.ArrayBegin(10u);
                    BindGradientStop(writer, 0.00f, gradientInfomode.lowColor);
                    BindGradientStop(writer, 0.20f, gradientInfomode.lowColor);
                    BindGradientStop(writer, 0.20f, Color.Lerp(gradientInfomode.lowColor, gradientInfomode.mediumColor, Level2And4InterpolationFactor));
                    BindGradientStop(writer, 0.40f, Color.Lerp(gradientInfomode.lowColor, gradientInfomode.mediumColor, Level2And4InterpolationFactor));
                    BindGradientStop(writer, 0.40f, gradientInfomode.mediumColor);
                    BindGradientStop(writer, 0.60f, gradientInfomode.mediumColor);
                    BindGradientStop(writer, 0.60f, Color.Lerp(gradientInfomode.mediumColor, gradientInfomode.highColor, Level2And4InterpolationFactor));
                    BindGradientStop(writer, 0.80f, Color.Lerp(gradientInfomode.mediumColor, gradientInfomode.highColor, Level2And4InterpolationFactor));
                    BindGradientStop(writer, 0.80f, gradientInfomode.highColor);
                    BindGradientStop(writer, 1.00f, gradientInfomode.highColor);
                    writer.ArrayEnd();
		            writer.TypeEnd();
		            writer.TypeEnd();

                    // Do not execute the original method.
                    return false;
                }
            }

            // The infomode prefab is not one of this mod's building level infomode prefabs.
            // Execute the original method.
            return true;
        }

        /// <summary>
        /// Write a gradient stop.
        /// </summary>
	    private void BindGradientStop(IJsonWriter writer, float offset, Color color)
	    {
            // Logic copied exactly from Game.UI.InGame.InfoviewsUISystem.BindGradientStop().
		    writer.TypeBegin("infoviews.GradientStop");
		    writer.PropertyName("offset");
		    writer.Write(offset);
		    writer.PropertyName("color");
		    writer.Write(color);
		    writer.TypeEnd();
	    }

        /// <summary>
        /// Perform updates.
        /// </summary>
        protected override void OnUpdate()
        {
            // Nothing to do here, but implementation is required.
            // All work is performed in BindInfomodeGradientLegendImpl() which is called from BindInfomodeGradientLegendPrefix().
        }
    }
}
