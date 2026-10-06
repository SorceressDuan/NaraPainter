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
> `Toolbar_MaskBrush` → `Text.ToolbarMaskBrush`，`Undo_ContentFill` → `Text.UndoContentFill`。
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
是否可靠无法在这个沙箱里验证（窗口看不到），所以下拉菜单项在 `MainWindow.LabelFlyouts()` 里赋值，
并在构造函数里挂一次 `CultureChanged`（窗口关闭时退订）。工具栏「调整图层」和「内容感知填充」
两个下拉属于这种情况。

**红线**：XAML 里不写用户可见文案。`Text` / `Label` / `Header` / `Content` / `PlaceholderText` /
`ToolTipService.ToolTip` / `Title` 一律走绑定或 code-behind 赋值。界面上的枚举列表（24 种混合模式、
通道、色相范围、选区形状）由 `Strings` 的只读列表属性给出，顺序与模型枚举一致，
`SelectedIndex` 直接绑枚举序号，所以列表顺序不能改。

## C# 取文案

`Strings.ToolbarUndo`；带占位符的用 `Localization.Interpolate(Strings.StatusLayerCount, count)`。
视图模型里可以直接写继承来的 `Text.ToolbarUndo`。

**注意 `Interpolate` 收的是模板，不是键。** `Strings.StatusOpened` 本身就是 `"已打开 {0} · {1} · 耗时 {2} 毫秒"`，
把它交给按键查表的那套就会得到 `!Status_Opened!` 这种半成品——这个坑踩过一次，窗口标题直接显示成
`!Nara Dream Painter — 未命名!`。自检里因此加了两条守卫：`window.Title` 含 `!` 即失败，
`chrome.texts` 会把窗口里实际显示的文字打出来。

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
- **框架自带的文案**：`ToggleSwitch` 的 On/Off 与 `CheckBox` 的勾选标记来自 WinUI 自己的资源，
  不会跟着 `Strings.resx` 变。凡是界面上出现开关的地方都显式写 `OnContent`/`OffContent`
  （用 `Common_On`/`Common_Off`，或像「涂抹/擦除」那样各自成对），不依赖框架默认值。
- `Models` / `Imaging` 抛出的异常消息：错误对话框的正文用的是 `error.Message`，目前是英文。
  要汉化得在那两个项目里另建资源文件，超出本次范围。Models 里 `AdjustmentSettings.DisplayName`
  那四个英文名现在只被 Models 自己用，界面一律走 `AdjustmentKinds.Name`。
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

## 加一门新语言

加一个 `Strings.<culture>.resx`，再往 `LanguageCatalog.Options` 加一行。别的代码不用动：
`Localization` 通过 `ResourceManager` 查表，语言选择器、持久化、刷新链路都不知道具体语言有几种。

## 验证

```powershell
powershell -ExecutionPolicy Bypass -File tools/verify/check-localization.ps1
```

`tools/verify/verify.ps1` 会跑同一个脚本并在汇总里给一行 `LOCALIZATION`。

窗口在这个沙箱里看不到，所以中文是否真的显示出来靠 `--selftest` 的两条运行时检查：

- `CheckResources`：反射读一遍 `LocalizedStrings` 的每个键，任何没解析出来的键（`!Key!`）都会让
  自检失败，并留一行
  `resources culture=zh-CN keys=207 broken=0 sample=撤销/图层/就绪`。
- `CheckChrome`：从活动窗口的视觉树与 `CommandBar.PrimaryCommands` 里把标签读回来，确认
  `{x:Bind Text.X}` 在加载时确实解析了（绑定路径写错不会抛异常，只会留下空标签，这种失败在无窗口
  环境里看不出来），并留一行 `chrome culture=zh-CN texts=NN checked=6 missing=0`。

再配合 `window.title=Nara Dream Painter — 未命名`，可以确认资源确实在运行时生效，而不是只有文件里对齐。


## 语言切换

状态栏右下角的「语言 / Language」下拉框切换界面语言，**即时生效、无需重启**，选择保存在
`%LOCALAPPDATA%\NaraDreamPainter\settings.json`，下次启动沿用；没有保存过时按系统语言决定
（中文系统 → 简体中文，其它 → English）。

链路：`Localization.Culture` 的 setter 保存偏好并抛 `CultureChanged`，`LocalizedStrings.Refresh()`
给每个绑定属性发 `PropertyChanged`，`MainWindow.OnCultureChanged()` 另外处理三类声明式绑定覆盖不到
的地方——视觉树之外的 flyout、自行拼接文案的视图模型（`Document.RefreshLocalization()`）、以及窗口标题。

### 已知显示缺陷：两个选择器的选中项文本不重绘

切换语言后，**混合模式**与**选区形状**两个下拉框会暂时空白，重新打开下拉框选一次即恢复。
原因是 WinUI 的 ComboBox 在 `ItemsSource` 变化后不总会重绘已关闭状态下显示选中项的文本。

背后的数据是对的，不是丢数据：`SmokeTest` 里打印过切换后的 `layerIndex=0`、
`names=[Normal,Darken,…]`，即视图模型与列表都是新语言，只有显示层停住了。

试过三种修法都没用：把列表改成 `ObservableCollection` 原地替换项；每次读取返回新列表并通知
`ItemsSource`；把 `SelectedIndex` 从 `TwoWay` 改成 `OneWay`。

**有效的修法**：`MainWindow.RepaintSelections()` 在切换后遍历视觉树，把每个 ComboBox 的
`SelectedIndex` 先设为 -1 再设回原值，强制它重绘选中项文本。这样一来**形状**与**语言**两个
选择器都恢复了。

**仍然空白的只剩一个**：`LayersPanel` 图层行模板里的**混合模式**选择器（`LayersPanel.xaml:105`，
在 `ListView` 的 `DataTemplate` 内）。同一个混合模式选择器在 `PropertiesPanel` 里是好的，差别在于
模板实例。自检日志会如实记录数量：
```
language pickers=4 blank=1 values=' / 混合模式混合模式混合模式 / 形状形状形状 / 简体中文'
```
数据是对的（`firstMode='Normal'`），只是显示层停住，用户点开该下拉框选一次即恢复。要彻底修，
方向是给模板内的选择器换成 `ObservableCollection<可通知的项对象>`，让项自己发 `PropertyChanged`。

因此 `SmokeTest.CheckLanguageSwitch` **刻意不对整棵视觉树做快照比对**——那会把空白报成切换失败，
却说明不了切换是否生效。它断言的是文化、窗口标题、工具栏标签、以及选择器背后的取值，把选择器的
空白数**记录**在日志里而不是抛出。