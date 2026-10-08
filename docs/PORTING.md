# 移植范围与模块交接

这份文档给接手具体模块的人看：上游代码在哪、契约是什么、边界在哪。
环境搭建和沙箱的坑见 [BUILD.md](BUILD.md)。

## 当前策略：优先移植，其次开发

上游 `legacy/` 里的 Swift 实现比我们已移植的多得多。**动手写新功能之前，先在 `legacy/` 里找一遍**：

- 上游有 → 翻译它的算法。难度更低，保真度更高（数值行为、边界情况都是原作者的判断）
- 上游没有 → 再在 C# 里从零设计

这个顺序不是图省事，是为了不重复犯原作者已经犯过并修正过的错。

## v0.3.0 规划

分三批，**同时只动一个模块**，每个模块独立跑绿再进下一个。

### 第一阶段

| 顺序 | 模块 | 上游是否已有 | 备注 |
| --- | --- | --- | --- |
| 1 | **修复画笔 / 污点修复** | 污点修复**已完成**（v0.3.0） | 算法本体是 `legacy/Compositor/Rendering/HealPixels.c` 的 `spot_heal()`，不是 Swift。对照见下表「污点修复」一节 |
| 2 | **基础文字工具** | 有 —— `ImageLayer.text` | 上游是画布内联编辑，我们改为「弹窗输入 → 生成文本图层」。动手前必须先验证 Win2D 能否渲染中文 |
| 3 | **图层编组** | 有 —— `ImageLayer.parentID` + `isGroup` | 字段定义可直接照搬。但它同时触及图层模型、`CanvasDocument.Flatten` 递归、撤销栈、图层面板树形 UI 四处，风险最高，放最后 |

### 第二阶段（先出技术方案，暂不开工）

| 模块 | 上游是否已有 | 已知难点 |
| --- | --- | --- |
| 完整画笔引擎（颜色、笔刷预设、压感） | 有 —— 上游有 Brush（Paint/Erase、smoothing、Shift 直线） | 需要新增「按颜色在像素上绘制」的路径，与现有蒙版画笔不同；无数位板时压感只能模拟 |
| 图层样式（描边、投影） | 有 —— `MetalLayerEffects.swift`，上游走 GPU | 上游是 Metal，我们得走 Win2D。渲染时机与混合模式的叠加顺序要重新定 |
| 魔棒 / 快速选择（GrabCut） | 有 —— 上游 Magic Wand 与 Object 追踪 | GrabCut 的迭代次数与初始化方式直接决定交互延迟，大画布上必须做预览降采样 |

### 第三阶段：明确不实现

PSD 导入导出、CMYK 色彩管理、RAW、智能对象、矢量路径、动作与批处理、插件系统。

**不写代码，也不留空接口。** 逐条理由见
[MISSING_FEATURES.md](MISSING_FEATURES.md) 的「明确不实现（专业级深水区）」。
留接口只会让人误以为这些能力在路上。

## 上游代码

原项目快照在 `legacy/`，只读参考，不要改。有价值的几处：

| 内容 | 位置 |
| --- | --- |
| 24 种混合模式的枚举与分组 | `legacy/Compositor/Document/LayerAppearance.swift` |
| 图层与文档模型 | `legacy/Compositor/Document/EditorSession.swift` 开头 |
| 图层组字段（`parentID` / `isGroup`） | 同上，`ImageLayer` 结构体 |
| 蒙版 | `legacy/Compositor/Document/LayerMask.swift` |
| 色相/饱和度：色彩立方与 HSL 数学 | `legacy/Compositor/Document/HueSaturation.swift` |
| 色阶：LevelRange 与查表 | `legacy/Compositor/Document/Levels.swift` |
| 曲线：单调三次 Hermite | `legacy/Compositor/Document/Curves.swift` |
| 曝光、黑白、色彩平衡、颗粒 | `legacy/Compositor/Document/ImageAdjustments.swift` |
| 像素核（C） | `legacy/Compositor/Rendering/AdjustPixels.c`、`LevelsPixels.c` |
| **污点修复的算法本体（C）** | `legacy/Compositor/Rendering/HealPixels.c`、`HealPixels.h` |
| 污点修复的笔迹生命周期 | `legacy/Compositor/Document/BrushStroke.swift` 的 `heal()`（约 867 行起） |
| 上游完整功能清单（自述） | `legacy/README.upstream.md` 的 Features 一节 |

Swift 里的 `CGImage`/`CGContext` 对应 OpenCvSharp 的 `Mat`；
`Core Image` 那套滤镜没有直接对应，按 W3C 公式自己算（已经在 `NaraPainter.Models` 里做了）。
`MetalLayerEffects.swift` 那类 GPU 效果走 Win2D。

## 污点修复（v0.3.0 已完成）

上游把它拆成「拖动时攒 coverage，松手时一次性重建」两段，我们照搬了这个节奏：

| 我们这边 | 上游对应 |
| --- | --- |
| `src/NaraPainter.Imaging/Services/SpotHeal.cs` | `HealPixels.c` 的 `spot_heal()` / `heal_coverage_bounds()`，逐行对照 |
| `src/NaraPainter.Imaging/Services/SpotHealBrush.cs` | `BrushStroke.swift` 的 `heal()`，负责裁工作区与 alpha 转换 |
| `src/NaraPainter.App/ViewModels/SpotHealViewModel.cs` | `EditorSession+Brush.swift` 的 `beginBrush` / `finishBrush` |
| `tests/NaraPainter.Tests/SpotHealTests.cs` | `legacy/CompositorTests/SpotHealingTests.swift` 的条纹+红斑用例 |

**一处必须记住的差异**：上游的 `HealPixels.h` 要求**预乘 alpha**，而本项目的 `PixelBuffer`
是直通 alpha（见下面的契约）。转换只在 `SpotHealBrush` 的边界做，算法内核保持与 C 版一致。
代价是**全透明像素的颜色会在往返中丢失**——预乘空间里 alpha=0 不携带颜色。对不透明照片无影响。

涂过的区域之外，图层像素必须逐字节不变；这条被 `SpotHealTests` 与 `SpotHealStrokeTests` 都锁住了。

## 文字工具（v0.3.0 已完成基础版）

上游 `legacy/Compositor/Document/TypeTool.swift` 是 **472 行 CoreText/AppKit 排版**（行内编辑、
段落框、字偶距、按字符的颜色与字体分段），**不能移植**——Windows 侧没有对应物。
只有 `LayerTextStyle` 的字段定义可以照搬，见 `NaraPainter.Models/Text/TextStyle.cs`。
排版层是重写的，走 Win2D。

| 我们这边 | 上游对应 |
| --- | --- |
| `NaraPainter.Compositing/Rendering/TextRasterizer.cs` | `TypeTool.swift` 的排版与绘制 |
| `NaraPainter.Models/Text/TextStyle.cs` | `LayerTextStyle` 的字段 |
| `NaraPainter.App/Views/TextDialog.cs` | 上游的画布内联编辑（我们改成弹窗） |
| `tests/NaraPainter.Tests/TextLayerTests.cs` | 无对应，上游测的是行内编辑 |

### 三个已验证的平台事实

1. **`CanvasDevice.GetSharedDevice()` 可用。** 不需要活动画布、不需要 `CanvasControl`，
   在本项目的非打包 WinAppSDK 配置下能离屏渲染。这是文字工具与后续任何离屏绘制的前提。
   （仓库里 `CanvasView.CreateCheckerBrush` 是另一个先例，但它是在 `OnDraw` 内部拿 `session.Device`。）
2. **`CanvasTextLayout` 的 `requestedWidth` 传 0 是个陷阱。** DirectWrite 会在**每个字符后换行**，
   一串 8 个汉字会排成 8 行、尺寸变成 48×487.7 而不是 398.2×61。要传一个足够宽的排版边界。
   这个坑不测就发现不了——`LayoutBounds` 是"合理"的非零值，只是方向反了。
3. **Win2D 表面是预乘 BGRA，`PixelBuffer` 是直通 RGBA。** 读了要换通道序 + 反预乘。
   这是本项目第二次付这个学费（第一次是污点修复，见上一节），
   两处的转换都在各自文件的边界上，没有合并成一份——**下次要动的话先合并**。

### 一条设计约束：文字图层必须是画布大小

`CanvasDocument.Fit` 与 `DocumentRenderer` 会把**非画布尺寸**的图层**拉伸**到画布，
再合并或导出时字形就被重采样了。所以 `TextRasterizer` 返回的是画布大小的缓冲区、
文字按点击坐标画在里面，而不是一张紧贴文字的图。`TextLayerTests` 里
`ATextLayerIsCanvasSizedSoNothingStretchesIt` 锁住了这条。


## 已经定好的契约

这些不要改，其他人依赖它们。要改先在共享任务里说。

- `src/NaraPainter.Models/Layers/BlendMode.cs` — 24 个模式，顺序即选择器顺序
- `src/NaraPainter.Models/Blending/BlendFunctions.cs` — 混合函数，W3C 公式，逐通道
- `src/NaraPainter.Models/Blending/BlendCompositor.cs` — CPU 合成参考实现
- `src/NaraPainter.Models/Pixels/PixelBuffer.cs` — 8 位 RGBA，**直通 alpha，不是预乘**
- `src/NaraPainter.Models/Layers/Layer.cs`、`Documents/CanvasDocument.cs` — 图层与文档
- `src/NaraPainter.Models/Adjustments/*` — 五个调整的设置对象

## 各模块

### NaraPainter.Imaging — OpenCvSharp

`src/NaraPainter.Imaging/`。CPU 像素操作和文件读写。

- `Services/ImageCodec.cs`：`Cv2.ImRead` / `Cv2.ImWrite`。注意 OpenCV 用 BGR 顺序，
  `PixelBuffer` 是 RGBA，两边转换必须显式做，别靠 `Cv2.CvtColor` 猜。
  `Cv2.ImWrite` 按扩展名选编码器，PNG/JPEG/WebP/BMP/TIFF 都支持，WebP 在
  `OpenCvSharp4.runtime.win` 里编进去了。
- `Services/AdjustmentFilter.cs`：把 `AdjustmentSettings` 应用到 `PixelBuffer`。
  四个亮度/对比度和色阶都走 256 项查表，逐通道；色相/饱和度走逐像素 HSL。
  查表只处理颜色通道，alpha 原样保留。
- `Services/CanvasRenderer.cs`：`CanvasDocument.Flatten` 的配套实现，负责把
  `AdjustmentSettings` 变成对下方合成结果的操作，然后交给 `BlendCompositor.Composite`。
- `Services/SelectionMask.cs`：矩形/椭圆选区转成 `byte[]` 覆盖度，喂给 `Composite`。

红线：不要用 `System.Drawing`。任何像素级操作都要有 `// MIGRATION: 原API -> 新API` 注释。

### NaraPainter.Compositing — Win2D

`src/NaraPainter.Compositing/`。画布渲染。

`Controls/CanvasView.cs` 对界面那边的公开表面，先按这个签名写，界面会直接调：

```csharp
public sealed class CanvasView : Microsoft.UI.Xaml.Controls.UserControl
{
    public CanvasDocument? Document { get; set; }
    public double Zoom { get; set; }          // 1.0 = 100%
    public double PanX { get; set; }
    public double PanY { get; set; }
    public bool ShowPixelGrid { get; set; }
    public void FitToWindow();
    public void ZoomTo(double zoom);          // 以画布中心为锚点
    public void Refresh();                    // 文档改了之后调用
    public event EventHandler? ViewChanged;   // 缩放/平移变化后触发，状态栏用
}
```

**基类是 `UserControl` 而不是 `CanvasControl`**：Win2D 1.4.0 的 WinUI 3 投影里
`CanvasControl`、`CanvasVirtualControl`、`CanvasAnimatedControl` 全是 sealed，
继承不了（CS0509）。所以 `CanvasView` 自己是个 `UserControl`，
内部 new 一个 `CanvasControl` 当 Content。外面用它的人别写 `is CanvasControl`。

- `Rendering/DocumentRenderer.cs`：把 `CanvasDocument` 画到 `CanvasDrawingSession`：
  自下而上逐图层，每层先画到 `CanvasRenderTarget` 再做混合。
- `Blending/BlendEffectFactory.cs`：`BlendMode` → Win2D 能力映射。
  Win2D 原生只认 `CanvasBlend` 里的那几种，其余必须显式处理：
  能用 `BlendEffect` 的用，不能的退回 CPU。哪些模式走哪条路要写在代码注释里。
- `Rendering/CpuCompositor.cs` — 回退路径，直接调
  `NaraPainter.Models.Blending.BlendCompositor.Composite`，保证 GPU 不可用时输出与参考实现一致。

### 界面与影像之间的接口

契约接口在 `src/NaraPainter.Models/Services/`，命名空间 `NaraPainter.Models.Services`：

- `IImageCodec` — 解码/编码，扩展名与格式（`ImageFileFormat` 枚举）
- `IAdjustmentFilter` — 应用调整
- `ISelectionMaskBuilder` — 选区覆盖度与按选区混合

放 Models 而不是 App，是因为 App 引用 Imaging，而 Imaging 要实现这些接口；
接口留在 App 里就会形成环，链接编译只是绕过。`Models` 没有人反向依赖，放这里环就没了。

`NaraPainter.Imaging` 里的实现类名固定为 `ImageCodec`、`AdjustmentFilter`、
`SelectionMaskBuilder`，构造函数无参，这样界面可以直接 `new`。


### NaraPainter.App — WinUI 3

`src/NaraPainter.App/`。目录按需求文档分：`Views/`、`ViewModels/`、`Services/`、
`Controls/`、`Converters/`。

- `Views/MainWindow.xaml`：左图层面板 + 中画布 + 右属性面板，加工具栏。
- `Views/LayersPanel.xaml`：`ListView` + 拖拽排序 + 可见性 + 不透明度 + 混合模式。
- `Views/PropertiesPanel.xaml`：调整滑条。
- `ViewModels/DocumentViewModel.cs`、`LayerViewModel.cs`、`AdjustmentViewModel.cs`。
- `Services/FileDialogService.cs`：`FileOpenPicker` / `FileSavePicker`，
  未打包应用要先用 `WinRT.Interop.InitializeWithWindow` 关联窗口句柄。
- `Services/UndoStack.cs`：撤销栈，MVP 只需要覆盖图层增删改和调整。

命名空间用 `Microsoft.UI.Xaml.*`，禁止 `Windows.UI.Xaml.*`。

### 可执行文件名与发布布局

发布的根目录必须是这个形状，`packaging/pack.ps1` 会自己断言，`tools/verify/verify.ps1`
的 `PACKAGE` 一段也会复核：

- zip 内**不套层文件夹**：解压后内容直接落在用户选的目标目录
- `NaraPainter.exe` 在发布根目录，`Compositor.dll`、`Compositor.deps.json`、
  `Compositor.runtimeconfig.json` 等依赖与它同级（宿主就是按 exe 所在目录解析依赖的）
- `LICENSE`、`README.md`、`RUNNING.txt`（给普通用户的运行说明）在根目录

`NaraPainter.exe` 这个名字来自 App csproj 的 `<AssemblyName>NaraPainter</AssemblyName>`，
不是事后重命名。**改名是安全的**：代码里没有任何一处从进程名推导路径——窗口标题是
`DocumentViewModel.WindowTitle` 里拼的字符串，用户数据与日志固定写
`AppContext.BaseDirectory` 和 `%TEMP%`（`StartupLog`、`SmokeTest`、`App.Report`）。
真要改，改 `AssemblyName` 与 `package/Package.appxmanifest` 里的 `Executable` 即可。

**构建脚本必须是纯 ASCII。** Windows PowerShell 把无 BOM 的 `.ps1` 按 ANSI 解码，脚本里的中文
字面量在解析阶段就已经是乱码——`pack.ps1` 曾经因此发布出一个文件名乱码的说明文件。中文内容
一律放独立的 UTF-8 文件，用 `[System.IO.File]::ReadAllText(path, [System.Text.Encoding]::UTF8)`
读进来，再用 `UTF8Encoding($false)` 写出去。

### NaraPainter.Tests — xUnit

`tests/NaraPainter.Tests/`。`NaraPainter.Models` 和 `NaraPainter.Imaging` 都要覆盖到：

- 混合模式的数值，对照 W3C 公式手算的期望值，24 个模式都要有
- alpha 边界：全透明背景、全透明源、半透明叠加
- 调整算法：查表边界、色相绕回 360、色阶白点小于黑点的夹紧
- 文件读写往返：编码后解码回来，像素差异在容差内
- 文档模型：图层排序、删除、`Flatten` 的层序、蒙版覆盖

再加两条结构性检查：仓库里不能出现 Apple 框架引用
（`import UIKit`、`AppKit`、`CoreGraphics`、`Metal`、`NSObject` 之类），
也不能出现 `System.Drawing` 和 `Windows.UI.Xaml`。

## 验收

- `dotnet build Compositor.sln -c Debug` 零错误零警告
- `dotnet test` 全绿
- `NaraPainter.exe` 能启动，打开图片、加图层、切混合模式、调滑条、导出
- 4000×3000 的图从打开到可编辑 2 秒以内
- 仓库里没有任何 Apple 框架引用

## 风格

按人写代码的样子来：只在真正绕的地方写注释，不写 `// TODO: Implement`
和每方法一段 `/// <summary>` 的模板注释。命名用常见英文词。
Allman 花括号、4 空格缩进。不要写「作为 AI」这类对话残留到代码或文档里。
