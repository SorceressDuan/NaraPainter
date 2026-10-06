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

- `Controls/CanvasView.cs`：`CanvasControl` 子类，负责缩放、平移、`Invalidate`，
  把 `CanvasDocument` 画出来。当前图层用 `CanvasRenderTarget`，
  混合用 `CanvasBlend` 或 `BlendEffect`。
- `BlendEffectFactory.cs`：把 `BlendMode` 映射到 Win2D 能力。
  Win2D 原生只认 `CanvasBlend` 里的那几种，其余必须走
  `PixelShaderEffect` 或退回 CPU。哪些走哪条路要在代码里写清楚。

**必须提供 CPU 回退**：任何一条 GPU 路径失效时，整条链路要能退化到
`Compositor.Models.Blending` 的实现并给出同样的像素结果。这是可测试性的底线。

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
