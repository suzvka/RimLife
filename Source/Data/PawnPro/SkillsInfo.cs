using System.Collections.Generic;
using System.Threading.Tasks;
using RimWorld;
using Verse;

namespace RimLife
{
	/// <summary>
	/// 表示 Pawn 技能的快照，包括等级和热情。
	/// 注意：此数据为快照，不保证其时序一致性。
	/// </summary>
	public class SkillsInfo
	{
		/// <summary>
		/// Pawn 的所有技能，包含等级和热情信息。
		/// 当 Pawn 没有技能时（例如，非类人生物），此列表为空。
		/// </summary>
		public IReadOnlyList<SkillEntry> AllSkills { get; }

		private SkillsInfo()
		{
			AllSkills = new List<SkillEntry>();
		}

		private SkillsInfo(IReadOnlyList<SkillEntry> allSkills)
		{
			AllSkills = allSkills;
		}

		/// <summary>
		/// 从 Pawn 创建技能快照。必须在主线程上调用。
		/// </summary>
		public static SkillsInfo CreateFrom(Pawn p)
		{
			if (p?.skills == null) return new SkillsInfo();

			var list = new List<SkillEntry>();
			var skills = p.skills.skills; // RimWorld 提供一个 SkillRecord 列表
			if (skills != null)
			{
				foreach (var sr in skills)
				{
					if (sr == null || sr.def == null) continue;
					Passion passion;
					try { passion = sr.passion; }
					catch { passion = Passion.None; }

					bool hasPassion = passion != Passion.None;

					string label = sr.def.label ?? sr.def.defName; // label 适合显示；defName 作为后备

					list.Add(new SkillEntry
					{
						DefName = sr.def.defName,
						Label = label,
						Level = sr.Level,
						Passion = passion.ToString(),
						HasPassion = hasPassion,
						TotallyDisabled = sr.TotallyDisabled
					});
				}
			}

			return new SkillsInfo(list);
		}

		/// <summary>
		/// 通过将工作分派到主线程来异步创建 SkillsInfo 快照。
		/// </summary>
		public static Task<SkillsInfo> CreateFromAsync(Pawn p)
		{
			if (p == null) return Task.FromResult(new SkillsInfo());
			return MainThreadDispatcher.EnqueueAsync(() => CreateFrom(p));
		}
	}

	public struct SkillEntry
	{
		public string DefName; // 技能ID (例如 "Shooting")
		public string Label; // 显示名
		public int Level; // 数值等级 (0-20)
		public string Passion; // 热情：None / Minor / Major
		public bool HasPassion; // 是否有热情
		public bool TotallyDisabled; // 是否彻底禁用
	}
}
