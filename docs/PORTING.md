# 移植范围与模块交接

这份文档给接手具体模块的人看：上游代码在哪、契约是什么、边界在哪。
环境搭建和沙箱的坑见 [BUILD.md](BUILD.md)。

## 上游代码

原项目快照在 `legacy/`，只读参考，不要改。有价值的几处：

| 内容 | 位置 |
| --- | --- |
| 24 种混合模式的枚举与分组 | `legacy/Compositor/Document/LayerAppearance.swift` |
| 图层与文档模型 | `legacy/Compositor/Document/EditorSession.swift` 开头 |
| 蒙版 | `legacy/Compositor/Document/LayerMask.swift` |
| 色相/饱和度：色彩立方与 HSL 数学 | `legacy/Compositor/Document/HueSaturation.swift` |
| 色阶：LevelRange 与查表 | `legacy/Compositor/Document/Levels.swift` |
| 曲线：单调三次 Hermite | `legacy/Compositor/Document/Curves.swift` |
| 曝光、黑白、色彩平衡、颗粒 | `legacy/Compositor/Document/ImageAdjustments.swift` |
| 像素核（C） | `legacy/Compositor/Rendering/AdjustPixels.c`、`LevelsPixels.c` |

Swift 里的 `CGImage`/`CGContext` 对应 OpenCvSharp 的 `Mat`；
`Core Image` 那套滤镜没有直接对应，按 W3C 公式自己算（已经在 `Compositor.Models` 里做了）。
`MetalLayerEffects.swift` 那类 GPU 效果走 Win2D。

## 已经定好的契约

这些不要改，其他人依赖它们。要改先在共享任务里说。

- `src/Compositor.Models/Layers/BlendMode.cs` — 24 个模式，顺序即选择器顺序
- `src/Compositor.Models/Blending/BlendFunctions.cs` — 混合函数，W3C 公式，逐通道
- `src/Compositor.Models/Blending/BlendCompositor.cs` — CPU 合成参考实现
- `src/Compositor.Models/Pixels/PixelBuffer.cs` — 8 位 RGBA，**直通 alpha，不是预乘**
- `src/Compositor.Models/Layers/Layer.cs`、`Documents/CanvasDocument.cs` — 图层与文档
- `src/Compositor.Models/Adjustments/*` — 五个调整的设置对象

## 各模块

### Compositor.Imaging — OpenCvSharp

`src/Compositor.Imaging/`。CPU 像素操作和文件读写。

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

### Compositor.Compositing — Win2D

`src/Compositor.Compositing/`。画布渲染。

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
  `Compositor.Models.Blending.BlendCompositor.Composite`，保证 GPU 不可用时输出与参考实现一致。

### 界面与影像之间的接口

契约接口在 `src/Compositor.Models/Services/`，命名空间 `Compositor.Models.Services`：

- `IImageCodec` — 解码/编码，扩展名与格式（`ImageFileFormat` 枚举）
- `IAdjustmentFilter` — 应用调整
- `ISelectionMaskBuilder` — 选区覆盖度与按选区混合

放 Models 而不是 App，是因为 App 引用 Imaging，而 Imaging 要实现这些接口；
接口留在 App 里就会形成环，链接编译只是绕过。`Models` 没有人反向依赖，放这里环就没了。

`Compositor.Imaging` 里的实现类名固定为 `ImageCodec`、`AdjustmentFilter`、
`SelectionMaskBuilder`，构造函数无参，这样界面可以直接 `new`。


### Compositor.App — WinUI 3

`src/Compositor.App/`。目录按需求文档分：`Views/`、`ViewModels/`、`Services/`、
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
- `Compositor.exe` 在发布根目录，`Compositor.dll`、`Compositor.deps.json`、
  `Compositor.runtimeconfig.json` 等依赖与它同级（宿主就是按 exe 所在目录解析依赖的）
- `LICENSE`、`README.md`、`RUNNING.txt`（给普通用户的运行说明）在根目录

`Compositor.exe` 这个名字来自 App csproj 的 `<AssemblyName>Compositor</AssemblyName>`，
不是事后重命名。**改名是安全的**：代码里没有任何一处从进程名推导路径——窗口标题是
`DocumentViewModel.WindowTitle` 里拼的字符串，用户数据与日志固定写
`AppContext.BaseDirectory` 和 `%TEMP%`（`StartupLog`、`SmokeTest`、`App.Report`）。
真要改，改 `AssemblyName` 与 `package/Package.appxmanifest` 里的 `Executable` 即可。

**构建脚本必须是纯 ASCII。** Windows PowerShell 把无 BOM 的 `.ps1` 按 ANSI 解码，脚本里的中文
字面量在解析阶段就已经是乱码——`pack.ps1` 曾经因此发布出一个文件名乱码的说明文件。中文内容
一律放独立的 UTF-8 文件，用 `[System.IO.File]::ReadAllText(path, [System.Text.Encoding]::UTF8)`
读进来，再用 `UTF8Encoding($false)` 写出去。

### Compositor.Tests — xUnit

`tests/Compositor.Tests/`。`Compositor.Models` 和 `Compositor.Imaging` 都要覆盖到：

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
- `Compositor.exe` 能启动，打开图片、加图层、切混合模式、调滑条、导出
- 4000×3000 的图从打开到可编辑 2 秒以内
- 仓库里没有任何 Apple 框架引用

## 风格

按人写代码的样子来：只在真正绕的地方写注释，不写 `// TODO: Implement`
和每方法一段 `/// <summary>` 的模板注释。命名用常见英文词。
Allman 花括号、4 空格缩进。不要写「作为 AI」这类对话残留到代码或文档里。
