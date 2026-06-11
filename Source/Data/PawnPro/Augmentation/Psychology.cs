using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimLife
{
    /// <summary>
    /// PersonalityExtension: 基于"大五"模型，从 TraitDef 的 ModExtension 中读取人格数据。
    /// 由 CharacterCardMapper.MapPsychology 使用。
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
