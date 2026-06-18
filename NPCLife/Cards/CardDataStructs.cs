namespace NPCLife.Cards
{
    /// <summary>
    /// 殖民者轻量摘要（不展开子模块）。
    /// </summary>
    public struct ColonistSummary
    {
        public string ID;
        public string Name;
        public bool IsDead;
        public string CurrentJob;
        public string MoodTier;
        public string PainTier;
        public string PawnRelation;
    }

    /// <summary>
    /// 派系关系快照。
    /// </summary>
    public struct FactionStanding
    {
        public string FactionName;
        public float Goodwill;
        public string RelationLabel;
    }

    /// <summary>
    /// 天气信息快照。
    /// </summary>
    public struct WeatherInfo
    {
        public string Label;
        public string Description;
        public bool IsRain;
        public bool IsSnow;
        public float WindSpeed;
    }
}
