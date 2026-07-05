using NPCLife.Core;
using NPCLife.Framework.Script;
using RimLife.Infrastructure.Mcp;
using System.Collections.Generic;
using Verse;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// IScriptLineResolver 默认实现。
    /// 通过 PawnQueryHelper 查找 Pawn，取其显示名填充 ScriptLine.SpeakerName。
    /// </summary>
    internal class DefaultScriptLineResolver : IScriptLineResolver
    {
        public void Resolve(IReadOnlyList<ScriptLine> lines)
        {
            if (lines == null) return;

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];

                // 跳过无 SpeakerId 的行（旁白/动作/停顿无需解析）
                if (string.IsNullOrEmpty(line.SpeakerId))
                {
                    line.SpeakerName = null;
                    continue;
                }

                try
                {
                    var pawn = PawnQueryHelper.FindPawnById(line.SpeakerId);
                    if (pawn != null)
                    {
                        // 优先使用 Name（殖民者有正式名字），其次 LabelShortCap（通用简短名）
                        line.SpeakerName = pawn.Name?.ToStringShort
                            ?? pawn.LabelShortCap
                            ?? line.SpeakerId;
                    }
                    else
                    {
                        // Pawn 已死亡或离开地图，保留原始 ID 供调试追溯。
                        line.SpeakerName = $"?{line.SpeakerId}";
                    }
                }
                catch (System.Exception)
                {
                    // 解析失败，保留原始 ID
                    line.SpeakerName = line.SpeakerId;
                }
            }
        }
    }
}
