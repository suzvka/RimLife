using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RimLife
{
    public class WordRule
    {
        public static readonly Dictionary<string, SemanticRole> Rules = BuildRules();
        public List<string> Tags { get; set; }

        private static Dictionary<string, SemanticRole> BuildRules()
        {
            var rules = new Dictionary<string, SemanticRole>(StringComparer.OrdinalIgnoreCase);

            void Map(SemanticRole type, params string[] aliases)
            {
                foreach (var alias in aliases) rules[alias] = type;
            }

            // 施事者 (Agent): 动作的发起者
            Map(SemanticRole.AGENT, "Agent", "Actor", "Subject", "Doer", "Source", "SUBJ", "SBJ");

            // 受事者 (Patient): 动作的承受者
            Map(SemanticRole.PATIENT, "Patient", "Object", "DirectObject", "Target", "OBJ", "DOBJ");

            // 与事者 (Recipient): 动作的接收者
            Map(SemanticRole.RECIPIENT, "Recipient", "IndirectObject", "Receiver", "IOBJ");

            // 客体 (Theme): 被移动或传递的物体
            Map(SemanticRole.THEME, "Theme", "Topic");

            // 谓语 (Predicate): 动作或状态
            Map(SemanticRole.PREDICATE, "Predicate", "Verb", "Action", "State", "PRED");

            // 结果 (Result): 动作导致的终态
            Map(SemanticRole.RESULT, "Result", "Outcome");

            return rules;
        }
    }

    public class WordSlot
    {
        public string RoleName { get; }      // 如 "Attacker"
        public SemanticRole SourceType { get; }  // 如 WordType.SubjectNp (从 SUBJECT_Label 解析)
        public SemanticAxis Multiplier { get; set; }

        public WordSlot(string roleName, string sourceTypeName, SemanticAxis multiplier)
        {
            RoleName = roleName;
            // 这里把 sourceTypeName (XML里的 type属性) 解析为枚举
            SourceType = ResolveType(sourceTypeName);
            Multiplier = multiplier;
        }

        private static SemanticRole ResolveType(string name)
        {
            if (WordRule.Rules.TryGetValue(name, out var typeFromRule))
            {
                return typeFromRule;
            }

            if (Enum.TryParse(name, ignoreCase: true, out SemanticRole typeFromEnum))
            {
                return typeFromEnum;
            }

            return SemanticRole.Error;
        }
    }
}
