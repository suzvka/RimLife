using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using UnityEngine;
using UnityEngine.UIElements;
using Verse;

namespace RimLife
{
	public enum WordType
	{
		None,           // 未指定
		AGENT,          // 施事者：谁发起动作
		PATIENT,        // 受事者：谁/什么承受动作 
		RECIPIENT,      // 与事者：谁接收（双宾动词需要）
		THEME,          // 客体：被移动/传递的事物 
		PREDICATE,      // 谓语：动作/状态本身
		RESULT,         // 结果：动作导致的终态
		Error           // 错误类型
	}

	// ==========================================================
	// 1. 数学核心：支持向量运算的语义轴
	// ==========================================================
	public struct SemanticAxis
	{
		[XmlAttribute("Valence")] public float Valence { get; set; }	// 效价：这个词在多大程度上倾向表述积极的状态
		[XmlAttribute("Degree")] public float Degree { get; set; }		// 程度：这个词在多大程度上倾向表述强烈的状态
		[XmlAttribute("Dynamic")] public float Dynamic { get; set; }    // 动态：这个词在多大程度上倾向表述正在变化的状态
        public List<string> Tags { get; set; }

        public SemanticAxis(float v, float d, float dyn)
		{
			Valence = v; Degree = d; Dynamic = dyn;
		}

		public SemanticAxis(float value = 0.0f)
		{
			Valence = value; Degree = value; Dynamic = value;
		}

		// 单位向量 (乘法不改变原值)
		public static SemanticAxis One => new SemanticAxis(1.0f);
		// 零向量 (加法不改变原值)
		public static SemanticAxis Zero => new SemanticAxis(0.0f);

		// 向量加法：用于叠加 Bias
		public static SemanticAxis operator +(SemanticAxis a, SemanticAxis b)
			=> new SemanticAxis(a.Valence + b.Valence, a.Degree + b.Degree, a.Dynamic + b.Dynamic);

		// 向量乘法：用于应用 Multiplier
		public static SemanticAxis operator *(SemanticAxis a, SemanticAxis b)
			=> new SemanticAxis(a.Valence * b.Valence, a.Degree * b.Degree, a.Dynamic * b.Dynamic);
	}


	public class Word
	{
		public string Text;
		public List<string> Tags;
		public WordType Type;
		public SemanticAxis Semantics;

		Word()
		{
			Text = string.Empty;
			Tags = new List<string>();
			Type = WordType.None;
			Semantics = SemanticAxis.Zero;
		}

		public static implicit operator bool (Word w)
		{
			return !w.Text.NullOrEmpty();
		}
	}
}
