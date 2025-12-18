using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RimLife
{
    public class WordRule
    {
        public static readonly Dictionary<string, WordType> Rules = BuildRules();
        public List<string> Tags { get; set; }

        private static Dictionary<string, WordType> BuildRules()
        {
            var rules = new Dictionary<string, WordType>(StringComparer.OrdinalIgnoreCase);

            void Map(WordType type, params string[] aliases)
            {
                foreach (var alias in aliases) rules[alias] = type;
            }

            // 施事者 (Agent): 动作的发起者
            Map(WordType.AGENT, "Agent", "Actor", "Subject", "Doer", "Source", "SUBJ", "SBJ");

            // 受事者 (Patient): 动作的承受者
            Map(WordType.PATIENT, "Patient", "Object", "DirectObject", "Target", "OBJ", "DOBJ");

            // 与事者 (Recipient): 动作的接收者
            Map(WordType.RECIPIENT, "Recipient", "IndirectObject", "Receiver", "IOBJ");

            // 客体 (Theme): 被移动或传递的物体
            Map(WordType.THEME, "Theme", "Topic");

            // 谓语 (Predicate): 动作或状态
            Map(WordType.PREDICATE, "Predicate", "Verb", "Action", "State", "PRED");

            // 结果 (Result): 动作导致的终态
            Map(WordType.RESULT, "Result", "Outcome");

            return rules;
        }
    }

    public class WordSlot
    {
        public string RoleName { get; }      // 如 "Attacker"
        public WordType SourceType { get; }  // 如 WordType.SubjectNp (从 SUBJECT_Label 解析)
        public SemanticAxis Multiplier { get; set; }

        public WordSlot(string roleName, string sourceTypeName, SemanticAxis multiplier)
        {
            RoleName = roleName;
            // 这里把 sourceTypeName (XML里的 type属性) 解析为枚举
            SourceType = ResolveType(sourceTypeName);
            Multiplier = multiplier;
        }

        private static WordType ResolveType(string name)
        {
            if (WordRule.Rules.TryGetValue(name, out var typeFromRule))
            {
                return typeFromRule;
            }

            if (Enum.TryParse(name, ignoreCase: true, out WordType typeFromEnum))
            {
                return typeFromEnum;
            }

            return WordType.Error;
        }
    }
}
