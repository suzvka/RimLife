using System;
using System.Collections.Generic;
using Verse;

namespace RimLife
{

    /// <summary>
    /// 槽位实现器：直接返回最终要插入模板中的字符串。
    /// 
    /// - 对 SubjectNp 这类槽，可以绕过词汇表，直接从 PawnPro 生成“我/他/名字”。
    /// - 对 DomainNp，可以组合所有格 + 领域名词（“我的” + “伤口”）。
    /// </summary>
    public interface ISlotRealizer
    {
        string RealizeSlot(
            SlotRequest slot,
            Fact fact);
    }
}
