using System;
using System.Text;
using Xunit.Abstractions;

namespace RimLife.Tests.Helpers
{
    /// <summary>
    /// 测试基类：为复杂过程的"人在回路"检测提供结构化日志输出。
    /// 简单逻辑测试应直接使用 Assert，不继承此类。
    /// 
    /// 使用方式：
    ///   - Section("标题")     → 打印分隔区块标题
    ///   - Log("label", value) → 打印键值对
    ///   - Log("message")      → 打印单行消息
    ///   - Dump(obj)           → 打印对象的 ToString()
    ///   - LogHeader("title")  → 打印醒目标题
    /// </summary>
    public abstract class LogTestBase
    {
        protected readonly ITestOutputHelper Output;
        private int _stepCounter;

        protected LogTestBase(ITestOutputHelper output)
        {
            Output = output ?? throw new ArgumentNullException(nameof(output));
        }

        /// <summary>打印区块分隔标题。</summary>
        protected void Section(string title)
        {
            Output.WriteLine(string.Empty);
            Output.WriteLine($"═══ {title} ═══");
        }

        /// <summary>打印醒目标题。</summary>
        protected void LogHeader(string title)
        {
            Output.WriteLine(string.Empty);
            Output.WriteLine(new string('━', 60));
            Output.WriteLine($"  {title}");
            Output.WriteLine(new string('━', 60));
        }

        /// <summary>打印单行日志。</summary>
        protected void Log(string message)
        {
            Output.WriteLine($"  {message}");
        }

        /// <summary>打印键值对日志。</summary>
        protected void Log(string label, object value)
        {
            Output.WriteLine($"  {label,-24} │ {value}");
        }

        /// <summary>打印带序号的步骤。</summary>
        protected void Step(string description)
        {
            _stepCounter++;
            Output.WriteLine(string.Empty);
            Output.WriteLine($"  [{_stepCounter}] {description}");
        }

        /// <summary>打印对象的字符串表示。</summary>
        protected void Dump(object obj)
        {
            if (obj == null)
            {
                Output.WriteLine("  (null)");
                return;
            }
            Output.WriteLine($"  {obj}");
        }

        /// <summary>打印集合的每一项。</summary>
        protected void DumpAll<T>(System.Collections.Generic.IEnumerable<T> items, string label = null)
        {
            if (label != null)
                Output.WriteLine($"  {label}:");
            int i = 0;
            foreach (var item in items)
            {
                Output.WriteLine($"    [{i}] {item}");
                i++;
            }
            if (i == 0)
                Output.WriteLine("    (empty)");
        }

        /// <summary>构建带消息的断言异常（不中断测试，仅打印通过/失败）。</summary>
        protected void AssertPass(string message = null)
        {
            Output.WriteLine(message != null ? $"  ✓ PASS: {message}" : "  ✓ PASS");
        }

        protected void AssertFail(string message)
        {
            Output.WriteLine($"  ✗ FAIL: {message}");
        }

        /// <summary>带 Assert 的布尔检查，同时打印日志。</summary>
        protected void Check(bool condition, string description)
        {
            if (condition)
                Output.WriteLine($"  ✓ {description}");
            else
                Output.WriteLine($"  ✗ {description}");
        }

        /// <summary>
        /// 打印分隔线。
        /// </summary>
        protected void Separator()
        {
            Output.WriteLine("  " + new string('─', 40));
        }

        /// <summary>
        /// 重置步骤计数器。
        /// </summary>
        protected void ResetSteps()
        {
            _stepCounter = 0;
        }

        /// <summary>
        /// 将完整日志累积到 StringBuilder 并最终通过一次 Output 输出，
        /// 用于需要整体审查的复杂流程。
        /// </summary>
        protected class LogAccumulator
        {
            private readonly StringBuilder _sb = new StringBuilder();

            public void Line(string text) => _sb.AppendLine(text);
            public void Section(string title) { _sb.AppendLine(); _sb.AppendLine($"═══ {title} ═══"); }
            public void KV(string label, object value) => _sb.AppendLine($"  {label,-24} │ {value}");
            public void Separator() => _sb.AppendLine("  " + new string('─', 40));

            public string Flush()
            {
                var result = _sb.ToString();
                _sb.Clear();
                return result;
            }

            public void WriteTo(ITestOutputHelper output)
            {
                output.WriteLine(_sb.ToString());
                _sb.Clear();
            }
        }
    }
}
