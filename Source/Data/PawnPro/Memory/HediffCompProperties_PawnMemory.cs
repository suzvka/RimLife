using Verse;

namespace RimLife
{
    /// <summary>
    /// PawnProMemory Hediff 的配置属性类。
    /// 此类作为 HediffCompProperties 的子类，在 XML 中被引用。
    /// 对应的运行时组件是 HediffComp_PawnMemory。
    /// </summary>
    public class HediffCompProperties_PawnMemory : HediffCompProperties
    {
        public HediffCompProperties_PawnMemory()
        {
            compClass = typeof(HediffComp_PawnMemory);
        }
    }
}
