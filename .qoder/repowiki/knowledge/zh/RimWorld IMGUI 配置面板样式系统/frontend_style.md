## 1. 核心系统与架构
本项目采用 **RimWorld 原生 IMGUI (Immediate Mode GUI)** 体系构建配置界面，未引入 Web 前端技术栈（如 CSS/HTML）。UI 逻辑严格遵循 **MVVM 变体** 或 **组件化** 设计，通过 `ConfigPanelLayout` 统一管控布局，`IConfigPage` 接口实现页面多态，`UIHelper` 与 `LayoutHelper` 提供原子级样式与布局能力。

### 架构分层
- **布局容器层 (`ConfigPanelLayout`)**: 负责“侧栏导航 + 内容区 + 状态栏”的三区固定布局。管理页面路由、滚动位置重置及全局状态栏渲染。
- **页面抽象层 (`IConfigPage`)**: 定义 `Draw(Rect, Listing_Standard)` 契约。各功能页（连接、叙事、提示词等）实现此接口，确保视觉风格一致。
- **样式原子层 (`UIHelper`)**: 集中管理颜色常量、间距规范、RichText 格式化及 CJK 文本高度补偿逻辑。
- **布局算法层 (`LayoutHelper`)**: 解决 IMGUI 中常见的滚动高度截断问题，提供 `ScrollHeightTracker`（过估算策略）和 `AdaptiveCardTracker`（双帧收敛自适应高度）。

## 2. 视觉设计规范 (Design Tokens)

### 色彩体系 (Dark Theme)
所有颜色均定义为 `UnityEngine.Color`，采用深色系以契合 RimWorld 游戏内氛围：
- **背景色**: 
  - 侧栏: `RGBA(0.15, 0.15, 0.15, 0.95)`
  - 内容区: `RGBA(0.2, 0.2, 0.2, 1.0)`
  - 卡片背景: `RGBA(0.22, 0.22, 0.22, 1.0)`
  - 状态栏: `RGBA(0.12, 0.12, 0.12, 1.0)`
- **交互色**:
  - 选中项背景: `RGBA(0.25, 0.25, 0.25, 1.0)`
  - 悬停高亮: `RGBA(0.22, 0.22, 0.22, 1.0)`
  - 强调色 (Highlight): `RGBA(0.35, 0.65, 1.0, 1.0)` (用于激活态指示器、分段选择器)
- **状态语义色**:
  - 成功/运行: `RGBA(0.3, 0.8, 0.3)` (绿色)
  - 错误/失败: `RGBA(0.9, 0.3, 0.3)` (红色)
  - 闲置/未测试: `RGBA(0.5, 0.5, 0.5)` (灰色)
  - 进行中: `RGBA(0.35, 0.65, 1.0)` (蓝色)

### 间距与尺寸规范
- **间距常量**: 
  - `GapTiny` (4f): 标签与输入框间、列表项间。
  - `GapSmall` (8f): 同组元素间、卡片内边距。
  - `GapMedium` (12f): Section 之间、卡片之间。
  - `GapLarge` (16f): 页面级区块间。
- **按钮规范**: 
  - 统一高度 `BtnHeight` = 28f。
  - 宽度分级: `Small` (80f), `Medium` (120f), `Large` (160f)。

### 字体与排版
- **RichText 支持**: 广泛使用 `<size>`, `<color>`, `<b>` 标签进行微调。
- **标题层级**: 
  - 页面标题: `<size=18><b>`
  - Section 标题: `<size=14><b>── {title} ──</b></size>`
  - 卡片标题: `<size=14><b>`
  - 描述文本: `<color=#888888><size=12>`
- **CJK 高度补偿**: 针对 Unity/RimWorld 引擎对中文字符高度估算偏低的问题，引入 `CjkHeightCompensation = 1.25f` 因子。`UIHelper.CalcTextHeight` 会自动检测 CJK 字符并应用补偿，防止文本截断。

## 3. 关键组件与模式

### 卡片式布局 (Card Pattern)
- **实现**: `UIHelper.BeginCard` 与 `LayoutHelper.AdaptiveCardTracker`。
- **特征**: 深色背景 + 1px 边框。支持自适应高度，通过双帧测量（Frame N 测量，Frame N+1 应用）消除滚动条抖动。
- **应用场景**: 凭证管理卡片、模型列表容器、配置分组。

### 分段选择器 (Segmented Selector)
- **实现**: `UIHelper.DrawSegmentedSelector`。
- **特征**: 替代传统的 Radio Button。选中项具有半透明强调色背景和高亮文字，未选中项为普通按钮样式。

### 状态反馈机制
- **即时状态**: 使用圆形指示器 `●` 配合颜色变化（绿/灰/红）。
- **临时消息**: `UIHelper.DrawStatusMessage` 实现“显示 5 秒，最后 1 秒淡出”的统一反馈效果。错误消息以 `[错误]` 前缀触发红色渲染。

### 滚动高度追踪 (Scroll Height Tracking)
- **痛点解决**: 传统 IMGUI 常因内容高度计算不准导致底部内容被截断或滚动条闪烁。
- **方案**: `LayoutHelper.ScrollHeightTracker` 采用 `OvershootFactor = 1.2f` 过估算策略，并设置 `MinScrollMultiplier = 3f` 下限，确保小内容页面也有合理的滚动体验。

## 4. 开发者约束与最佳实践

1. **禁止硬编码魔法数字**: 所有间距、颜色、尺寸必须引用 `UIHelper` 或 `ConfigPanelLayout` 中的常量。
2. **CJK 文本处理**: 涉及中文显示的 `Label` 或 `TextField`，必须使用 `UIHelper.CalcTextHeight` 或 `UIHelper.AutoHeightLabel` 而非原生 `Text.CalcHeight`。
3. **页面扩展**: 新增配置页需实现 `IConfigPage` 接口，并在 `ConfigPanelLayout` 构造函数中注册。页面内部应使用 `Listing_Standard` 进行流式布局。
4. **异步操作 UI 反馈**: 在触发异步任务（如 API 测试、模型获取）时，必须立即更新按钮状态为“进行中...”并禁用重复点击，任务结束后恢复。
5. **状态持久化**: 页面控件应直接读写 `RimLifeCore` 或 `CredentialManager` 中的真实状态，或使用本地缓冲（如 `_editBuffers`）在用户点击“保存”后一次性提交。