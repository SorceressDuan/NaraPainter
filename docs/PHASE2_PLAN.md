# 第二阶段技术方案：文字工具与图层编组

上游 Swift 源码在 `legacy/`，本轮**只做方案，不写代码**。两份方案的共同前提是
[MISSING_FEATURES.md](MISSING_FEATURES.md) 里的定位不变：轻量级编辑器，不做 PSD 往返。

---

## 一、基础文字工具

### 结论先行

**上游的实现不能移植，只能照它的数据模型重写排版层。** 原因是排版引擎换了：
上游 `legacy/Compositor/Document/TypeTool.swift` 是 472 行，基于 AppKit 的
`NSAttributedString` + CoreText 做行内编辑、段落框、字偶距、按字符的颜色/字体分段；
Windows 侧没有对应物，只能换成 Win2D / DirectWrite。这一项是**重新开发**。

### 上游给了什么、没给什么

| 内容 | 能否直接用 |
| --- | --- |
| `LayerTextStyle` 字段集（内容、字体名、字号、RGB、对齐、字距、行距） | **能**，字段定义直接照搬 |
| `leading` 语义：0 表示 Auto = 字号 × 1.2 | **能**，这条公式照抄 |
| `boxSize` 段落框、`padding = 12`、`boxIsValid` 的取值范围校验 | **能**，校验逻辑照搬 |
| `colorRuns` / `fontRuns` 按字符分段 | 本轮**不做**，但字段预留方式可以照它的形状 |
| CoreText 排版与行内编辑 | **不能**，换 DirectWrite |

上游是本项目的委托范围之外的 macOS 26 / SwiftUI 项目，其 `TypeTool.swift`
在 `legacy/Compositor/Document/TypeTool.swift`，只读参考。

### 建议的落地路径

分三小步，每步独立跑绿：

**第 1 步：模型与栅格化，不碰 UI。**
新增 `NaraPainter.Models/Text/TextStyle.cs`，字段照抄上游 `LayerTextStyle`（但先砍掉
`colorRuns` / `fontRuns`，只留单一颜色）。新增
`NaraPainter.Compositing/Rendering/TextRasterizer.cs`，把 `TextStyle` 栅格化成 `PixelBuffer`。
**这一步可以完全用单元测试驱动，不需要窗口。**

> **栅格化器不能放在 `NaraPainter.Imaging`。** 那里没有 Win2D 引用（只有
> OpenCvSharp4），而且 `Directory.Build.targets:19-25` 规定引用 Win2D 的项目
> 不能是 AnyCPU——`Imaging` 是框架中立的。放 `Compositing` 或 `App`。
> 这条是本方案早先版本写错的地方，已更正。

关键 API 事实（已核对本仓库）：

- `Microsoft.Graphics.Win2D` **1.4.0**，`NaraPainter.App.csproj:30` 与
  `NaraPainter.Compositing.csproj:25` 都已引用。托管投影程序集是
  `Microsoft.Graphics.Canvas.Interop.dll`，`using Microsoft.Graphics.Canvas.Text;` 指的就是它。
- 仓库里**目前没有任何** `CanvasTextFormat` / `CanvasTextLayout` / `DrawText` 调用，
  这是第一次引入文字渲染。
- **`NuGet.config` 只挂了 `.tools\local-feed` 一个离线源**，所以不要打算新增 NuGet 包。
- `CanvasRenderTarget` 已经在用，而且**就是离屏用法**：
  `CanvasView.cs:349-367` 的 `CreateCheckerBrush` 拿一个 `CanvasDevice` 直接
  `new CanvasRenderTarget(device, w, h, 96)`，再用
  `using (CanvasDrawingSession session = tile.CreateDrawingSession()) { ... }` 画进去。
  文字栅格化照这个形状写即可，不需要活动画布。
  `CanvasRenderTarget` 继承自 `CanvasBitmap`，所以 `GetPixelBytes()` /
  `GetPixelBytes(left, top, w, h)` 都能用——但**仓库里至今没有任何一处读回像素**
  （`GetPixelBytes` / `GetPixelColors` 全仓库 0 命中），这条路是全新的。
- **读回来的是预乘 BGRA**（默认渲染目标就是 `B8G8R8A8UIntNormalized` + 预乘）。
  转成 `PixelBuffer` 要**换通道序 + 反预乘**。转换只写一份。
- 取字体列表用 `CanvasTextFormat.GetSystemFontFamilies()`，**不要硬编码字体名**：
  `App.xaml:19` 只声明了一个 `Microsoft YaHei UI`，而且中文串会让本地化 Gate 4 直接失败。
- 量文字尺寸用 `CanvasTextLayout.LayoutBounds` / `DrawBounds`，据此决定栅格目标大小。

**第 2 步：对话框。**
`ContentDialog` 收集文本、字体、字号、颜色、对齐。照 `MainWindow.xaml.cs:326-343`
的导出对话框写（代码构造、`XamlRoot = Content.XamlRoot`、返回一个元组或 null）。
两个现成的坑：

- **对话框在窗口视觉树外，`FontFamily` 要单独设。** 三个现有对话框都从
  `AppFontFamily` 重新设一遍（`:336-339`、`:493-496`、`:961-964`）。
- 仓库里**没有任何带 `TextBox` 的对话框**，这是第一个。

**第 3 步：接入图层。**
新增一个 `AddTextLayer` 动作：栅格化 → 落到新图层 → 压一步撤销
（`DelegateAction`，与 `DocumentViewModel.AddLayer`（`:192-202`）一致）。

> **注意定位问题。** `ImportLayer` 会把非画布尺寸的图层**拉伸**到画布
> （`DocumentViewModel.cs:226` 的 `_document.Fit`，`DocumentRenderer.cs:241` 同理）。
> 所以文字不能只栅格化成一个 tight 的文本框贴上去，否则会被拉满整个画布。
> 要么栅格化成画布大小、把文字画在指定位置，要么先给图层加「位置」概念——
> **这是本方案尚未解决的一处**，第 1 步就要定下来。


### 必须先在代码里验证的三件事

这三点没验证之前不要写第 1 步的排版代码：

1. **中文能不能排版。** 项目没有自带字体资源，只声明了一个
   `App.xaml:19` 的 `Microsoft YaHei UI`，靠 DirectWrite 逐字回退。
   先写一个一次性探针，用 `CanvasTextLayout` 排一串中文，确认不是豆腐块、
   确认 `LayoutBounds` 合理。**这是本项最大的未知数**，验证不过的话整个文字工具的
   形态要重新考虑。
2. **CanvasDevice 从哪来。** `CanvasRenderTarget` 的构造需要一个
   `ICanvasResourceCreator`。仓库里唯一用 `CanvasDevice` 的地方是
   `CanvasView.CreateCheckerBrush`，而它是**在 `OnDraw` 里拿 `session.Device`**——
   也就是设备来自画布绘制过程。工具动作要在绘制之外跑，所以只有两条路：
   - 从 `CanvasView` 把 `CanvasControl` 的设备**暴露出来**（现在它是私有的，
     `docs/modules/COMPOSITING.md:23` 明确写了「拿不到」）；
   - 或者用 `CanvasDevice.GetSharedDevice()`（1.4.0 有，投影程序集和 winmd 都在），
     但**本仓库从未调用过**，在这个非打包 WinAppSDK 应用里的行为未经验证。

   **先探针，别假设。** 另外注意设备丢失：`CanvasView.OnCreateResources`
   （`:234-244`）会销毁一切绑定设备的资源，所以栅格化器要么每次操作一次性使用，
   要么做同样的失效处理。
3. **直通 alpha 与预乘的边界在哪。** `PixelBuffer` 是直通 alpha
   （`PixelBuffer.cs:4`），Win2D 的表面是预乘的。污点修复已经付过一次这个学费
   （见 `docs/PORTING.md` 的「污点修复」一节）。
   **但这里的方向是反的**：`SpotHealBrush` 的 `ToStraight` 在 `Imaging` 里，
   而 `DocumentRenderer.ToPremultipliedBgra` 是 `private`。
   要写的这一份（预乘 BGRA → 直通 RGBA）两边都没有，**新写一份并公开出来复用**，
   不要复制粘贴出第三份。

### 风险

- **中文字体缺失**是唯一可能推翻方案的点，先验证。
- **CanvasDevice 的获取方式未定**（见上面第 2 条），影响第 1 步能否在单测里跑。
- **零警告是硬门槛。** `TreatWarningsAsErrors` 虽然是 `false`，但
  `tools/verify/verify.ps1:260` 要求 `$warnings -eq 0` 才算 PASS。
- 上游有 `DocumentLimits.maxSideExtent` 之类的上限校验，我们栅格化出来的图层尺寸
  要有同样的上限，否则超大字号会把内存打满。
- 文字一旦栅格化就不能再编辑。**这是有意的**：可重排的文字图层需要把
  `TextStyle` 挂在图层上并在每次变更时重排，那等于把「文档模型」这条线又拉长一截。
  本轮按需求文档说的「弹窗输入转文字图层」做，**不做可重排文字**。

---

## 二、图层编组

### 结论先行

**模型层风险可控，UI 层才是真正的工作量。** 上游有一份干净的参考实现
`legacy/Compositor/Document/LayerGroups.swift`（311 行），取舍可以直接照搬。

### 上游的关键设计决定（照搬）

1. **文件夹是「穿透」的，不是当作一个合成单元。**
   `LayerGroups.swift:49-52` 的注释写得很明确：文件夹的不透明度**乘进**里面每个图层，
   而不是把文件夹整体合成一次再调不透明度。50% 的图层放进 50% 的文件夹 = 显示 25%，
   而面板上该图层仍显示 50%。
   → **这意味着 `CanvasDocument.Flatten` 不需要真正的递归合成**，只要在遍历时把
   祖先文件夹的不透明度连乘即可。工作量比预期小很多。

   **而且这不是「可以这么选」，是「必须这么选」。** 把子图层先合成到一个组缓冲区、
   再整体调组不透明度，**在像素上不等价**：子图层里任何非 Normal 的混合模式会去混合
   一个透明缓冲区，而不是真正的背景。上游自始至终让每一层直接混到running composite 上
   （`EditorCanvas.swift:3078-3117`），并且把文件夹的混合模式**钉死为 Normal**
   （`ProjectStore.swift:228-232`，注释写明「their blend mode is still pass-through」）。
   → **`BlendCompositor` 一行都不用改。** 传 `layer.Opacity × Π(祖先文件夹不透明度)` 即可。
   这也是唯一能同时保住 CPU 预览、GPU 画布、导出三条路径一致的做法。

2. **文件夹的另一个结论：新图层放进选中的文件夹里。**
   `EditorSession.swift:653` 与 `:859`：`parentID = activeLayer?.isGroup == true ? activeLayerID : activeLayer?.parentID`。
   我们现在的 `AddLayer` 硬编码插到最上面（`Layers.Insert(0, view)`），要跟着改。

3. **展开/折叠状态不能进模型。** 上游把它放在会话上
   （`EditorSession.swift:173` 的 `collapsedGroupIDs`），缩进由
   `CGFloat(min(depth, 8)) * 24` 算（`NativeLayerList.swift:908`）。
   放进 `Layer` 会让它进入撤销栈和文件格式。

4. **数据是一张平表 + `parentID`。** 层序、可见性、层级都由
   `LayerOrder.resolve` 从平表算出来，不维护第二棵树。存储仍是「下到上」的平表
   （`EditorSession.swift:67`），面板行是它反转并去掉折叠子树后的结果。

5. **必须防环、限深。** `LayerHierarchy.validate` 沿 `parentID` 向上走，
   每层看是否重复（环）以及父节点是不是文件夹，深度上限 64。拖拽改父节点时这是硬要求。

6. **可见性是沿路径连乘的。** 文件夹隐藏 ⇒ 里面全部不画，但子图层自己的
   `IsVisible` 不变。`entries()` 的 `effective = visible && layer.isVisible` 就是这条。

7. **文件夹自己也可以有蒙版，蒙版同样是穿透的。** `LayerMask.swift:168-171` 的注释：
   文件夹蒙版乘进里面每一层自己的蒙版，再乘外层的文件夹蒙版。
   → 这一步我们的 `CoverageFor` 目前只认图层自己的蒙版，要扩展成沿路径相乘。

### 我们这边要改什么

| 位置 | 改动 | 风险 |
| --- | --- | --- |
| `Models/Layers/Layer.cs` | 加 `Guid? ParentId`、`bool IsGroup`，**`Clone()` 必须带上这两个字段** | 低 |
| `Models/Documents/CanvasDocument.cs` | `Flatten`（`:90-113`）改成先算有效不透明度、有效可见性、有效蒙版再逐层合成；`Move`/`MoveUp`/`MoveDown`（`:68-84`）目前是整表下标，无法表达「进到父节点第 k 位」；`Remove`（`:57-62`）不删子树；新增 `LayerOrder`（移植上游 `resolve`/`validate`） | **中**，这是合成主路径，必须靠测试兜住 |
| **`Compositing/Rendering/DocumentRenderer.cs`** | **`RebuildLayers`（`:147-188`，不透明度在 `:165`/`:174` 施加）、`SignatureOf`（`:460-477`）也都是平表遍历。不一起改，画布预览会与导出不一致** | **中**，容易漏 |
| **`Compositing/Rendering/CpuCompositor.cs`** | **`NeedsFullDocumentPass`（`:35-43`）同样平表遍历** | **中**，容易漏 |
| **`App/ViewModels/TransformViewModel.cs`** | **`Resampled`（`:113-125`）在改画布尺寸时用 `Clone()` + `Add()` 重建整个文档。`Clone()` 不带上 `ParentId` 就会「改一次画布尺寸，编组静默消失」** | **中**，静默数据损坏 |
| `App/ViewModels/DocumentViewModel.cs` | `ReloadLayers`（`:346-354`）要输出展平的树行；`ReorderLayers`（`:275-283`）要带父节点；`AddLayer`/`AddAdjustmentLayer`/`ImportLayer` 的硬编码 `Layers.Insert(0, …)` 要改成「进到选中的文件夹」；`DuplicateLayer`（`:238-260`）要连子树一起复制；`DeleteLayer` 要连子树一起删；`LayerCountLabel`（`:104-106`）会把文件夹也算成图层 | **高**，这是最乱的一处 |
| `App/ViewModels/LayerViewModel.cs` | 加 `Depth` / `IsGroup` / `HasChildren`，`ContentLabel`（`:59-61`）要给文件夹第三种分支（否则显示「空图层」） | 低 |
| `App/Views/LayersPanel.xaml(.cs)` | 缩进、展开三角、拖入/拖出文件夹、建立/解散编组 | **最高** |
| `App/Views/MainWindow.xaml(.cs)` | 编组/解组命令；新增 flyout 动作必须**同时**登记进 `Shortcuts()` 与 `HintFor`（`MainWindow.xaml.cs:407-428` 对未登记动作抛异常），否则构造函数里就抛 | 中 |
| 两种 resx + `Strings.cs` + `LocalizedStrings.cs` | 新文案，必须两边同时加 | 低（有脚本把关） |

**预期完全不动的文件**：`Models/Blending/BlendCompositor.cs`、`Models/Pixels/PixelBuffer.cs`、
`Services/UndoStack.cs`（机制够用，改的只是调用方）、整个 `NaraPainter.Imaging`。

### 一个必须先决策的问题：要不要同时做「合并」

**当前 Windows 版根本没有合并/拼合命令**——`DocumentViewModel.Flatten()`
（`:286`）只被导出、吸管和自检调用，界面上没有入口。而上游是把编组和
⌘E 合并放在一起的（`LayerMerge.swift:34`），且合并计划本身就要读树
（多选 → Merge Layers；选中的是文件夹 → Merge Group；否则同一父节点下面的那一层 → Merge Down）。

→ **建议：编组这一批连 Merge Down / Merge Group 一起做**，否则用户建了组却拆不开。
但它是净新增代码，要单独占一小步，不要和模型改动混在一次提交里。

### 为什么 UI 是最高风险

面板当前把 `_document.Layers` **反转**后塞进 `Layers`
（`ReloadLayers`，`:349` `foreach (Layer layer in _document.Layers.Reverse())`），
所以「列表下标」与「模型顺序」本来就已经是反的（`DocumentIndex`，`:404-409`）。
加上层级之后，一次拖拽要同时决定：新的父节点、在新父节点内第几位、以及展开状态。
上游把这件事收在 `placeLayer(_:in:above:atBottom:)` 一个函数里并做完整校验，
我们应该照抄这个结构，而不是在 UI 事件里现算。

另外两点现成的坑：

- 面板用的是 WinUI 自带的 `CanReorderItems` / `ReorderMode="Enabled"`
  （`LayersPanel.xaml:23-33`），它只会**平铺重排**，没有「放到某一项**里面**」的概念。
  要拖进文件夹就得自己处理拖放，不能靠自带重排。上游对应的是 `dropOperation == .on`
  （`NativeLayerList.swift:402-406`）。
- `SmokeTest.cs:156-161` 在运行期钉住了「面板顺序 = 文档顺序的反转」这条不变量
  （`Layers[0].Name == Document.Layers[^1].Name`）。**这条不变量在树形下依然要成立**，
  否则自检会失败——这是好事，等于免费多了一道看门狗。

### 撤销

上游的 `beginEdit` / `endEdit` 一次操作记一步。我们这边照现有做法：
结构性改动（建组、解组、移动）用 `DelegateAction` 记**整个顺序的快照**。
`ReorderLayers`（`:275-283`）已经是这个模式，直接沿用，不要为编组另发明一套。

**注意现有三种结构性撤销都表达不了父子关系**，必须换掉：
`AddLayer`（`:198`）、`DuplicateLayer`（`:256`）、`DeleteLayer`（`:270`）存的都是
「一个 `LayerViewModel` + 一个平表下标」，既没有父节点也覆盖不了子树。
另外 `RemoveLayerCore`（`:369-376`）在行不在 `Layers` 里时会提前返回，
半途恢复会让面板与文档静默失同步——换成快照式之后这个问题自然消失。


### 排序建议

1. **模型层（不碰 UI）**：`Layer.ParentId`/`IsGroup` + `Clone()` 带上；移植
   `LayerOrder`（`entries` / `resolve` / `validate`）；`Flatten` 改成用有效不透明度、
   有效可见性、有效蒙版，并**跳过文件夹行本身**（现在它会分配一个整幅大小的透明缓冲区
   白合成一次，`:105-107`）。**同时改 `DocumentRenderer.RebuildLayers` 与
   `CpuCompositor.NeedsFullDocumentPass`**，否则画布和导出会不一致。
   这一步**只加单元测试**，跑绿。
2. **面板只读展示**：缩进 + 展开三角，不做拖拽。
3. **结构性操作**：建组 / 解组 / 拖拽改父节点，全部走快照式撤销。
4. **合并命令**（Merge Down / Merge Group），单独一步。

第 1 步做完就能确认模型改动没有破坏合成，这与「风险最高，必须放最后」并不冲突：
**放最后的是 UI 那一半**，模型那一半可以也应该先做。

现成的免费回归网：`DocumentModelTests.cs` 里 20 多条测试全部是平表断言，
只要 `ParentId == null && IsGroup == false` 时行为不变，它们会原样通过——
也就是说**模型层改动不需要改任何既有测试就能验证没有回归**。
上游对应的编组测试可以直接翻译：`legacy/CompositorTests/GroupTests.swift`
（隐藏文件夹盖住子图层、环与非法父子被拒、解组后子层回到文件夹原位并可撤销）。

### 一处需要留意的既有行为

`AddLayer` 目前的命名是 `_document.Layers.Count + 1`（`:194`），
编组之后「图层 N」这个名字会把文件夹也算进去。上游用的是去重后的
`Folder N`（`LayerGroups.swift:184-186`）。这是用户可见文案，要在同一批里定下来。


---

## 三、明确不做（本阶段）

- PSD / PSB 导入导出、CMYK、RAW、智能对象、矢量路径、动作批处理、插件系统
  —— 理由见 [MISSING_FEATURES.md](MISSING_FEATURES.md) 的「明确不实现」分区。
- 可重排（非栅格化）文字图层、按字符分段的颜色与字体。
- 文件夹的非穿透合成（即「先合成整组再调组不透明度」）——上游不是这么做的，
  我们跟着上游，保持两边行为一致。上面已说明这在像素上也不等价。
- 剪贴蒙版（clipping mask）。上游的渲染器确实会把剪贴栈合成到一个临时缓冲区
  （`LiveMaskRenderer.prepareStacks`），**但那是剪贴蒙版、不是文件夹**，
  而且我们这边连 `MaskSourceId` 字段都还没有，不在这批里做。

