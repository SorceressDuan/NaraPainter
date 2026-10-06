# Compositor.Compositing — Win2D 画布与 GPU 合成

`src/Compositor.Compositing/`。界面上的画布、缩放平移、逐图层合成，以及 GPU 不可用时的
CPU 回退。只引用 `Compositor.Models`，不引用 `Compositor.Imaging`。

| 文件 | 职责 |
| --- | --- |
| `Controls/CanvasView.cs` | 画布控件：缩放、平移、像素网格、透明棋盘格、`Invalidate` |
| `Rendering/DocumentRenderer.cs` | 把 `CanvasDocument` 画到 `CanvasDrawingSession` |
| `Rendering/CpuCompositor.cs` | 回退路径：整篇文档走 `BlendCompositor.Composite` |
| `Rendering/ViewTransform.cs` | 文档坐标 ↔ 控件 DIP 的换算，以及可见区域计算 |
| `Blending/BlendEffectFactory.cs` | `BlendMode` → Win2D 能力的映射 |

## 公开面

`CanvasView` 的成员与 [PORTING.md](../PORTING.md) 里定的签名逐字一致。
**注意基类不是 `CanvasControl`**：Win2D 1.4.0 的 WinUI 3 投影里
`CanvasControl` / `CanvasVirtualControl` / `CanvasAnimatedControl` 都是 `sealed`
（winmd 元数据：`Public, Sealed, Import, WindowsRuntime`），继承会直接 CS0509。
实际形态是 `public sealed class CanvasView : Microsoft.UI.Xaml.Controls.UserControl`，
构造时用代码创建 `CanvasControl` 作为 `Content`，所以本模块没有 XAML，
界面那边 `<compositing:CanvasView x:Name="Canvas" />` 与全部成员调用都不受影响；
唯一不能做的是把实例当成 `CanvasControl` 用（取 `.Device`、模式匹配等）。

除签名单上的成员外，另有三个附加成员：

| 成员 | 说明 |
| --- | --- |
| `IAdjustmentFilter? AdjustmentFilter` | 调整图层在画布上的实现。不设置则调整图层被跳过（与 `CanvasDocument.Flatten()` 不传 runner 的行为一致），画布会和导出不一致，所以界面必须设置它，且与导出用同一个实例 |
| `bool ForceCpuRendering` | 强制走 `CpuCompositor` |
| `bool UsedCpuPath` | 上一帧是否走了 CPU 路径 |

交互约定：滚轮以指针为锚点缩放（`1.1^(delta/120)`）；Shift+滚轮水平平移；
中键拖拽或 空格+左键拖拽平移；`Ctrl+0` 不在本模块处理，由界面调 `FitToWindow()`。
像素网格仅在 `ShowPixelGrid == true` 且 `Zoom >= 8` 时绘制，且每轴超过 2000 条线时跳过。

## 上游对照

| 上游（Swift / Metal） | 这里 |
| --- | --- |
| `MetalLayerEffects.swift` 的图层合成 kernel | `DocumentRenderer` 的逐层 `BlendEffect`，公式同 W3C |
| `CGContext` 画布与缩放 | `CanvasControl` + `CanvasDrawingSession`，`ViewTransform` 负责换算 |
| `CGImage` 位图 | `CanvasBitmap.CreateFromBytes`，上传前做直通→预乘转换 |
| `CALayer` 的合成组 | `CanvasRenderTarget` 之间的乒乓合成 |

## 合成管线

自下而上逐图层，只渲染**可见区域**（外加 25%、最多 512 文档像素的外扩，用于平移复用）：

1. `ViewTransform.VisibleDocumentRect` 算出控件里能看到的文档矩形，裁剪到文档边界。
2. 渲染区 = 可见区外扩一点，同样裁剪到文档；渲染比例 `scale` 在缩小时取缩放比
   （缩小时屏幕像素就是能看到的全部细节），放大时取 1（文档分辨率），
   再受 12M 像素上限与 `CanvasDevice.MaximumBitmapSizeInPixels` 约束。
3. 每层先落到 `_layerTarget`（清空→按区域裁剪画入，带不透明度），
   再用 `BlendEffect(background=_front, foreground=_layerTarget)` 合成到 `_back`，然后交换。
   Normal 模式不走效果，直接 SourceOver 画上去。每一步都在**新清空的**目标上写，
   所以不依赖「`CreateDrawingSession()` 会不会自动清屏」这种隐含行为。
4. 合成结果缓存在 `_front` 上，连同区域、比例、图层指纹一起记下来；
   平移只要还落在缓存区域内、缩放比例没变、文档指纹没变，就直接
   `DrawImage` 复用，不重新合成。`Refresh()` / 文档变更 / 设备丢失会清掉缓存。

图层指纹是「图层 Id、可见性、不透明度、混合模式、像素与蒙版数组的引用、调整设置」的哈希。
像素缓冲是原地修改的，所以**改完像素必须调 `Refresh()`**，指纹只兜住换缓冲和换图层的情况。

## 混合模式路由

Win2D 自己的 `CanvasBlend` 只有 `SourceOver` / `Copy` / `Min` / `Add`，只能表达 Normal。
`PixelShaderEffect` 能表达其余模式，但需要预先编译好的 HLSL 字节码，本工具链没有着色器编译器，
所以这条路是封死的。Direct2D 的混合效果（`BlendEffect`）按 W3C 合成与混合规范实现了
Photoshop 那套模式，映射关系在 `BlendEffectFactory.Map` 里逐个列出：

- `Normal` → 不用效果，直接 SourceOver（`BlendEffectMode` 里没有 Normal 这一项）
- 其余 23 个 → `BlendEffectMode` 同名项，`Divide` 对应 `Division`
- 效果建不出来、设备拒绝、图元超限 → 抛给 `DocumentRenderer.Draw` 的兜底分支，
  整篇文档改走 `CpuCompositor`

`BlendEffectMode` 里有而 `BlendMode` 里没有的项（`DarkerColor`、`LighterColor`、`Dissolve`）
不参与映射。

## 预乘 alpha

`CanvasBitmap.CreateFromBytes` / `CreateFromColors` **不接受 `CanvasAlphaMode.Straight`**，
实测抛 `COMException`，`hr = 0x88982F80`（`WINCODEC_ERR_UNSUPPORTEDPIXELFORMAT`），
硬件设备与 WARP 都一样；`R16G16B16A16` 之类的格式也一律 `E_INVALIDARG`。
所以上传前把 `PixelBuffer` 的直通 RGBA 转成预乘 BGRA（`ToPremultipliedBgra`），
用 `B8G8R8A8UIntNormalized + Premultiplied`。这同时正好是 `BlendEffect` 需要的输入格式。
蒙版走 `AlphaMaskEffect`（按蒙版的 alpha 相乘），coverage 直接写进预乘后的四个通道。

文档任一边长超过 `MaximumBitmapSizeInPixels`（典型 16384）时，上传前按同一比例整体缩小，
蒙版与整篇回退位图用同一个比例，保证坐标空间一致。20:1 的长图因此仍能走 GPU 路径。

## CPU 回退

`CpuCompositor.Render` 直接调 `CanvasDocument.Flatten`，也就是
`BlendCompositor.Composite`，不复制一份合成循环——两条路径的数值一致性靠这一点保证。
触发条件：

- `ForceCpuRendering` 或 `DocumentRenderer.ForceCpu`
- 文档里有可见的调整图层（调整是逐像素重写下方结果，本质是 CPU 数学）
- GPU 路径抛异常（设备丢失、效果不支持、图元超限）

设备丢失后 Win2D 会重新触发 `CreateResources`，控件在那里丢弃设备相关资源并清缓存，
下一帧重新尝试 GPU 路径；确定性的失败（模式没有 GPU 路由）会记住并停止重试，直到下次 `Refresh()`。

## 已知差异与限制

GPU 预览与 CPU 参考实现并非逐字节相同：Direct2D 在 8 位预乘空间里算，`BlendFunctions` 用
double 算。除下列模式外，实测每个通道差值 ≤ 3（64×64 噪声图、不透明度 0.75）：

| 模式 | 最大差值 | 差值 > 2 的通道数（共 16384） |
| --- | --- | --- |
| ColorBurn | 17 | 60 |
| ColorDodge | 43 | 48 |
| VividLight | 43 | 126 |
| HardMix | 143 | 39 |
| Divide | 15 | 67 |
| Hue | 11 | 28 |
| Saturation | 18 | 11 |

这些都是带除法或阈值的公式：输入差 1/255 就足以让 8 位与 double 的取整落在阈值的两边
（`HardMix` 的 `< 0.5` 尤其明显）。平均差值全部 ≤ 0.43。**导出与回退路径不受影响**：
CPU 路径与 `BlendCompositor` 逐字节相同。要逐像素一致就设 `ForceCpuRendering = true`。

其它限制：

- 像素网格每轴最多 2000 条线，超过就不画（超大窗口 + 8 倍放大会碰到）
- 空格拖拽要求画布持有焦点，点击画布会 `Focus(FocusState.Pointer)`
- 蒙版尺寸与文档不一致时忽略（`Layer.CoverageFor` 的既有语义），不重采样
- 缩放范围 2%–3200%（`MinZoom`/`MaxZoom`）

## 验证

构建（环境变量见 [BUILD.md](../BUILD.md)，沙箱里也要带 `-m:1 -nodeReuse:false`）：

```powershell
dotnet build src\Compositor.Compositing\Compositor.Compositing.csproj -c Debug -p:Platform=x64
# 0 个警告 0 个错误
```

无窗口验证用一个临时控制台工程跑（不进仓库）：引用本工程 + Models，覆盖

- `CpuCompositor.Render` 与 `CanvasDocument.Flatten()`、手写 `BlendCompositor` 链逐字节相同
- 24 个混合模式经 `CpuCompositor.Composite` 与 `BlendCompositor.Composite` 逐字节相同（含 coverage 与不透明度）
- 调整图层 runner 有无时的行为与 `Flatten` 一致
- 真设备（硬件与 `ForceSoftwareRenderer`）下 `DocumentRenderer.Draw` 不抛异常、不触发回退，
  强制 CPU 的一帧与参考实现逐字节相同
- 20 个混合模式的 GPU/CPU 差值表（见上）
- 4000×3000 文档首帧 229–406 ms，平移一帧 0 ms（缓存命中）
- `CanvasView` 在 WinUI 线程上可实例化，公开成员可用，可加入 XAML 面板

首帧数字来自 1600×1000 视口、0.28 倍缩放（即整篇文档在屏上）的测量；
真实窗口的端到端计时仍以 `Compositor.exe` 启动后的实测为准。
