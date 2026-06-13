using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimLife
{
    /// <summary>
    /// RimWorld DefModExtension，用于从 TraitDef XML 数据中读取人格维度。
    /// 仅作为反序列化载体存在，框架对此无感知。
    /// </summary>
    public class PersonalityExtension : DefModExtension
    {
        public List<PersonalityEntry> data = new List<PersonalityEntry>();

        public PersonalityEntry GetByDegree(int degree)
        {
            if (data == null) return PersonalityEntry.Zero;
            return data.FirstOrDefault(x => x.degree == degree) ?? PersonalityEntry.Zero;
        }
    }

    /// <summary>
    /// 单条人格条目：一个 degree 对应的五维贡献值。
    /// </summary>
    public class PersonalityEntry
    {
        public int degree = 0;
        public int openness = 0;
        public int conscientiousness = 0;
        public int extraversion = 0;
        public int agreeableness = 0;
        public int neuroticism = 0;
        public static readonly PersonalityEntry Zero = new PersonalityEntry();
    }
}
