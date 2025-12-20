using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using Verse;

namespace RimLife
{
	public enum Level
	{
		Undefined,
		VeryLow,
		Low,
		Average,
		High,
		VeryHigh
	}

	public class Keywords
	{
        public enum FunctionWord
        {
            None,   // 零形式
            Of,     // 的（万金油：所属、属性、关系）
            In,     // 在（位置、时间、状态）
            With,   // 用（工具、方式、材料）
            Give,   // 给（与事、目标、受益）
            From,   // 从（来源、起点）
            To,     // 到（目标、终点）
            By,     // 被（施事标记，被动）
            And,    // 和（连接词性与语义角色相同的词）
        }

        public List<string> Tags
        {
            get
            {
                var allWords = (Agent ?? Enumerable.Empty<Word>())
                    .Concat(Patient ?? Enumerable.Empty<Word>())
                    .Concat(Recipient ?? Enumerable.Empty<Word>())
                    .Concat(Theme ?? Enumerable.Empty<Word>())
                    .Concat(Predicate ?? Enumerable.Empty<Word>())
                    .Concat(Result ?? Enumerable.Empty<Word>());

                return allWords
                    .Where(w => w?.Tags != null)
                    .SelectMany(w => w.Tags)
                    .Distinct()
                    .ToList();
            }
        }

        List<Word> Agent;         // 施事者：谁发起动作
        List<Word> Patient;       // 受事者：谁/什么承受动作 
        List<Word> Recipient;     // 与事者：谁接收（双宾动词需要）
        List<Word> Theme;         // 客体：被移动/传递的事物 
        List<Word> Predicate;     // 谓语：动作/状态本身
        List<Word> Result;        // 结果：动作导致的终态

		public Keywords() 
        {
            Agent = new List<Word>();
            Patient = new List<Word>();
            Recipient = new List<Word>();
            Theme = new List<Word>();
            Predicate = new List<Word>();
            Result = new List<Word>();
        }

        public Keywords AddAgent(Word NewWord) { 
            Agent.Add(NewWord);
            return this; 
        }
        public Keywords AddPatient(Word NewWord)
        {
            Patient.Add(NewWord);
            return this;
        }
        public Keywords AddRecipient(Word NewWord)
        {
            Recipient.Add(NewWord);
            return this;
        }
        public Keywords AddTheme(Word NewWord)
        {
            Theme.Add(NewWord);
            return this;
        }
        public Keywords AddPredicate(Word NewWord)
        {
            Predicate.Add(NewWord);
            return this;
        }
        public Keywords AddResult(Word NewWord)
        {
            Result.Add(NewWord);
            return this;
        }

        public static FunctionWord GetFunction(Word front, Word after)
        {
            if (front == null || after == null)
            {
                return FunctionWord.None;
            }

            switch (front.Type)
            {
                case PartOfSpeech.Noun:
                    switch (after.Type)
                    {
                        case PartOfSpeech.Noun:
                            return FunctionWord.Of; // 名词 + 名词 → "of" (所属/修饰关系)
                        case PartOfSpeech.Verb:
                            return FunctionWord.To; // 名词 + 动词 → "to" (目的/不定式)
                        case PartOfSpeech.Adjective:
                            return FunctionWord.With; // 名词 + 形容词 → "with" (带有...特征)
                    }
                    break;

                case PartOfSpeech.Verb:
                    switch (after.Type)
                    {
                        case PartOfSpeech.Noun:
                            // 动词 + 名词 的情况很复杂，取决于具体动词。
                            // "with" 通常表示工具/伴随 (e.g., "cut with knife")
                            // "to" 通常表示方向/目标 (e.g., "go to school")
                            // 这里可以根据 front (动词) 的语义角色或类型进一步细分。
                            // 暂时保留 "With" 作为一种常见情况。
                            return FunctionWord.With;
                        case PartOfSpeech.Adverb:
                            // 副词直接修饰动词，通常不需要介词 (e.g., "run quickly")
                            return FunctionWord.None;
                    }
                    break;

                case PartOfSpeech.Adjective:
                    if (after.Type == PartOfSpeech.Noun)
                    {
                        return FunctionWord.Of; // 形容词 + 名词 → "of" (e.g., "proud of him")
                    }
                    break;
            }

            // 默认情况：无介词
            // (如：代词+名词，限定词+名词等保持无介词)
            return FunctionWord.None;
        }
	}

	// 叙事轴向量：axisKey -> intensity
	// 例：need.hunger=0.8, urgency=0.5, valence.neg=0.4, social.request=0.2
	public sealed class AxisVector
	{
		private readonly Dictionary<string, float> _axes = new(StringComparer.OrdinalIgnoreCase);

		public IReadOnlyDictionary<string, float> Weights => new ReadOnlyDictionary<string, float>(_axes);

		public AxisVector Set(string axisKey, float value)
		{
			if (!string.IsNullOrWhiteSpace(axisKey))
				_axes[axisKey] = value;
			return this;
		}

		public AxisVector Add(string axisKey, float delta)
		{
			if (string.IsNullOrWhiteSpace(axisKey)) return this;
			_axes[axisKey] = _axes.TryGetValue(axisKey, out var old) ? old + delta : delta;
			return this;
		}

		public float Get(string axisKey, float fallback = 0f)
			=> _axes.TryGetValue(axisKey, out var v) ? v : fallback;

		public bool IsEmpty => _axes.Count == 0;
	}

	/// <summary>
	/// Fact：事实卡（中间件）
	/// - 不负责“选模板/排版/主题策略”
	/// - 负责：把游戏数值语义化（Slots），并提供主题可消费的元数据（Tags + Axes + Source）
	/// </summary>
	public sealed class Fact
	{
        // --- Identity / trace ---
        List<Keywords> Keywords;

        public List<string> Tags
        {
            get
            {
                if (Keywords == null)
                {
                    return new List<string>();
                }
                return Keywords
                    .Where(kw => kw.Tags != null)
                    .SelectMany(kw => kw.Tags)
                    .Distinct()
                    .ToList();
            }
        }

        // --- Entities / time ---
        public int MapId { get; set; }
        public int GameTick { get; }					// 时间戳（RimWorld tick 或你自己的逻辑时间）

		// --- Selection metadata for Theme ---
		public AxisVector NarrativeAxes { get; }		// 叙事轴/语义轴向量（强度标签）；可用于合并/随机倾向
		public float Confidence { get; }				// 置信度

        public Fact(
            List<Keywords> keywords = default,
			AxisVector narrativeaxes = default,
			float confidence = 1
		)
		{
			NarrativeAxes = narrativeaxes ?? new AxisVector();
			Confidence = Math.Clamp(confidence, 0f, 1f);
            Keywords = keywords ?? new List<Keywords>();
            MapId = Find.CurrentMap.uniqueID;
            GameTick = MapTick.Get(MapId);
        }

        public Keywords AddThing()
		{
			var NewThing = new Keywords();
            Keywords.Add(NewThing); 
			return NewThing;
        }

		public List<Keywords> GetThing(List<string> Tags = default)
		{
            if (Tags == default || !Tags.Any())
            {
				return Keywords?.ToList() ?? new List<Keywords>();
            }

            return Keywords
                .Where(kw => kw.Tags != null && Tags.All(tag => kw.Tags.Contains(tag)))
                .ToList();
        }
    }
}