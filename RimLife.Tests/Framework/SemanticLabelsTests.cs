using RimLife.Framework;
using Xunit;

namespace RimLife.Tests.Framework
{
    /// <summary>
    /// SemanticLabels 纯逻辑断言测试。
    /// 所有方法均为纯函数，无任何外部依赖，适合完整断言覆盖。
    /// </summary>
    public class SemanticLabelsTests
    {
        // ================================================================
        // MapPainTier
        // ================================================================

        [Theory]
        [InlineData(0f, "None")]
        [InlineData(-1f, "None")]
        [InlineData(0.04f, "Minor")]
        [InlineData(0.15f, "Moderate")]
        [InlineData(0.40f, "Severe")]
        [InlineData(0.70f, "Extreme")]
        [InlineData(0.90f, "Shock")]
        public void MapPainTier_Thresholds_ReturnCorrectLabel(float pain, string expected)
        {
            Assert.Equal(expected, SemanticLabels.MapPainTier(pain));
        }

        // ================================================================
        // MapMoodTier
        // ================================================================

        [Theory]
        [InlineData(0f, "Devastated")]
        [InlineData(0.01f, "Devastated")]
        [InlineData(0.05f, "Miserable")]
        [InlineData(0.12f, "Miserable")]
        [InlineData(0.20f, "Poor")]
        [InlineData(0.40f, "Content")]
        [InlineData(0.60f, "Happy")]
        [InlineData(0.80f, "Thriving")]
        public void MapMoodTier_Thresholds_ReturnCorrectLabel(float mood, string expected)
        {
            Assert.Equal(expected, SemanticLabels.MapMoodTier(mood));
        }

        // ================================================================
        // MapBleedSeverity
        // ================================================================

        [Theory]
        [InlineData(0f, "None")]
        [InlineData(-1f, "None")]
        [InlineData(0.10f, "Minor")]
        [InlineData(0.25f, "Serious")]
        [InlineData(0.50f, "Critical")]
        [InlineData(0.70f, "Fatal")]
        public void MapBleedSeverity_Thresholds_ReturnCorrectLabel(float bleed, string expected)
        {
            Assert.Equal(expected, SemanticLabels.MapBleedSeverity(bleed));
        }

        // ================================================================
        // MapCapacityTier
        // ================================================================

        [Theory]
        [InlineData(0f, "Disabled")]
        [InlineData(0.20f, "Poor")]
        [InlineData(0.50f, "Fair")]
        [InlineData(0.80f, "Normal")]
        [InlineData(1.20f, "Good")]
        [InlineData(1.50f, "Excellent")]
        public void MapCapacityTier_Thresholds_ReturnCorrectLabel(float level, string expected)
        {
            Assert.Equal(expected, SemanticLabels.MapCapacityTier(level));
        }

        // ================================================================
        // MapOpinionTier
        // ================================================================

        [Theory]
        [InlineData(60f, "Adoring")]
        [InlineData(50f, "Adoring")]
        [InlineData(30f, "Friendly")]
        [InlineData(20f, "Friendly")]
        [InlineData(10f, "Warm")]
        [InlineData(5f, "Warm")]
        [InlineData(0f, "Neutral")]
        [InlineData(-19f, "Neutral")]
        [InlineData(-30f, "Cold")]
        [InlineData(-60f, "Hostile")]
        public void MapOpinionTier_Thresholds_ReturnCorrectLabel(float opinion, string expected)
        {
            Assert.Equal(expected, SemanticLabels.MapOpinionTier(opinion));
        }

        // ================================================================
        // MapGearCondition
        // ================================================================

        [Theory]
        [InlineData(0f, "Broken")]
        [InlineData(0.10f, "Worn")]
        [InlineData(0.30f, "Damaged")]
        [InlineData(0.60f, "Good")]
        [InlineData(0.90f, "Pristine")]
        public void MapGearCondition_Thresholds_ReturnCorrectLabel(float durability, string expected)
        {
            Assert.Equal(expected, SemanticLabels.MapGearCondition(durability));
        }

        // ================================================================
        // MapThermalComfort
        // ================================================================

        [Theory]
        [InlineData(-20f, "DeadlyCold")]
        [InlineData(-5f, "VeryCold")]
        [InlineData(5f, "Cool")]
        [InlineData(20f, "Comfortable")]
        [InlineData(40f, "Warm")]
        [InlineData(50f, "VeryHot")]
        [InlineData(60f, "DeadlyHot")]
        public void MapThermalComfort_Thresholds_ReturnCorrectLabel(float temp, string expected)
        {
            Assert.Equal(expected, SemanticLabels.MapThermalComfort(temp));
        }

        // ================================================================
        // MapLightLevel
        // ================================================================

        [Theory]
        [InlineData(0f, "PitchDark")]
        [InlineData(0.005f, "PitchDark")]
        [InlineData(0.15f, "Dim")]
        [InlineData(0.45f, "Lit")]
        [InlineData(0.70f, "Bright")]
        public void MapLightLevel_Thresholds_ReturnCorrectLabel(float glow, string expected)
        {
            Assert.Equal(expected, SemanticLabels.MapLightLevel(glow));
        }

        // ================================================================
        // MapFoodStatus
        // ================================================================

        [Theory]
        [InlineData(0f, "Starving")]
        [InlineData(-1f, "Starving")]
        [InlineData(1f, "Famine")]
        [InlineData(3f, "Low")]
        [InlineData(7f, "Adequate")]
        [InlineData(15f, "Abundant")]
        public void MapFoodStatus_Thresholds_ReturnCorrectLabel(float days, string expected)
        {
            Assert.Equal(expected, SemanticLabels.MapFoodStatus(days));
        }

        // ================================================================
        // MapPowerStatus
        // ================================================================

        [Theory]
        [InlineData(-200f, "Blackout")]
        [InlineData(-100f, "Blackout")]
        [InlineData(-50f, "Strained")]
        [InlineData(200f, "Adequate")]
        [InlineData(600f, "Stable")]
        public void MapPowerStatus_Thresholds_ReturnCorrectLabel(float watts, string expected)
        {
            Assert.Equal(expected, SemanticLabels.MapPowerStatus(watts));
        }

        // ================================================================
        // MapNeedUrgency
        // ================================================================

        [Fact]
        public void MapNeedUrgency_NullDefName_ReturnsUnknown()
        {
            Assert.Equal("Unknown", SemanticLabels.MapNeedUrgency(null, 0.5f));
        }

        [Theory]
        [InlineData("Food", 0.05f, "Starving")]
        [InlineData("Food", 0.15f, "VeryHungry")]
        [InlineData("Food", 0.35f, "Hungry")]
        [InlineData("Food", 0.60f, "Sated")]
        [InlineData("Rest", 0.05f, "Collapsed")]
        [InlineData("Rest", 0.15f, "VeryTired")]
        [InlineData("Rest", 0.35f, "Tired")]
        [InlineData("Rest", 0.60f, "Rested")]
        [InlineData("Joy", 0.05f, "Bored")]
        [InlineData("Joy", 0.20f, "Restless")]
        [InlineData("Joy", 0.50f, "Entertained")]
        [InlineData("Beauty", 0.05f, "UglySurroundings")]
        [InlineData("Beauty", 0.50f, "Aesthetic")]
        [InlineData("Comfort", 0.05f, "Uncomfortable")]
        [InlineData("Comfort", 0.50f, "Comfortable")]
        [InlineData("Outdoors", 0.95f, "CabinFever")]
        [InlineData("Outdoors", 0.50f, "Fine")]
        [InlineData("DrugDesire", 0.05f, "Withdrawal")]
        [InlineData("Chemical", 0.05f, "Withdrawal")]
        [InlineData("Chemical", 0.50f, "Craving")]
        [InlineData("Suppression", 0.05f, "Rebellious")]
        [InlineData("Suppression", 0.50f, "Suppressed")]
        public void MapNeedUrgency_KnownNeeds_ReturnCorrectLabel(string defName, float level, string expected)
        {
            Assert.Equal(expected, SemanticLabels.MapNeedUrgency(defName, level));
        }

        [Fact]
        public void MapNeedUrgency_UnknownDefName_ReturnsDefName()
        {
            Assert.Equal("CustomNeed", SemanticLabels.MapNeedUrgency("CustomNeed", 0.5f));
        }

        // ================================================================
        // MapFactionRelation
        // ================================================================

        [Theory]
        [InlineData("Ally", "Ally")]
        [InlineData("Hostile", "Hostile")]
        [InlineData("Neutral", "Neutral")]
        [InlineData("Unknown", "Neutral")]
        [InlineData(null, "Neutral")]
        public void MapFactionRelation_VariousInputs_ReturnsCorrectLabel(string kind, string expected)
        {
            Assert.Equal(expected, SemanticLabels.MapFactionRelation(kind));
        }
    }
}
