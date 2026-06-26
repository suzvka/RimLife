## 1. 核心系统与架构
该项目的前端样式完全基于 **RimWorld (Unity) 的 IMGUI 系统**，采用程序化绘制（Immediate Mode）而非声明式 UI。核心架构遵循 **“布局-逻辑分离”** 原则：
- **`ConfigPanelLayout`**: 负责整体三区布局（侧栏导航、内容滚动区、底部状态栏）。
- **`IConfigPage`**: 定义页面接口，每个功能模块（如连接、叙事、调试）实现独立的 `Draw` 逻辑。
- **`UIHelper` & `LayoutHelper`**: 封装所有视觉常量、颜色定义、间距规范及复杂布局算法（如自适应高度卡片、嵌套滚动）。

## 2. 视觉风格与设计令牌 (Design Tokens)
样式系统定义了统一的深色主题（Dark Theme），通过 `UIHelper` 中的静态字段管理设计令牌：

### 颜色体系
- **背景色**: 
  - 侧栏: `Color(0.15, 0.15, 0.15, 0.95)`
  - 内容区: `Color(0.2, 0.2, 0.2, 1.0)`
  - 卡片背景: `Color(0.22, 0.22, 0.22, 1.0)`
  - 选中项: `Color(0.25, 0.25, 0.25, 1.0)`
- **强调色**: 
  - 高亮/激活: `Color(0.35, 0.65, 1.0, 1.0)` (蓝色)
  - 危险/错误: `Color(0.9, 0.3, 0.3, 1.0)` (红色)
  - 成功/在线: `Color(0.3, 0.8, 0.3, 1.0)` (绿色)
- **文本与边框**: 
  - 分隔线: `Color(0.3, 0.3, 0.3, 1.0)`
  - 次要文本: `<color=#888888>` (灰色)

### 间距与尺寸
- **间距常量**: 
  - `GapTiny` (4f): 微间距，用于标签与输入框之间。
  - `GapSmall` (8f): 小间距，同组元素间。
  - `GapMedium` (12f): 中等间距，Section 或卡片间。
  - `GapLarge` (16f): 大间距，页面级区块间。
- **按钮规范**: 
  - 统一高度: `28f`
  - 宽度分级: 小 (80f), 中 (120f), 大 (160f)

## 3. 关键布局模式与约定
### 自适应高度卡片 (Adaptive Cards)
为解决 IMGUI 中动态内容高度难以预测的问题，系统引入了 `LayoutHelper.AdaptiveCardTracker`：
- **双帧收敛**: 第一帧测量内容实际高度，第二帧使用测量值绘制背景，避免内容截断或重叠。
- **视觉边界**: 卡片统一使用 `Widgets.DrawBoxSolid` 绘制背景，并配合 `Widgets.DrawBox` 绘制 1px 边框。

### 滚动区域管理
- **过估算策略**: `ScrollHeightTracker` 使用 `1.2f` 的过估算因子和 `3f` 的最小倍数下限，确保长内容在滚动时不被底部截断。
- **嵌套滚动**: 通过 `LayoutHelper.AllocateSubScrollRegion` 在父滚动区内预留固定高度，实现子区域（如模型列表）的独立滚动。

### CJK 文本补偿
针对 RimWorld 引擎对中文字符高度计算偏低的问题，系统实现了 `CjkHeightCompensation` (1.25f) 因子：
- `UIHelper.CalcTextHeight`: 自动检测 CJK 字符并应用补偿，确保多行中文文本显示完整。

## 4. 组件交互规范
- **状态反馈**: 
  - 操作结果通过 `DrawStatusMessage` 统一展示，持续 5 秒，最后 1 秒淡出。
  - 错误信息以 `[错误]` 开头并显示为红色。
- **分段选择器**: 使用 `DrawSegmentedSelector` 替代原生单选按钮，选中项具有高亮背景和白色粗体文本。
- **Hover 效果**: 列表项通过 `DrawHoverBackground` 提供半透明高亮反馈，增强可点击性感知。

## 5. 开发者指南
1. **新增页面**: 实现 `IConfigPage` 接口，并在 `ConfigPanelLayout` 中注册。
2. **绘制内容**: 必须使用 `Listing_Standard` 进行垂直流式布局，严禁硬编码绝对坐标。
3. **使用 Helper**: 所有间距、颜色、按钮绘制必须调用 `UIHelper` 或 `LayoutHelper` 的方法，禁止直接引用魔法数字或 `Color` 构造函数。
4. **处理动态高度**: 若卡片内容长度不固定，必须使用 `AdaptiveCardTracker` 而非手动估算高度。