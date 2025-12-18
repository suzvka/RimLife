using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace RimLife
{
	public enum DiscourseFunction // 话语功能
	{
		Independent,    // 独立片段
		Intro,          // 引入
		Elaboration,    // 阐述
		Contrast,       // 对比
		Result          // 结果
	}

	public enum SyntacticType // 句法类型
	{
		Full,			// 完整句子
		Fragment,       // 片段
		Modifier        // 修饰语
	}

	// ==========================================================
	// 3. Runtime 层：负责解析与计算 (业务逻辑)
	// ==========================================================

	public class Sentence
	{
		// 运行时状态
		public string TemplateId { get; set; }
		public DiscourseFunction Function { get; set; }
		public SyntacticType Syntactic { get; set; }
		public List<string> Tags { get; set; }
		public string rawText { get; set; }

		// 解析后的结构
		public List<string> TemplateTextSegments { get; private set; } // 静态文本片段
		public List<WordSlot> Slots { get; private set; }              // 动态槽位列表

		// 计算参数
		public SemanticAxis Bias { get; private set; }

		public Sentence() { }

		// 初始化：解析文本模板，建立槽位映射
		public void Initialize(SentenceTemplateDto templateDto)
		{
			Bias = templateDto.Bias;
			TemplateTextSegments = new List<string>();
			Slots = new List<WordSlot>();

			var slotDefs = templateDto.Slots?.ToDictionary(s => s.RoleName) ?? new Dictionary<string, SlotDefinitionDto>();

			// 正则：匹配 {RoleName}
			var regex = new Regex(@"\{([a-zA-Z0-9_]+)\}");
			var lastIndex = 0;

			foreach (Match match in regex.Matches(rawText))
			{
				// 1. 收集前置静态文本
				TemplateTextSegments.Add(rawText.Substring(lastIndex, match.Index - lastIndex));

				// 2. 获取槽位逻辑名 (如 "Attacker")
				var roleName = match.Groups[1].Value;

				// 3. 查找配置并创建 Slot
				if (slotDefs.TryGetValue(roleName, out var def))
				{
					// 正常情况：使用配置的类型和乘子
					Slots.Add(new WordSlot(roleName, def.SourceTypeString, def.Multiplier));
				}
				else
				{
					// 容错情况：模板里写了 {X} 但 XML 没配。
					// 默认：当作通用名词，乘子为 (1,1,1) 不改变语义
					Slots.Add(new WordSlot(roleName, "NOUN_General", SemanticAxis.One));
				}

				lastIndex = match.Index + match.Length;
			}

			// 收集剩余文本
			TemplateTextSegments.Add(rawText.Substring(lastIndex));
		}

		// 核心方法：仿射变换计算 (Affine Transformation)
		// Result = Bias + Sum(SlotWord * SlotMultiplier)
		public SemanticAxis Evaluate(List<Word> filledWords)
		{
			if (filledWords == null || filledWords.Count != Slots.Count)
				return Bias; // 异常或空填充时，至少返回句子的"底色"

			var totalVector = Bias; // 起点

			for (int i = 0; i < Slots.Count; i++)
			{
				var wordVec = filledWords[i].Semantics;
				var multiplier = Slots[i].Multiplier;

				// 向量逐项积：应用介词/逻辑算子对词义的扭曲
				// e.g. "against" (Multiplier: -1) * "Evil" (Word: -1) = +1 (Heroic)
				var weightedVec = wordVec * multiplier;

				// 向量加法：叠加到总意象
				totalVector += weightedVec;
			}

			return totalVector;
		}

		// 新增方法：用给定的词填充模板，生成最终文本
		public string Fill(List<Word> filledWords)
		{
			if (filledWords == null || filledWords.Count != Slots.Count)
			{
				// 在无法正确填充时返回原始模板作为容错
				return rawText;
			}

			var sb = new StringBuilder();
			for (int i = 0; i < TemplateTextSegments.Count; i++)
			{
				// 1. 添加静态文本片段
				sb.Append(TemplateTextSegments[i]);

				// 2. 如果后面还有槽位，则填充词语
				if (i < Slots.Count && filledWords[i])
				{
					sb.Append(filledWords[i].Text); 
				}
			}
			return sb.ToString();
		}
	}

	// ==========================================================
	// 2. DTO 层：负责 XML 数据的映射 (配置数据)
	// ==========================================================

	// 对应 XML 结构：
	// <Sentence id="..." function="Result" syntactic="FullSentence">
	//    <Text>...</Text>
	//    <Bias .../>
	//    <Slots>...</Slots>
	// </Sentence>
	[XmlRoot("Sentence")]
	public class SentenceTemplateDto
	{
		[XmlAttribute("ID")]
		public string Id { get; set; }

		// 句子整体的固有属性 (话语功能 & 句法类型)
		// 使用 string 方便 XML 读写，运行时解析为 Enum
		[XmlAttribute("Function")]
		public string DiscourseFunctionStr { get; set; }

		[XmlAttribute("Syntactic")]
		public string SyntacticTypeStr { get; set; }

		// [DEPRECATED] public string Text { get; set; }
		
		// 新增：话题标签
		[XmlArray("Tags")]
		[XmlArrayItem("li")]
		public List<string> Tags { get; set; }

		// 新增：捕获所有语言的文本节点
		[XmlAnyElement]
		public XmlElement[] LocalizedTexts { get; set; }

		// 偏置：句子的"底色" (基准向量)
		[XmlElement("Bias")]
		public SemanticAxis Bias { get; set; }

		// 槽位定义列表
		[XmlArray("Slots")]
		[XmlArrayItem("Slot")]
		public List<SlotDefinitionDto> Slots { get; set; }

		// 工厂方法：将静态配置转换为运行时对象
		public Sentence ToSentence(string currentLanguage)
		{
			var sentence = new Sentence();
			// 1. 选择文本
			sentence.rawText = null;
			if (LocalizedTexts != null && LocalizedTexts.Length > 0)
			{
				// a. 尝试匹配当前语言
				var textNode = LocalizedTexts.FirstOrDefault(node => node.Name.Equals(currentLanguage, StringComparison.OrdinalIgnoreCase));
				if (textNode != null)
				{
					sentence.rawText = textNode.InnerText;
				}
				else
				{
					// b. 回退机制：使用第一个定义的语言
					sentence.rawText = LocalizedTexts[0].InnerText;
				}
			}

			// 如果没有可用的文本模板，则无法创建句子
			if (string.IsNullOrEmpty(sentence.rawText))
			{
				// RimWorld-style logging would be better here, e.g. Log.Error(...)
				return null;
			}

			// 2. 基础属性映射
			sentence.TemplateId = this.Id;
			sentence.Tags = this.Tags ?? new List<string>(); // 确保 Tags 列表不为 null

			// 3. 枚举安全解析
			Enum.TryParse(this.DiscourseFunctionStr, true, out DiscourseFunction func);
			sentence.Function = func;

			Enum.TryParse(this.SyntacticTypeStr, true, out SyntacticType syn);
			sentence.Syntactic = syn;

			// 4. 构建查找字典 (RoleName -> Definition)
			// 允许模板里定义空 Slots 列表 (纯静态句子)
			var slotMap = Slots?.ToDictionary(s => s.RoleName) ?? new Dictionary<string, SlotDefinitionDto>();

			// 5. 初始化解析
			sentence.Initialize(this);

			return sentence;
		}
	}

	// 对应 XML: <Slot Name="Attacker" Type="SUBJECT_Label"> <Multiplier .../> </Slot>
	public class SlotDefinitionDto
	{
		[XmlAttribute("Name")]
		public string RoleName { get; set; } // 逻辑角色 (如 "Attacker")

		[XmlAttribute("Type")]
		public string SourceTypeString { get; set; } // 数据源类型 (如 "SUBJECT_Label")

		[XmlElement("Multiplier")]
		public SemanticAxis Multiplier { get; set; } // 算子：如何扭曲填入词的语义
	}
}
