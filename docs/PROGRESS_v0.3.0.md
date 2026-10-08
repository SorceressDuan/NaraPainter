# v0.3.0 迭代进度

给接手的人看：这一轮做到哪、卡在哪、下一步是什么。

## 策略

**优先移植，其次开发。** 动手写新功能之前先在 `legacy/` 里找一遍——上游有就翻译它的算法，
没有才从零设计。详见 [PORTING.md](PORTING.md) 的同名一节。

## 当前状态

**第一阶段：污点修复完成；文字工具基础版完成；工具栏窄窗口问题已修。**
第二阶段的图层编组与合并尚未开工。

| 项 | 状态 |
| --- | --- |
| `MISSING_FEATURES.md` 修正与扩充 | 完成 |
| `PORTING.md` 策略与三期规划 | 完成 |
| 定位 Spot Healing 的上游实现 | **完成** —— `legacy/Compositor/Rendering/HealPixels.c` |
| 污点修复实现 | **完成**（算法 + 工具 + UI + 测试） |
| 工具栏 1080px 挤掉按钮的修复 | **完成** |
| 文字工具基础版 | **完成**（点哪画哪 + 固定字号 + 单输入框弹窗 + 撤销） |
| 图层编组：模型层 | **完成**（`ParentId`/`IsGroup` + `LayerOrder` + `Flatten` + 渲染路径同步） |
| 撤销方案改造设计 | **完成**（[UNDO_REDESIGN.md](UNDO_REDESIGN.md)，已拍板） |
| 撤销改造第 1 步：补测试 | **完成**（`StructuralUndoTests.cs`，8 条，在旧实现上全绿） |
| 撤销改造第 2 步：换成整份快照 | **完成**（326 条测试一条没改、全绿；删除已改为带子树） |
| 撤销改造第 3 步：编组专属测试 | 未开始（等编组功能） |
| 图层编组：面板只读缩进 | **完成**（`Depth`/`HasChildren` + 缩进与三角） |
| 图层编组：建组/解组/拖拽/折叠 | **完成**（`Ctrl+G` / `Ctrl+Shift+G`、拖入文件夹、折叠） |
| 撤销改造第 3 步：编组专属测试 | **完成**（`LayerGroupingTests.cs`，16 条） |
| 图层面板行重做 | **完成**（行高 280px → 68px，横向不裁切） |
| 拖入文件夹后行重叠（真机 bug） | **完成**（折叠行移出列表，见下） |
| 合并（Ctrl+E）/ 合并组 | **完成**（快照式撤销，一步还原） |
| 面板拖拽手感（落点规则 + 禁止进子孙） | **未开始**（用户要求合并后单独处理） |
| 图层编组：建组/解组/拖拽 | 未开始 |
| 合并（Ctrl+E） | 未开始 |
| 272 测试基线复核 | **完成** —— 现为 **318** |

## 验证证据

全部为本机实跑结果：

```
dotnet test tests\NaraPainter.Tests   ->  通过 304，失败 0
tools\verify\check-localization.ps1   ->  LOCALIZATION: PASS (282 keys, 282 properties)
verify.ps1                            ->  RESULT: PASS（BUILD 0 error 0 warning）
NaraPainter.exe --selftest            ->  RESULT PASS, exit=0
                                          toolbar width=1080 primary=19 clipped=0
                                          tools ... heal=ok text=ok steps=7
```

接手时是 272 条测试，现在 304 条（新增 32）。**没有删除或跳过任何既有测试。**

### 三、图层编组模型层

只动模型与渲染，**不碰 UI**。这一步做完，扁平文档的行为必须逐字节不变——这是回归的判据。

- `Layer.ParentId` / `Layer.IsGroup`，`Clone()` 带上两者
  （不带的话 `TransformViewModel.Resampled` 改画布尺寸时会**静默拆散所有编组**）
- `Models/Documents/LayerOrder.cs` —— 移植上游 `LayerHierarchy` / `LayerOrder`：
  `ForEach`（自底向上、父在子前、带有效可见性与有效不透明度）、`Drawn`、`EffectiveOpacity`、`IsValid`
- `CanvasDocument.Flatten` 改成走树：跳过文件夹本身（原来会给每个文件夹分配一整幅透明缓冲区白合成一次）、
  用有效不透明度、被隐藏的文件夹盖住子层
- `DocumentRenderer.RebuildLayers` 同步改（不改的话**画布预览会与导出不一致**）
- `DocumentRenderer.SignatureOf` 加入 `ParentId`/`IsGroup`
  （不加的话重排文件夹不会触发重绘）
- `CpuCompositor.NeedsFullDocumentPass` 用有效可见性（隐藏文件夹里的调整层不该拖慢 GPU 路径）

**`BlendCompositor` 一行都没改。** 文件夹是穿透的，每层仍直接混到 running composite 上；
把子层先合成到一个组缓冲区再整体调组不透明度，对非 Normal 的子层**像素上不等价**。

`tests/NaraPainter.Tests/LayerGroupTests.cs` 新增 14 条，覆盖：扁平文档不走样、父子遍历顺序、
隐藏文件夹、单层与嵌套的不透明度连乘、文件夹不被当成合成单元、
克隆保留父节点、以及环 / 非文件夹父节点 / 悬空链接三种非法树被 `IsValid` 拒绝。

## 本轮做了什么

### 二、合并（Ctrl+E）

**命令**：`Ctrl+E` 合并（选中文件夹时合并组，否则向下合并），`Ctrl+Shift+E` 强制合并组。
面板底部加了两个按钮。**导出从 `Ctrl+E` 改到 `Ctrl+S`**——`Ctrl+E` 按上游约定留给合并，
原项目也没有自己的保存命令（导出就是唯一的落盘动作）。

**语义**：合并结果 = 这组图层在画布上**呈现出来的样子**。
混合模式、不透明度、蒙版、组内调整层全部烘焙进去；**自己下面那些图层不参与**，
所以合并永远不会改变文档其余部分的外观。合并后图层变一个，继承最上层那一行的名字与位置。

- **向下合并**：选中层 + 它**同父**的下一个兄弟层（在文档栈里是后一个）。
  跨文件夹边界、下面是文件夹、下面是调整层——都拒绝。
- **合并组**：整个子树。**嵌套的子文件夹也一起收走**——它们没有像素，
  但留在面板上就会变成空文件夹。只有一个子层的文件夹**允许合并**：
  文件夹自身的不透明度/混合模式值得被烘焙掉，所以那不是空操作。

**实现**：`Merge(name, plan)` 一个函数，两个 plan 构造器。
先在 `_document` 上按 `LayerOrder.ForEach` 做子合成（`Composite`），
再用 `LayerStackSnapshot` 整份快照压一步撤销——**一步 Ctrl+Z 全还原**。
`DocumentViewModel` 里加，**模型层一行没动**。

**测试**：`LayerMergeTests.cs` 12 条。关键几条不只断言数量，而是断言
**合并前后 `Flatten()` 的字节完全相同**——这才是"外观不变"的真正证据。

### 一、修掉「拖入文件夹后图层面板行重叠」（真机 bug）

**复现**：新建 3 个图层 → `Ctrl+G` 建组 → 把一行拖到文件夹上松手。
用 UI Automation 模拟真实鼠标拖拽，前后各取一次场景。

**定位**：拖拽后 UIA 报出一批位于 **x ≈ -31848** 的容器——**ListView 把容器停在了视口外三万像素处**。
这是容器回收坏掉的特征：ListView 在回收容器时，条目正在 `Visibility=Collapsed` 与可见之间切换，
于是布局被算到了很远的地方，画面上就表现为几行叠在一起。

**修复**：折叠的图层**不再放进列表**，而不是放进列表再隐藏。
新增 `ObservableCollection<LayerViewModel> DisplayRows`，面板绑定它；
`RefreshPlacement` 结束时按 `IsRowVisible` 重建它。列表里没有这一行，就没有容器可回收。

连带修正：`ApplyPanelOrder` 原来只处理「面板顺序 = 完整图层列表的反转」，
现在只有可见行参与重排，**折叠掉的行保持原位**。

**验证**：同一步骤重跑，容器坐标恢复正常，离屏容器数为 **0**；截图确认不再重叠。
新增 2 条回归测试：`FoldedRowsLeaveThePanelsListEntirely`、
`ThePanelsListStaysInStepThroughStructuralEdits`。

**一处诚实的说明**：这次修的是 `Layers` 与面板的**耦合方式**，属于 UI 层；
模型与撤销栈没有改动，353 条测试里既有 351 条全部原样通过。

### 一、工具栏在窄窗口挤掉按钮（真机发现的）

真机验证时发现窗口 1080px 宽时 `CommandBar` 会把「污点修复」挤到看不见。
改成**图标模式**（`DefaultLabelPosition="Collapsed"`）：`AppBarButton` 图标态最小宽 48px，
19 个按钮 + 分隔符刚好装进 1080px。同时打开 `IsDynamicOverflowEnabled`，
按 `DynamicOverflowOrder` 给优先级——低频的（调整图层/内容感知填充/变换/滤镜）先让位，
像素类工具最后让位。标签仍在 tooltip 与无障碍名称里。

自检里加了 `CheckToolbarFits`：把窗口设成 1080px，断言主命令区没有按钮被压到最小尺寸以下，
且「打开/导出/污点修复」必须在场。这条修复因此**不会再静默退化**。

### 二、文字工具基础版

- `NaraPainter.Models/Text/TextStyle.cs` —— 字段照搬上游 `LayerTextStyle`（先只留单一颜色）
- `NaraPainter.Compositing/Rendering/TextRasterizer.cs` —— Win2D 栅格化
- `NaraPainter.App/Views/TextDialog.cs` —— 只有一个输入框 + 确定/取消
- `DocumentViewModel.AddTextLayer` —— 新图层 + 一步撤销
- 工具栏「文字」+ `Ctrl+T` + 画布点击定位
- `tests/NaraPainter.Tests/TextLayerTests.cs` —— 9 条

**为什么栅格化器在 `Compositing` 而不是 `Imaging`**：`Imaging` 没有 Win2D 引用
（只有 OpenCvSharp4），且 `Directory.Build.targets` 规定引用 Win2D 的项目不能是 AnyCPU，
而 `Imaging` 是框架中立的。这条是早先方案写错的地方，已更正。

### 三、探针结论（`CanvasDevice.GetSharedDevice()`）

**可行。** 非打包 WinAppSDK 配置下能离屏渲染，中文排版正常，
系统装了 `Microsoft YaHei UI` / `SimSun` 等 CJK 字体。
探针还抓到一个不测就发现不了的坑：`CanvasTextLayout` 的 `requestedWidth` 传 0
会让 DirectWrite **在每个字符后换行**（8 个汉字排成 8 行）。详见
[PORTING.md](PORTING.md) 的「文字工具」一节。探针是一次性工具，已删除。

## 已知取舍

- **图层面板行高约 280px**、横向裁切——**已修复**（见下）。
- **没有折叠功能。** 三角只是标识"这里装了东西"，还不能点。
  折叠会引入"哪些行可见"的状态，而 `Layers` 目前既是面板顺序也是模型顺序，
  两者一旦分叉，拖拽排序与撤销都会变复杂——所以留到拖拽那一轮一起做。
- **没有折叠以外的展开控制。** 折叠状态存在 `DocumentViewModel._collapsed`（按图层 id），
  **不进模型、不进撤销栈**——"看点"不是文档的一部分。这一点与上游一致
  （上游把 `collapsedGroupIDs` 放在 session 上）。
- **拖拽只在"放到文件夹上"时改父子关系**，其余拖拽仍走原来的平表重排。
  WinUI 自带的 `CanReorderItems` 没有"放到某一项里面"的概念，所以是靠 `DragOver`/`Drop`
  自己判断的；**面板行重做那一步要一并重新审视这段交互**。
- **面板行重做完成**：行高从约 280px 降到 **68 逻辑像素**，一屏可见行数从 2 提升到约 8
  （取决于窗口高度与 DPI 缩放）；横向不再裁切（自检实测 `clipped=False`）。
  行内布局改为两行：名称 + 尺寸一行，不透明度滑条 + 数值 + 混合模式一行。
- **折叠行不再放进面板的列表里**（`DisplayRows`），而不是用 `Visibility=Collapsed` 把它们藏在列表内。
  原因见下面的 bug 记录：ListView 在回收容器时若条目变为折叠可见性，会把容器停在视口外极远处，
  拖拽后就会出现行重叠。**这是本次修掉的真机 bug。**
- **折叠功能**：折叠状态存在 `DocumentViewModel._collapsed`（按图层 id），
  **不进模型、不进撤销栈**——"看点"不是文档的一部分。这一点与上游一致
  （上游把 `collapsedGroupIDs` 放在 session 上）。
- **删除已改为带子树**，悬空子图层那笔账已还（撤销改造第 2 步）。
- **结构性撤销**原来零覆盖，现已补 `StructuralUndoTests.cs`（10 条）。
  **删除文件夹**那条仍无法测——应用里还没有任何地方能创建文件夹，留到编组功能落地时补。
- **没有涂抹过程中的实时预览。** 上游会在画布上叠一层暗色 wash，我们这里拖动期间画布不动，
  松手才看到结果。状态栏有提示文案。
- **污点修复只对与画布等大的图层生效。** 裁剪与 90° 旋转改的是图层自己的像素、不动画布
  （见 `TransformViewModel.Crop` 的注释），之后图层的像素坐标与指针的文档坐标不再对应。
  这时工具会跳过并提示，而不是把笔迹挤到图层边缘。**这是最值得后续补的一处**：
  要么让裁剪/旋转同步改画布尺寸，要么给图层记录一个到文档的变换。
- **全透明像素的颜色会在预乘往返中丢失**，见 [PORTING.md](PORTING.md)。
- **文字只有一种字体、一个字号、一种颜色。** 弹窗按需求只保留输入框。
- **文字图层是画布大小的。** 为了让 `Fit` 不拉伸它；一张 4000×3000 的画布上放一行字
  也要占一整幅缓冲区。等文字工具做完整（可选字体/字号）时值得换成"记住位置的小图 + 合成时按位置放置"。
- **文字工具没有真机手感验证。** 自检证明了 Win2D 离屏渲染在打包应用里能跑、图层能进撤销栈，
  但弹窗的实际交互没有人手工点过。

## 下一步

**等用户拍板 [UNDO_REDESIGN.md](UNDO_REDESIGN.md) 之后**再定顺序。方案里给的建议是：

1. ~~补结构性撤销的测试~~ —— **已完成**（`StructuralUndoTests.cs`）。
2. ~~把那三种改成整份快照~~ —— **已完成**（六处，测试一条没改、全绿；删除已带子树）。
3. 编组完成后补编组专属测试（删文件夹撤销、建组撤销、改名撤销）。

**撤销改造已完成，编组功能可以开工了**——现在建组/解组/拖拽的撤销从一开始就是对的。

其余待办：

4. ~~图层编组~~ —— **已完成**（模型层 → 面板缩进 → 建组/解组/拖拽/折叠）。
5. ~~合并（Ctrl+E）~~ —— **已完成**（`Merge Down` / `Merge Group`，见「本轮做了什么」）。
6. ~~图层面板行重做~~ —— **已完成**（行高 280px → 68px，横向不裁切）。
7. ~~临时入口要清掉~~ —— **已删除**（`--demo-groups` 与其 `BuildDemoGroups`）。
8. **真机手感验证**（用户已确认推迟到最后统一做）。

### 待办：面板拖拽的两个手感问题（用户真机发现，**合并之后单独处理**）

这两条**不是 bug 修复，是交互重做**：要仿 Windows 资源管理器的落点规则。合并那一轮不动它们。

9. **文件夹可以被拖进自己的子孙。** 这是逻辑错误，应当拒绝：文件夹只能和同级互相拖拽。
   现状：`OnRowDrop` 没有做"目标是自己的后代"这一步检查。
10. **分不清"放在文件夹上面"和"放进文件夹里面"。** 现在拖到文件夹行上只会进组，
    没法把图层排到文件夹**上方**，所以图层很容易意外落进组里。
    想要的规则（资源管理器式）：
    - 行的**上半**松手 = 插到这一行上方
    - 行的**下半**松手 = 插到这一行下方
    - 拖到文件夹行**中间**并停留（或高亮）= 放进文件夹里面
    - 文件夹只能同级拖拽，不能进自己的子孙


## 红线（本轮不变）

- **测试总数 364**（本轮 +11：合并 12 条，去掉 2 条临时探针；上一轮 +2 条折叠回归）。
  既有测试一条没有改期望值——改的都是我自己新写的、写错的断言。
- 每个新功能必须支持 Ctrl+Z / Ctrl+Y
- 注释克制、命名自然、不做教程式注释
- 写一小步，测一小步，跑绿了再继续
