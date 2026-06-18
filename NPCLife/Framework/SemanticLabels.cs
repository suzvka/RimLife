namespace NPCLife.Framework
{
    /// <summary>
    /// 将裸数值映射为语义标签。所有方法均为纯函数，线程安全，零外部依赖。
    /// </summary>
    public static class SemanticLabels
    {
        // ============================================================
        // Pawn 级标签
        // ============================================================

        /// <summary>
        /// 疼痛等级标签。阈值参考 RimWorld PainCategoryDef 体系。
        /// </summary>
        public static string MapPainTier(float pain)
        {
            if (pain <= 0f) return "None";
            if (pain < 0.08f) return "Minor";
            if (pain < 0.25f) return "Moderate";
            if (pain < 0.60f) return "Severe";
            if (pain < 0.85f) return "Extreme";
            return "Shock";
        }

        /// <summary>
        /// 心情等级标签。阈值参考精神崩溃阈值体系。
        /// </summary>
        public static string MapMoodTier(float mood)
        {
            if (mood <= 0.01f) return "Devastated";
            if (mood <= 0.12f) return "Miserable";
            if (mood <= 0.28f) return "Poor";
            if (mood <= 0.50f) return "Content";
            if (mood <= 0.75f) return "Happy";
            return "Thriving";
        }

        /// <summary>
        /// 失血严重度标签。致命线约 0.6。
        /// </summary>
        public static string MapBleedSeverity(float bleedRate)
        {
            if (bleedRate <= 0f) return "None";
            if (bleedRate < 0.15f) return "Minor";
            if (bleedRate < 0.40f) return "Serious";
            if (bleedRate < 0.60f) return "Critical";
            return "Fatal";
        }

        /// <summary>
        /// 能力等级标签 (0~1+)。
        /// </summary>
        public static string MapCapacityTier(float level)
        {
            if (level <= 0f) return "Disabled";
            if (level < 0.40f) return "Poor";
            if (level < 0.70f) return "Fair";
            if (level < 1.0f) return "Normal";
            if (level < 1.4f) return "Good";
            return "Excellent";
        }

        /// <summary>
        /// 社交好感度标签。opinion 范围约 -100 ~ +100。
        /// </summary>
        public static string MapOpinionTier(float opinion)
        {
            if (opinion >= 50f) return "Adoring";
            if (opinion >= 20f) return "Friendly";
            if (opinion >= 5f) return "Warm";
            if (opinion >= -20f) return "Neutral";
            if (opinion >= -50f) return "Cold";
            return "Hostile";
        }

        // ============================================================
        // 装备/物品级标签
        // ============================================================

        /// <summary>
        /// 装备耐久状态标签 (0~1)。
        /// </summary>
        public static string MapGearCondition(float durability)
        {
            if (durability <= 0f) return "Broken";
            if (durability < 0.25f) return "Worn";
            if (durability < 0.50f) return "Damaged";
            if (durability < 0.80f) return "Good";
            return "Pristine";
        }

        // ============================================================
        // 环境级标签
        // ============================================================

        /// <summary>
        /// 热舒适度标签。参考 GenTemperature 舒适区间。
        /// </summary>
        public static string MapThermalComfort(float tempCelsius)
        {
            if (tempCelsius < -10f) return "DeadlyCold";
            if (tempCelsius < 0f) return "VeryCold";
            if (tempCelsius < 12f) return "Cool";
            if (tempCelsius <= 32f) return "Comfortable";
            if (tempCelsius <= 45f) return "Warm";
            if (tempCelsius <= 55f) return "VeryHot";
            return "DeadlyHot";
        }

        /// <summary>
        /// 光照等级标签。参考 MapGlow / GlowGrid 阈值。
        /// </summary>
        public static string MapLightLevel(float glow)
        {
            if (glow < 0.01f) return "PitchDark";
            if (glow < 0.30f) return "Dim";
            if (glow < 0.60f) return "Lit";
            return "Bright";
        }

        // ============================================================
        // 殖民地/宏观级标签
        // ============================================================

        /// <summary>
        /// 食物储备状态 (按可维持天数)。
        /// </summary>
        public static string MapFoodStatus(float daysWorth)
        {
            if (daysWorth <= 0f) return "Starving";
            if (daysWorth < 2f) return "Famine";
            if (daysWorth < 5f) return "Low";
            if (daysWorth < 10f) return "Adequate";
            return "Abundant";
        }

        /// <summary>
        /// 电力状态 (盈余/赤字瓦特数)。
        /// </summary>
        public static string MapPowerStatus(float surplusWatts)
        {
            if (surplusWatts <= -100f) return "Blackout";
            if (surplusWatts < 0f) return "Strained";
            if (surplusWatts < 500f) return "Adequate";
            return "Stable";
        }

        /// <summary>
        /// 需求 DefName → 语义紧急标签。覆盖 RimWorld 原版常见需求。
        /// </summary>
        public static string MapNeedUrgency(string defName, float curLevel)
        {
            if (defName == null) return "Unknown";
            switch (defName)
            {
                case "Food":
                    if (curLevel < 0.10f) return "Starving";
                    if (curLevel < 0.25f) return "VeryHungry";
                    if (curLevel < 0.50f) return "Hungry";
                    return "Sated";
                case "Rest":
                    if (curLevel < 0.10f) return "Collapsed";
                    if (curLevel < 0.25f) return "VeryTired";
                    if (curLevel < 0.50f) return "Tired";
                    return "Rested";
                case "Joy":
                    if (curLevel < 0.10f) return "Bored";
                    if (curLevel < 0.30f) return "Restless";
                    return "Entertained";
                case "Beauty":
                    if (curLevel < 0.15f) return "UglySurroundings";
                    return "Aesthetic";
                case "Comfort":
                    if (curLevel < 0.15f) return "Uncomfortable";
                    return "Comfortable";
                case "Outdoors":
                    if (curLevel > 0.90f) return "CabinFever";
                    return "Fine";
                case "DrugDesire":
                case "Chemical":
                    if (curLevel < 0.20f) return "Withdrawal";
                    return "Craving";
                case "Suppression":
                    if (curLevel < 0.20f) return "Rebellious";
                    return "Suppressed";
                default:
                    return defName;
            }
        }

        /// <summary>
        /// 将 Faction 关系类型字符串映射为语义标签。纯函数，零 RimWorld 依赖。
        /// </summary>
        public static string MapFactionRelation(string kind)
        {
            switch (kind)
            {
                case "Ally": return "Ally";
                case "Hostile": return "Hostile";
                default: return "Neutral";
            }
        }
    }
}
