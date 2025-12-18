using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
		// 核心槽：允许单值或多值（用 Node 表达）
		public Node Agent;          // 施事者：谁发起动作
		public Node Patient;		// 受事者：谁/什么承受动作 
		public Node Recipient;		// 与事者：谁接收（双宾动词需要）
		public Node Theme;			// 客体：被移动/传递的事物 
		public Node Predicate;		// 谓语：动作/状态本身
		public Node Result;			// 结果：动作导致的终态

		// 额外子句（递归）
		public List<ClauseNode> Subclauses = new();

		// 截断/诊断
		public List<Node> Overflow = new();
		public List<string> Diagnostics = new();

		// 支持 Word、并列短语、介词短语、子句等
		public abstract class Node
		{
			public abstract string Render(RenderContext ctx);
		}

		public sealed class WordNode : Node
		{
			public Word Word;
			public override string Render(RenderContext ctx) => Word.Text;
		}

		// 并列短语：A、B和C
		public sealed class CoordNode : Node
		{
			public List<Node> Items = new();

			public override string Render(RenderContext ctx)
			{
				// 机械但稳定：2个用“和”，>=3 用顿号+和
				var parts = Items.Select(i => i.Render(ctx)).Where(s => !string.IsNullOrEmpty(s)).ToList();
				if (parts.Count == 0) return "";
				if (parts.Count == 1) return parts[0];
				if (parts.Count == 2) return $"{parts[0]}和{parts[1]}";
				return string.Join("、", parts.Take(parts.Count - 1)) + "和" + parts.Last();
			}
		}

		// 子句容器
		public sealed class ClauseNode : Node
		{
			public Keywords Child;
			public string Connector;         // 因为/但是/而/所以…
			public WordType? SharedRoleHost;   // Relative 时：修饰 AGENT/PATIENT...

			public override string Render(RenderContext ctx)
			{
				// 这里调用统一渲染器，但传入 mode/form
				return ctx.RenderFact(Child, this);
			}
		}

		public sealed class RenderContext
		{
			public Func<Keywords, ClauseNode, string> RenderFact; // 统一渲染器入口
		}

		public string GetAgent()
		{
			return Agent?.Render(new RenderContext { RenderFact = (keywords, clause) => clause.Render(new RenderContext()) });
		}

		public string GetPatient()
		{
			return Patient?.Render(new RenderContext { RenderFact = (keywords, clause) => clause.Render(new RenderContext()) });
		}

		public string GetRole()
		{
			return Theme?.Render(new RenderContext { RenderFact = (keywords, clause) => clause.Render(new RenderContext()) });
		}

		public string GetPredicate()
		{
			return Predicate?.Render(new RenderContext { RenderFact = (keywords, clause) => clause.Render(new RenderContext()) });
		}

		public string GetRecipient()
		{
			return Recipient?.Render(new RenderContext { RenderFact = (keywords, clause) => clause.Render(new RenderContext()) });
		}

        public string GetResult()
        {
            return Result?.Render(new RenderContext { RenderFact = (keywords, clause) => clause.Render(new RenderContext()) });
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

		public IEnumerable<KeyValuePair<string, float>> Pairs() => _axes;

		// 用于配额扣除：只处理正向消耗轴（按你模型：生成 Fact 就扣 quota）。
		public bool CanPay(AxisVector budget, float eps = 1e-6f)
		{
			foreach (var (k, cost) in _axes)
			{
				if (cost <= eps) continue;
				if (budget.Get(k) + eps < cost) return false;
			}
			return true;
		}

		public void PayFrom(ref AxisVector budget)
		{
			foreach (var (k, cost) in _axes)
			{
				if (cost <= 0f) continue;
				budget._axes[k] = Math.Max(0f, budget.Get(k) - cost);
			}
		}

		public float Dot(AxisVector other)
		{
			// 稀疏点积：用于主题偏好/随机倾向评分（仍可保持强随机）。
			float sum = 0f;
			// 遍历更小的一方更快；这里简单写。
			foreach (var (k, v) in _axes)
				sum += v * other.Get(k);
			return sum;
		}

		public override string ToString()
			=> string.Join(", ",
				_axes.OrderByDescending(kv => Math.Abs(kv.Value))
					 .Take(10)
					 .Select(kv => $"{kv.Key}:{kv.Value:0.##}"));
	}

	/// <summary>
	/// Fact：事实卡（中间件）
	/// - 不负责“选模板/排版/主题策略”
	/// - 负责：把游戏数值语义化（Slots），并提供主题可消费的元数据（Tags + Axes + Source）
	/// </summary>
	public sealed class Fact
	{
		// --- Identity / trace ---

		public string SubjectId;

		public string ObjectId;

        public string SchemaId { get; }     // 可扩展事实类型 ID（不枚举），例："pawn.need.hunger"

        // --- Entities / time ---
        public Keywords keywords;
		public int GameTick { get; }        // 时间戳（RimWorld tick 或你自己的逻辑时间）

		// --- Selection metadata for Theme ---
		public AxisVector NarrativeAxes { get; }                 // 叙事轴/语义轴向量（强度标签）；可用于合并/随机倾向
		public float Confidence { get; }                // 置信度

		

        private Fact(
			string schemaId,
			string subject,
			int gameTick,
			AxisVector narrativeaxes,
			float confidence =1,
			string obj = ""
		)
		{
			SchemaId = schemaId;

			SubjectId = keywords.GetAgent();
			ObjectId = keywords.GetPatient();
			GameTick = gameTick;

			NarrativeAxes = narrativeaxes;

			Confidence = Math.Clamp(confidence, 0f, 1f);
		}
	}
}