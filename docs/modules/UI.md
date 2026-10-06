# 界面模块（NaraDreamPainter.App）

WinUI 3 外壳：窗口布局、图层与属性面板、文件对话框、拖放、撤销。画布渲染在
`NaraDreamPainter.Compositing`，像素操作在 `NaraDreamPainter.Imaging`，界面只通过 `NaraDreamPainter.Models.Services`
下的三个接口和 `CanvasView` 的公开成员接触它们。

界面文字全部来自资源文件，默认简体中文、英文系统回退英文，见 [LOCALIZATION.md](LOCALIZATION.md)。

## 文件

| 文件 | 作用 |
| --- | --- |
| `App.xaml` / `App.xaml.cs` | 应用资源（`XamlControlsResources` + 转换器）、窗口创建、启动失败日志、`--selftest` 入口 |
| `Views/MainWindow.xaml(.cs)` | 工具栏 + 三栏布局 + 状态栏，画布装配、缩放命令、打开/导出、快捷键 |
| `Views/LayersPanel.xaml(.cs)` | 图层列表、可见性、行内混合模式与不透明度、拖拽排序、底部增删/上下移 |
| `Views/PropertiesPanel.xaml(.cs)` | 图层属性、四个调整折叠面板、选区与蒙版 |
| `Controls/AdjustmentSlider.xaml(.cs)` | 标题 + NumberBox + Slider 的复合控件，`EditStarted` 用来划分撤销步 |
| `ViewModels/DocumentViewModel.cs` | 文档、图层集合、选中层、缩放、状态文本、打开/导出/重排 |
| `ViewModels/LayerViewModel.cs` | 单个图层：名称、可见性、不透明度、混合模式、调整栈、蒙版 |
| `ViewModels/AdjustmentViewModel.cs` | 四个调整的编辑状态，写到当前图层的调整栈 |
| `ViewModels/SelectionViewModel.cs` | 选区矩形/椭圆与蒙版应用 |
| `ViewModels/CurvePointViewModel.cs` | 曲线控制点，越界编辑由 `AdjustmentViewModel` 驳回 |
| `ViewModels/ObservableObject.cs` | 手写 `INotifyPropertyChanged`（离线源里没有 MVVM Toolkit） |
| `Services/UndoStack.cs` | 撤销栈、`PropertyChange<T>`、`DelegateAction`、合并键 |
| `Services/FileDialogService.cs` | `FileOpenPicker` / `FileSavePicker`，未打包应用先关联 HWND |
| `Services/ImageImporter.cs` | `IImageCodec` 读出的 `PixelBuffer` → `Layer` / `CanvasDocument` |
| `Services/DroppedFile.cs` | 从拖放数据里取第一个文件路径 |
| `Services/SmokeTest.cs` | `--selftest` 的端到端自检，写日志并返回退出码 |
| `Services/StartupLog.cs` | 每次启动记一行命令行与参数解析结果，供「自检没反应」时定位 |
| `Converters/BoolToVisibilityConverter.cs` | `{Binding}` 用的 bool → Visibility |

## 布局与控件映射

工具栏是 `CommandBar`：打开(Ctrl+O)、导出(Ctrl+E)、撤销(Ctrl+Z)、重做(Ctrl+Y)、新建图层
(Ctrl+Shift+N)、复制、删除、调整图层下拉、缩小/放大/适应窗口/100%。快捷键走
`KeyboardAccelerator`，都挂在按钮上。

| 原控件 | 这里 |
| --- | --- |
| `NSTableView` | `ListView` + `DataTemplate`，行内放可见性、名称、混合模式、不透明度 |
| `NSSlider` | `Controls/AdjustmentSlider`（Slider + NumberBox，滑条拖动、数字精确输入） |
| `NSPopUpButton` | `ComboBox`，`SelectedIndex` 直接绑枚举序号（混合模式 24 项的顺序就是 `BlendMode` 的顺序） |
| `NSMenu` | `CommandBar` + `MenuFlyout` |
| `NSColorWell` | 暂无对应功能，未接入 `ColorPicker` |

图层列表的拖拽排序用 `ListView` 自带的 `CanReorderItems` + `DragItemsCompleted`，完成后把面板顺序交回
`DocumentViewModel.ReorderLayers`，由它换算成文档层序并记一条撤销。

## 数据流

`DocumentViewModel` 持有 `CanvasDocument`。面板里的 `Layers` 集合是**上层在前**（列表从上到下就是
图层从高到低），而 `CanvasDocument.Layers` 是**下层在前**，两者在 `DocumentViewModel` 里换算，重排后
`Layers[0]` 必须等于 `Document.Layers[^1]`（自检里有这条断言）。图层属性 setter 直接改模型，然后把
一对回放回调交给 `UndoStack`。

窗口订阅 `Changed`（像素/结构变了 → `Canvas.Refresh()`）和 `DocumentReplaced`（换了文档 →
`Canvas.Document = doc` + `FitToWindow()`）。画布只负责画，不参与模型变更。

## 调整

普通图层的调整是**非破坏栈**：`LayerViewModel` 保存 `AdjustmentSettings?[4]`，并保留载入时的原始像素；
任何一次改动都用 `IAdjustmentFilter.ApplyAll` 从原始像素重算整条链，写回 `Layer.Pixels`，所以把滑条
拖回 0 会彻底还原（identity 的调整会从栈里移除）。

调整图层（`Layer.Adjustment`）是另一种：它没有像素，`CanvasDocument.Flatten(adjustmentRunner)` 在合成
到它时对下方结果跑一次 `IAdjustmentFilter.Apply`。`MainWindow` 把 `Document.Filter` 同时赋给
`Canvas.AdjustmentFilter`，画布预览和导出走同一个实现，不会出现「画布看不到、导出却变了」。

- 亮度/对比度：两个 -100..100 滑条。
- 色相/饱和度：先选 7 个范围之一（Master/Reds/…/Magentas），三个滑条只改该范围；另有着色开关。
- 色阶：选通道（RGB/R/G/B），黑场、白场、灰度系数、输出黑白；`LevelRange.Normalized` 负责把白场夹在
  黑场之上。
- 曲线：通道选择 + 控制点列表，X/Y 都是数字输入，两端点 X 锁定在 0 和 255；「Add Point」在最大间隔的
  中点插一个线性插值的点。编辑越界（越过相邻点）会被 `CurvesSettings.With` 拒绝，控件保留原值。

## 撤销

`UndoStack` 里存的都是「已经发生」的动作，`Undo()/Redo()` 只是回放：

- `PropertyChange<T>` 记住旧值与新值，回放时调用同一个 setter（setter 内部会刷新通知与画布）。
- `DelegateAction` 用于图层新建/删除/复制/导入/重排，用同一对方法来回放。
- 合并键：滑块拖动会连续触发几十次 setter，同一 `MergeKey` 的连续动作会折叠成一条记录，所以一次拖动
  只占一步撤销。`AdjustmentSlider` 在按下/获得焦点时抛 `EditStarted`，视图模型借此换一个新的合并键，
  于是「拖一次 → Ctrl+Z」回到拖动前的值，而不是逐帧回退。
- 上限 200 步，打开新文件会清空。

## 选区与蒙版

选区目前是数值输入（矩形/椭圆 + X/Y/宽/高 + 羽化），`ISelectionMaskBuilder.Build` 生成画布分辨率的
覆盖度写进 `Layer.Mask`，合成时作为该图层 alpha 的乘数（`Layer.CoverageFor`）。「Select All」用
`Full()`，另可清除蒙版。画布上还没有交互式选区工具，这是后面要补的。

## 打开、导出、拖放

- 打开/导出用 `FileOpenPicker` / `FileSavePicker`，未打包应用必须先
  `WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd)`，否则对话框直接失败。可读/可写扩展名
  从 `IImageCodec.SupportedReadExtensions / SupportedWriteExtensions` 取，不在界面里写死格式表。
- 拖到**画布**上＝按新文档打开；拖到**图层面板**上＝导入成当前文档的一个图层（尺寸不同会缩放到画布）。
- 导出走 `Flatten()`，把结果交给 `IImageCodec.Write`，扩展名决定编码格式。

## 验证

构建（环境变量按 `docs/BUILD.md`）：

```powershell
dotnet build Compositor.sln -c Debug      # 0 warning 0 error
```

沙箱里同时跑多个 `dotnet build` 会互相抢 `obj/` 下的中间产物，报 CS2012 或
「input.json is being used by another process」；构建目录一旦坏掉，应用会在 `OnLaunched` **之前**
就抛 `XamlParseException`（退出码 `-1073741189` / `0xC000027B`），现象和「自检没被执行」一模一样。
同一份源码换个构建方式就正常，所以改了 App 的代码之后按这个顺序走：

```powershell
dotnet build-server shutdown
Remove-Item -Recurse -Force src\NaraDreamPainter.App\obj, src\NaraDreamPainter.App\bin
dotnet build Compositor.sln -c Debug
```

只删 App 的 `obj`/`bin` 再整体构建是目前稳定复现的配方；`-t:Rebuild` 不要用——它会连带重建依赖项目，
而 App 的 PRI 合并正好可能在依赖的 `.pri` 被清掉时跑，报
`PRI252 ... NaraDreamPainter.Compositing.pri not found`，或者留下更坏的中间产物。构建冲突时先
`dotnet build-server shutdown`（必要时加 `-nodeReuse:false`），最终验收前别和其他人的构建并行。

启动到底有没有走到自检，看 exe 目录下的 `startup.log`（同内容另写一份到
`%TEMP%\naradreampainter-startup.log`）：`[OnLaunched]` 一行带完整命令行、参数解析结果和工作目录，
后面跟着 `[selfTest] queued`、`[selfTest] running`、`[smokeTest]` 的路径与退出码。
如果里面只有 `[unhandled] XamlParseException` 而没有 `[OnLaunched]`，就是构建目录坏了，按上面的配方重建。

启动：

```powershell
& "src\NaraDreamPainter.App\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\NaraDreamPainter.exe"
```

沙箱里点不到窗口，所以功能验证靠 `--selftest`：它用真实视图模型跑一遍
「打开 → 新建图层 → 三种混合模式 → 亮度对比度 → 撤销/重做 → 色阶/色相饱和度/曲线 → 蒙版 → 导出 →
重新读回 → 打开 4000×3000」，每步都断言像素确实变了，最后把日志写文件并返回进程退出码。

```powershell
NaraDreamPainter.exe --selftest assets\testimages\photo.jpg --export .tools\out.png --log .tools\selftest.log
```

```text
window.title=NaraDreamPainter — Untitled
open file=photo.jpg size=800 × 600 layers=1 ms=31
window.title=NaraDreamPainter — photo.jpg
newLayer name=Layer 2 layers=2 size=800 × 600
blendModes Multiply=162,101,109,255 Screen=221,181,187,255 Overlay=211,146,169,255
brightnessContrast brightness=25 contrast=10 before=211,146,169,255 after=229,172,195,255
undo sample=211,146,169,255 canUndo=True canRedo=True
redo sample=229,172,195,255
levels black=30 white=230 before=229,172,195,255 after=232,175,195,255
hueSaturation saturation=-60 before=232,175,195,255 after=229,191,192,255 layerPixel=244,199,249,150
hueShift hue=120 before=229,191,192,255 after=230,211,164,255
curves points=3 point1Y=180 before=230,211,164,255 after=231,214,178,255
mask masked=True inside=153,123,85,255 outside=159,132,103,255
export path=...\ui-export.png bytes=1188444 sample=198,160,123,255
reopen size=800 × 600 center=198,160,123,255
duplicate name=Layer 2 copy layers=3 hasAdjustments=True
reorder panelTop=photo documentTop=photo layers=3
openLarge file=large-4000x3000.png size=4000 × 3000 layers=1 ms=286
RESULT PASS
exit=0
```

导出的 PNG 能直接看出蒙版的作用：左上 (0,0)-(400,300) 是加了调整和蒙版的图层，其余部分是未处理的底图。

参数两种写法都认：`--selftest <图片>` 和 `--selftest=<图片>`，`--large/--export/--log` 同理；
不传的项有默认值（`assets\testimages\photo.jpg`、`large-4000x3000.png`、
`%TEMP%\naradreampainter-selftest.png`、exe 目录下的 `selftest.log`）。注意 `--selftest` 后面会紧跟一个
图片路径，所以 `--selftest --log x.log` 里的 `--log` 会被当成图片名，日志里会出现一条
`RESULT FAIL FileNotFoundException`——这不算 bug，是这种写法的必然结果，用 `--selftest=<图片>` 更稳。

退出码：`0` 通过，`1` 有断言失败或图片打不开（日志里有 `RESULT FAIL ...`），
`-1073741189`（`0xC000027B`）是启动期 XamlParseException，说明构建目录坏了、自检根本没跑。
`--log` 指定的路径写不进去时（只读目录、沙箱限制），会自动退回到 exe 目录的 `selftest.log`
并在日志里说明，不会因为写日志失败而丢结果。

沙箱里没法确认、需要人工点一遍的：文件对话框（要真人选文件）、拖放、滑条拖动与键盘加速键的交互手感、
撤销按钮的可用状态。

## 已知限制

- 调整是整幅重算。800×600 无感，4000×3000 拖滑条每次约几百毫秒，会顿。
- 没有画笔/橡皮/渐变/文字工具，MIGRATION_SPEC 阶段 3 里的这几个按钮因此没放进工具栏。
- 图层行只显示尺寸或调整名，没有缩略图。
- 选区只能输入数值，不能画布上拖框；蒙版也不可视化。
- 关闭窗口不提示未保存；文档本身也没有自己的保存格式，只能导出成图片。
- `--selftest` 是给无显示环境用的自检入口，正常启动时不走它。


## 撤销粒度

一次拖动 = 一个撤销步，独立的一次改动 = 一个撤销步。

`UndoStack` 按 `MergeKey` 合并相邻动作，所以「合并」这件事完全由键决定：同一个键会collapse成一步，
不同的键各自成步。两条规则：

- `BeginOpacityEdit()` / `BeginEdit()` 打开一次编辑，此后到下一次打开之间的所有值共用一个键
- 没有打开编辑时，每个值拿一个**独立**键

第二条是关键，也是踩过的坑：键里原来用的是初始为 0 的 session 计数，而 session 只有 `BeginEdit` 才会递增，
于是「没有 BeginEdit 的两次独立改动」键相同、被静默合并——用户按方向键改两次值，一次 Ctrl+Z 会退回两次。
现在 session 为 0 时发一个递增的独立键。

滑条这一侧还有第二个坑：`AdjustmentSlider` 原本同时监听 `Track` 的焦点事件。按下滑条时 Slider 稍后会把焦点
交给 Thumb，于是 `PointerPressed` 开的 session 立刻被 `GotFocus` 顶掉、又被 `PointerCaptureLost` 关掉，
拖动中的每个值都走了「独立」分支，一步也合并不了。现在滑条**只认指针事件**，焦点事件只留给数字输入框
（输入框需要它，一次输入才算一次编辑）。

## 打开与导入的撤销

`DocumentViewModel.Open` 会替换整个文档（像素与画布尺寸），所以撤销它必须换回上一个文档对象，
`PropertyChange` 那套改不了画布尺寸。走的是 `DelegateAction` + `SwapDocument`。

**首次导入不记撤销步**：窗口启动时那张空白画布不算「上一个状态」，否则打开图片后第一次 Ctrl+Z 会把窗口清空。
`_isStartupCanvas` 记录这一点，第二次导入起才可撤销。

## 崩溃与容错

- `CrashReport` 写 `%LOCALAPPDATA%\NaraDreamPainter\crash.log`（便携包可能解压在只读位置，exe 旁边不一定能写），
  同时弹一个 user32 的 `MessageBox`。用 user32 而不是 XAML 对话框：这条路径在 UI 线程已经出问题时执行，
  再创建 XAML 对象很可能跟着失败。文案走 `Crash_Message` 资源键。
- `App` 挂了 `UnhandledException` 与 `TaskScheduler.UnobservedTaskException`（后者 `SetObserved`，避免进程被带走）。
- 打开/拖拽失败、导出失败都回固定中文文案（`Dialog_OpenFailedDetail` / `Dialog_ExportFailedDetail`），
  真实异常写进 crash.log。给用户看读取器抛出的英文异常没有意义。