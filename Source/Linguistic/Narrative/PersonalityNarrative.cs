using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using Verse;

namespace RimLife
{
    /// <summary>
    /// 定义人格特质的描述性级别。
    /// </summary>
    public enum TraitLevel
    {
        Undefined,
        VeryLow,
        Low,
        Average,
        High,
        VeryHigh
    }

    /// <summary>
    /// 一个简单的五维向量，表示 O/C/E/A/N 五个轴的数值贡献。
    /// </summary>
    public struct BigFiveVector
    {
        public int Openness;
        public int Conscientiousness;
        public int Extraversion;
        public int Agreeableness;
        public int Neuroticism;

        public static BigFiveVector Zero => new BigFiveVector();

        public BigFiveVector(int o, int c, int e, int a, int n)
        {
            Openness = o;
            Conscientiousness = c;
            Extraversion = e;
            Agreeableness = a;
            Neuroticism = n;
        }

        public void AddInPlace(BigFiveVector other)
        {
            Openness += other.Openness;
            Conscientiousness += other.Conscientiousness;
            Extraversion += other.Extraversion;
            Agreeableness += other.Agreeableness;
            Neuroticism += other.Neuroticism;
        }

        public static BigFiveVector Add(BigFiveVector a, BigFiveVector b)
        {
            return new BigFiveVector(
                a.Openness + b.Openness,
                a.Conscientiousness + b.Conscientiousness,
                a.Extraversion + b.Extraversion,
                a.Agreeableness + b.Agreeableness,
                a.Neuroticism + b.Neuroticism);
        }

        public bool IsZero()
        {
            return Openness ==0 && Conscientiousness ==0 && Extraversion ==0 && Agreeableness ==0 && Neuroticism ==0;
        }

        public override string ToString()
        {
            return $"O={Openness} C={Conscientiousness} E={Extraversion} A={Agreeableness} N={Neuroticism}";
        }

        public static BigFiveVector FromEntry(PersonalityEntry entry)
        {
            if (entry == null) return Zero;
            return new BigFiveVector(entry.openness, entry.conscientiousness, entry.extraversion, entry.agreeableness, entry.neuroticism);
        }
    }

    /// <summary>
    /// 单个 Trait 在人格聚合阶段的贡献快照（仅用于调试输出）。
    /// </summary>
    public struct PersonalityTraitContribution
    {
        public string DefName;
        public int Degree;
        public BigFiveVector Vector;

        public override string ToString() => $"{DefName}({Degree}): {Vector}";
    }

    /// <summary>
    /// 基于“大五”模型提供 pawn 人格的语言学表述。
    /// 此类将复杂的 pawn 数据（特质、背景故事等）转换为结构化的、描述性的人格维度。
    /// </summary>
    internal class PersonalityNarrative
    {
        #region Big Five Personality Traits

        public TraitLevel Openness { get; set; }
        public TraitLevel Conscientiousness { get; set; }
        public TraitLevel Extraversion { get; set; }
        public TraitLevel Agreeableness { get; set; }
        public TraitLevel Neuroticism { get; set; }

        #endregion

        // 基础（来自 Traits + XML 扩展）的五维向量
        private BigFiveVector _baseFromTraits;
        // 外部/动态注入的五维向量，分源存储（便于替换/撤销）
        private readonly Dictionary<string, BigFiveVector> _externals = new Dictionary<string, BigFiveVector>();
        //贡献明细（调试）
        private readonly List<PersonalityTraitContribution> _traitContributions = new List<PersonalityTraitContribution>();

        //只读数值接口（当前总值 & 基础值 & 外部值）
        public int OpennessScore => GetTotalVector().Openness;
        public int ConscientiousnessScore => GetTotalVector().Conscientiousness;
        public int ExtraversionScore => GetTotalVector().Extraversion;
        public int AgreeablenessScore => GetTotalVector().Agreeableness;
        public int NeuroticismScore => GetTotalVector().Neuroticism;

        public BigFiveVector BaseVector => _baseFromTraits; // struct copy
        public IReadOnlyDictionary<string, BigFiveVector> ExternalVectors => _externals; // reference safe enough
        public IReadOnlyList<PersonalityTraitContribution> TraitContributions => _traitContributions;

        public PersonalityNarrative(PawnPro pawnPro)
        {
            if (pawnPro == null) throw new ArgumentNullException(nameof(pawnPro));
            //计算基础向量
            RecomputeBaseFrom(pawnPro);
            // 基于当前总向量计算枚举标签
            RecalculateLevels();
        }

        /// <summary>
        /// 从 PawnPro 对象创建 PersonalityNarrative 的工厂方法。
        /// </summary>
        public static PersonalityNarrative From(PawnPro pawnPro)
        {
            return new PersonalityNarrative(pawnPro);
        }

        /// <summary>
        /// 重新根据 pawn 的 Traits（结合 XML modExtensions）计算基础五维向量。
        /// </summary>
        public void RecomputeBaseFrom(PawnPro pawnPro)
        {
            _baseFromTraits = BigFiveVector.Zero;
            _traitContributions.Clear();
            if (pawnPro == null || pawnPro.Mood == null || pawnPro.Mood.Traits == null) return;

            // 遍历 Trait 快照
            foreach (var t in pawnPro.Mood.Traits)
            {
                if (t.DefName == null) continue;
                TraitDef def = DefDatabase<TraitDef>.GetNamedSilentFail(t.DefName);
                if (def == null) continue;

                var ext = def.GetModExtension<PersonalityExtension>();
                if (ext == null) continue;

                PersonalityEntry match = ext.GetByDegree(t.Degree);
                if (match == null) continue; //保护性判断

                var vec = BigFiveVector.FromEntry(match);
                if (!vec.IsZero())
                {
                    _baseFromTraits.AddInPlace(vec);
                }
                _traitContributions.Add(new PersonalityTraitContribution
                {
                    DefName = t.DefName,
                    Degree = t.Degree,
                    Vector = vec
                });
            }
        }

        /// <summary>
        /// 导入/叠加一个外部（动态）人格向量。
        /// </summary>
        /// <param name="sourceKey">来源键（唯一标识）。</param>
        /// <param name="delta">要叠加的向量。</param>
        /// <param name="replace">true 覆盖同名来源；false 累加。</param>
        public void ImportExternal(string sourceKey, BigFiveVector delta, bool replace = false)
        {
            if (string.IsNullOrEmpty(sourceKey)) return;
            if (replace || !_externals.ContainsKey(sourceKey))
            {
                _externals[sourceKey] = delta;
            }
            else
            {
                var cur = _externals[sourceKey];
                cur.AddInPlace(delta);
                _externals[sourceKey] = cur;
            }
            RecalculateLevels();
        }

        /// <summary>
        /// 移除某个外部来源的向量。
        /// </summary>
        public bool RemoveExternal(string sourceKey)
        {
            if (string.IsNullOrEmpty(sourceKey)) return false;
            bool removed = _externals.Remove(sourceKey);
            if (removed) RecalculateLevels();
            return removed;
        }

        /// <summary>
        /// 清空所有外部来源。
        /// </summary>
        public void ClearExternal()
        {
            if (_externals.Count ==0) return;
            _externals.Clear();
            RecalculateLevels();
        }

        /// <summary>
        /// 获取当前总向量（基础 + 所有外部）。
        /// </summary>
        public BigFiveVector GetTotalVector()
        {
            var total = _baseFromTraits;
            foreach (var kv in _externals)
            {
                total.AddInPlace(kv.Value);
            }
            return total;
        }

        /// <summary>
        /// 由当前总向量动态计算五个标签枚举。
        /// 区分 “无贡献(Undefined)” 与 “贡献相互抵消后为0(Average)”。
        /// </summary>
        public void RecalculateLevels()
        {
            var total = GetTotalVector();

            //逐轴检查是否存在任何来源贡献（基础或任一外部）
            bool contribO = (_baseFromTraits.Openness !=0) || _externals.Values.Any(v => v.Openness !=0);
            bool contribC = (_baseFromTraits.Conscientiousness !=0) || _externals.Values.Any(v => v.Conscientiousness !=0);
            bool contribE = (_baseFromTraits.Extraversion !=0) || _externals.Values.Any(v => v.Extraversion !=0);
            bool contribA = (_baseFromTraits.Agreeableness !=0) || _externals.Values.Any(v => v.Agreeableness !=0);
            bool contribN = (_baseFromTraits.Neuroticism !=0) || _externals.Values.Any(v => v.Neuroticism !=0);

            Openness = MapFromSum(total.Openness, contribO);
            Conscientiousness = MapFromSum(total.Conscientiousness, contribC);
            Extraversion = MapFromSum(total.Extraversion, contribE);
            Agreeableness = MapFromSum(total.Agreeableness, contribA);
            Neuroticism = MapFromSum(total.Neuroticism, contribN);
        }

        private static TraitLevel MapFromSum(int sum, bool hadContribution)
        {
            if (!hadContribution) return TraitLevel.Undefined;
            if (sum <= -4) return TraitLevel.VeryLow;
            if (sum <= -1) return TraitLevel.Low;
            if (sum ==0) return TraitLevel.Average;
            if (sum <=3) return TraitLevel.High;
            return TraitLevel.VeryHigh;
        }

        #region Mapping Rule Signatures (Legacy placeholders)

        #endregion
    }

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
        public int degree =0;
        public int openness =0;
        public int conscientiousness =0;
        public int extraversion =0;
        public int agreeableness =0;
        public int neuroticism =0;
        public static readonly PersonalityEntry Zero = new PersonalityEntry();
    }
}
