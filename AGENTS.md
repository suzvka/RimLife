## 项目概述

RimLife 是一个 RimWorld 游戏模组（Mod），基于 LLM 实现游戏内剧情设计与增强。通过预处理底层数据、结构化剧情模板，配合大语言模型生成叙事内容。

## 技术栈

- 语言: C#
- 运行时: .NET (RimWorld 1.6 / Unity)
- 构建: MSBuild (`RimLife.csproj`, `RimLife.sln`)
- 测试: xUnit (`RimLife.Tests/`)
- UI 框架: RimWorld IMGUI (Verse.Widgets / Listing_Standard)
- LLM 集成: OpenAI 兼容 / Anthropic API

## 目录结构

```
Source/
  UI/           - 配置面板 UI（ConfigPanelWindow, ConfigPanelLayout, Pages/, Helpers/, Models/）
  Framework/    - 框架层（AgentPipeline, LLM, MCP, Script, EventBus 等）
  Data/         - 数据层（PawnPro, EnvironmentPro, Colony, Events）
  Infrastructure/ - 基础设施（LlmAccessor, Knowledge, MCP providers, SaveStore）
  Core/         - 核心接口（ILlmService, IKnowledgeBase 等）
  Agent/        - Agent 循环
  Mappers/      - Card 映射器
  Cards/        - 数据结构（EventCard, CharacterCard 等）
  Settings/     - Mod 设置
  Tool/         - 自检工具
Defs/           - RimWorld XML 定义
Patches/        - XML 补丁
Languages/      - 多语言（ChineseSimplified）
Libs/           - 外部依赖 DLL
1.6/Assemblies/ - RimWorld 1.6 兼容 DLL
```

## 关键入口 / 核心模块

- `Source/UI/ConfigPanelWindow.cs` - 浮动配置面板窗口入口
- `Source/UI/ConfigPanelLayout.cs` - 三区布局（侧栏 + 内容区 + 状态栏）
- `Source/UI/Helpers/UIHelper.cs` - UI 绘制辅助工具（间距/颜色/按钮/卡片/分段选择器等统一原语）
- `Source/UI/Pages/` - 各配置页面（Connection, Narrative, Knowledge, Advanced, Debug）
- `Source/UI/Models/LlmCredentialManager.cs` - LLM 凭证管理器（单例，全局持久化）
- `Source/Infrastructure/RimLifeCore.cs` - 核心服务入口
- `Source/Framework/AgentPipeline.cs` - Agent 管线

## 运行与预览

- 本项目为 RimWorld Mod，无法独立运行或预览
- 构建: `dotnet build RimLife.sln`
- 测试: `dotnet test RimLife.Tests/`
- 部署: 将编译产物放入 RimWorld Mods 目录

## 用户偏好与长期约束

- UI 使用统一的布局原语（UIHelper 中的间距常量、按钮尺寸常量、DrawButtonRow、DrawSegmentedSelector 等）
- 按钮尺寸必须使用 UIHelper 中的常量（BtnWidthSmall/Medium/Large, BtnHeight），禁止硬编码像素值
- 删除操作必须使用两步确认（首次点击标记待删除态，再次点击执行）
- API Key 编辑时必须提供显示/隐藏切换，默认脱敏显示
- 状态消息最后 1 秒应有淡出效果
- 侧栏导航项必须有 hover 反馈
- 卡片必须有四边完整边框（Widgets.DrawBox）

## 常见问题和预防

- `Text.CalcSize()` 需要在绘制上下文中调用，不能在非 GUI 线程使用
- `Widgets.DrawBox` 绘制的是 1px 边框线，`Widgets.DrawBoxSolid` 绘制实心矩形
- RimWorld IMGUI 是即时模式，每帧重绘，状态需要手动维护
- `Listing_Standard` 的 `GetRect()` 会自动推进游标，不需要手动计算 Y 坐标
