# 本地化（NaraDreamPainter.App）

界面文案全部走资源文件，代码与 XAML 里不出现用户可见的字面量。
这份文档写清**取文案的方式**（XAML 评审按这个来）、加键的步骤、以及哪些东西故意不翻译。

## 资源在哪

| 文件 | 作用 |
| --- | --- |
| `Resources/Strings.resx` | 英文，中性回退。缺键时 ResourceManager 落回这里 |
| `Resources/Strings.zh-CN.resx` | 简体中文。键集合必须与英文完全一致 |
| `Strings.cs` | C# 用的强类型访问器，一个键一个静态属性 |
| `LocalizedStrings.cs` | XAML 用的可绑定包装（单例），一个键一个实例属性，转发给 `Strings` |
| `App.xaml` 的 `AppFontFamily` | 界面字体，中英混排共用一个族 |

## 语言怎么选

`Localization.PickStartupCulture`：系统是 `zh-*` 用 `zh-CN`，其余一律英文——中文是这个构建的目标语言，
英文系统不该看到中文。加一门语言就是加一个 `Strings.<culture>.resx`，代码不用动。

`Localization.Culture` 可以在运行时改，改完抛 `CultureChanged`；本轮没有接运行时切换菜单，见文末。

## XAML 取文案的约定

**一律 `{x:Bind Text.<属性名>, Mode=OneWay}`**：

```xml
<TextBlock Text="{x:Bind Text.LayersTitle, Mode=OneWay}" />
<AppBarButton Label="{x:Bind Text.ToolbarUndo, Mode=OneWay}" />
```

> **最容易写错的一条：属性名是资源键去掉下划线。** `Toolbar_Open` → `Text.ToolbarOpen`，
> `Mask_BrushTool` → `Text.MaskBrushTool`，`Undo_ContentFill` → `Text.UndoContentFill`。
> 写成 `Text.Toolbar_Open` 编译不过（`LocalizedStrings` 没有这个属性），但这正是要的效果——
> 键写错是构建错误，不是运行时空标签。`Strings.cs` 里的静态属性同理，规则由
> `LocalizationTests` 断言。

- `Text` 是每个视图 code-behind 上的属性，值就是 `LocalizedStrings.Instance`。
- `DataTemplate` 里绑的是数据项，所以走视图模型基类上的同一个属性：
  `<DataTemplate x:DataType="vm:LayerViewModel">` + `{x:Bind Text.LayersVisibilityHint}`。
- `Mode=OneWay` 不能省：`x:Bind` 默认 `OneTime`，那样切语言不会刷新。

选它的理由：

- **键写错是编译错误**，不是运行时空标签。这是这个方案相对 `x:Uid` 的主要好处。
- `x:Uid` 要求把文案再抄一份到 `.resw`（键名形如 `元素名.Content`），等于第二套资源库，
  和 `Strings.resx` 必然漂移；`x:Static` 在 WinUI 的 XAML 里不被支持，静态类也无法通知绑定刷新。
- 一个机制覆盖 `Window`、`UserControl`、`DataTemplate`，不需要给每个控件起 `x:Name` 再在
  code-behind 里赋值。

**例外：`MenuFlyout` / `Flyout` 里的项。** 它们不在页面视觉树里，`x:Bind` 在懒加载的 Flyout 内容里
是否可靠无法在这个沙箱里验证（窗口看不到），所以下拉菜单项在 code-behind 的 `ApplyText()` 里赋值，
并订阅 `CultureChanged` 重设。工具栏「调整图层」和「内容感知填充」两个下拉属于这种情况。

**红线**：XAML 里不写用户可见文案。`Text` / `Label` / `Header` / `Content` / `PlaceholderText` /
`ToolTipService.ToolTip` / `Title` 一律走绑定或 code-behind 赋值。界面上的枚举列表（24 种混合模式、
通道、色相范围、选区形状）由 `Strings` 的只读列表属性给出，顺序与模型枚举一致，
`SelectedIndex` 直接绑枚举序号，所以列表顺序不能改。

## C# 取文案

`Strings.ToolbarUndo`；带占位符的用 `Localization.Format(Strings.StatusLayerCount, count)`。
视图模型里可以直接写继承来的 `Text.ToolbarUndo`。

命名规则：**属性名 == 资源键去掉下划线**（`Toolbar_Open` → `ToolbarOpen`），
`tests/NaraDreamPainter.Tests/LocalizationTests.cs` 会断言这条，写错名字测试就红。

键按界面分区：`App_`、`Status_`、`Toolbar_`、`Layers_`、`Properties_`、`Adjust_`、`Channel_`、
`Range_`、`Curve_`、`BlendMode_`、`Shape_`、`Mask_`、`Selection_`、`Fill_`、`Undo_`、`Format_`、
`Dialog_`、`Drop_`。同一句话在不同角色下允许占两个键（工具栏的 `Toolbar_ClearMask` 与历史里的
`Undo_ClearMask`），这样翻译可以分别走、将来也能各自改。

## 加一个键

1. `Resources/Strings.resx` 加 `<data name="...">`（英文，真英文，不是占位）。
2. `Resources/Strings.zh-CN.resx` 加同名条目（中文要像人写的界面文案）。
3. `Strings.cs` 加 `public static string Xxx => Localization.Get("...");`。
4. `LocalizedStrings.cs` 加 `public string Xxx => Strings.Xxx;`（XAML 要用的话）。
5. 跑 `powershell -ExecutionPolicy Bypass -File tools/verify/check-localization.ps1`：
   它核对两 resx 键集合、`Strings.cs` ↔ resx 一一对应、包装类与 `Strings.cs` 一致、
   XAML 没有硬编码文案、代码里按名字取的键都存在。`dotnet test` 里的 `LocalizationTests` 覆盖其中一部分。

## 不翻译的东西

- 数字与符号：`800 × 600`、`100%`、`Ctrl+Z` 的键名、`×` 分隔符留在代码里。带单位或语序的
  （「耗时 30 毫秒」）走资源。
- `Models` / `Imaging` 抛出的异常消息：错误对话框的正文用的是 `error.Message`，目前是英文。
  要汉化得在那两个项目里另建资源文件，超出本次范围。
- 自检日志（`selftest.log`、`startup.log`）：保持 ASCII，方便脚本比对。
- 撤销历史条目的名字：在记录那一刻按当时的语言定格，切换语言不会改写已经发生的那一步
  （`UndoStack` 存的是字符串）。

## 字体

`AppFontFamily` = `Microsoft YaHei UI`，**单一族名**，不写逗号列表：逗号回退链是 WPF 的 XAML 语义，
WinUI 3 没有承诺；整串被当成一个非法族名时会**静默**落回默认字体，这种失败没有任何报错。
族名不存在时 DirectWrite 会逐字回退，中文照样出字、不出方块，中英混排也共用一次回退结果。

`FontFamily` 只挂在 `Control` 上（`Grid` 没有这个属性），靠属性继承往下传，所以位置是：

- `Views/LayersPanel.xaml`、`Views/PropertiesPanel.xaml` 的 `UserControl` 根；
- `Views/MainWindow.xaml` 的 `CommandBar` 与状态栏文本；
- 错误对话框 `ContentDialog`：它不在窗口视觉树里，继承不到，必须单独设。

## 运行时切换语言（本轮未接菜单）

资源结构本身已经支持多语言，缺的只是一个入口。V0.2 不做语言菜单，因为它要动三个面板与视图模型，
而这一轮的交付重点是主线功能；加一个新语言仍然只需要加一个 `Strings.<culture>.resx`。

将来接入时的位置：

- `Localization.Culture` 的 setter 已经会抛 `CultureChanged`，切过去之后所有取文案的地方都会读到新语言。
- `LocalizedStrings` 在构造函数里订阅了 `CultureChanged`，切换时对自己的每个属性发一次
  `PropertyChanged`，XAML 的 `Mode=OneWay` 绑定随之更新——不需要重建窗口（重建会丢掉当前文档）。
- 视图模型里的文案（图层行标签、调整名、选区状态）由一个 `DocumentViewModel.RefreshLocalization()`
  逐级 `OnPropertyChanged` 通知；窗口标题在 `MainWindow` 里重设（`Title = Document.WindowTitle`）。
- `MenuFlyout` 里的项是 code-behind 赋值的，同样订阅 `CultureChanged` 重设。

## 验证

```powershell
powershell -ExecutionPolicy Bypass -File tools/verify/check-localization.ps1
```

`tools/verify/verify.ps1` 会跑同一个脚本并在汇总里给一行 `LOCALIZATION`。

窗口在这个沙箱里看不到，所以中文是否真的显示出来靠 `--selftest`：它通过反射读一遍
`LocalizedStrings` 的每个键，任何没解析出来的键（`!Key!`）都会让自检失败，并在
`startup.log` / `selftest.log` 里留一行

```
resources culture=zh-CN keys=NNN broken=0 sample=撤销/图层/就绪
```

再配合 `window.title=Nara Dream Painter — 未命名`，可以确认资源确实在运行时生效，而不是只有文件里对齐。
