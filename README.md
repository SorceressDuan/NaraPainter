# Nara Painter

> **本工具定位为轻量级 Windows 图像编辑器，支持图层、蒙版、内容感知填充、TIFF 导出，
> 不包含 Photoshop 级别的 PSD 导入/导出、CMYK 和矢量功能。**

> **运行方式：解压后双击 `launcher\NaraPainter.exe`。**
>
> 更省事：先双击 `创建桌面快捷方式.bat`，它会在根目录生成 `NaraPainter.lnk`，
> 之后从桌面或开始菜单启动即可。无需安装 .NET 或 Windows App SDK。

Nara Painter 是
[robbietilton/Compositor](https://github.com/robbietilton/Compositor) 的非官方 Windows 移植版。
原项目版权归 Robbie Tilton 所有，采用 MIT 许可证。
本项目与原作者无隶属或背书关系，也不是原项目的官方续作。
原始许可证见 `LICENSE`。

## 发布包里的目录结构

面向使用者的压缩包在根目录只放一个脚本和几份文档，程序都在子文件夹里：

```
创建桌面快捷方式.bat            生成 NaraPainter.lnk，可拖到桌面或开始菜单
RUNNING.txt                   一页说明
README.md  LICENSE
launcher/                     引导程序与它自己的运行库；双击里面的 NaraPainter.exe 启动
app/                          程序本体与运行库，不要移动或删除里面的文件
```

`launcher\NaraPainter.exe` 是个极小的引导程序：启动 `app\` 里的主程序后自己就退出。拆分的原因是
`app/` 里有一百多个运行时文件，全部摊在根目录会让第一次使用的人找不到主程序。
程序放在子目录里能正常启动，这一点是实测过的，不是假设——见 [docs/BUILD.md](docs/BUILD.md)。

---

## 这是什么

上游 Compositor 是一个 macOS 上的图像合成与后期工具，用 Swift、SwiftUI/AppKit 和 Metal 写成。
这个仓库把它搬到 Windows 上，技术栈换成 C# 12 / .NET 8、WinUI 3、Windows App SDK、OpenCvSharp4
和 Win2D。功能范围比原版小很多：先把「打开图片、图层、混合模式、基础调整、导出」这条主线做通，
其余能力按模块逐个补。原始 Swift 源码完整保留在 `legacy/` 下，作为移植时的对照参考，没有删改。

## 目前的状态

已经能用：

- 打开 PNG / JPEG / WebP / BMP / TIFF，拖放或菜单都行
- 画布显示，支持缩放、平移、适应窗口、放大后的像素网格
- 图层的新建、删除、复制、拖拽排序、重命名、显示/隐藏、不透明度
- 24 种混合模式（Photoshop 的完整列表）
- 矩形/椭圆选区转图层蒙版，带羽化
- 蒙版画笔：在蒙版上涂抹、擦除，可调笔刷大小、硬度、不透明度
- 内容感知填充：选区工具下调用 OpenCV 的 Inpaint 补掉选中的内容
- 调整图层：亮度/对比度、色相/饱和度、色阶、曲线，画布实时预览
- 撤销/重做，一次拖动合并成一步
- 导出 PNG / JPEG / WebP / TIFF，可选 JPEG 质量，TIFF 支持四种压缩

## 快捷键

| 按键 | 作用 |
| --- | --- |
| `Ctrl+O` | 打开图片 |
| `Ctrl+E` | 导出 |
| `Ctrl+Z` / `Ctrl+Y` | 撤销 / 重做 |
| `Ctrl+N` | 新建图层 |
| `Ctrl+J` | 复制当前图层 |
| `Ctrl+A` | 全选（整幅画布作为选区） |
| `Ctrl+D` | 取消选区 |
| `Ctrl` `+` / `Ctrl` `-` | 放大 / 缩小 |
| `Ctrl+0` | 适应窗口 |
| `Ctrl+1` | 实际大小（100%） |
| `M` | 切换蒙版画笔 |
| `I` | 切换吸管 |
| `Ctrl+Shift+X` | 裁剪到选区 |
| `Ctrl+Shift+L` / `Ctrl+Shift+R` | 顺时针 / 逆时针旋转 90° |
| `Ctrl+Shift+H` / `Ctrl+Shift+V` | 水平 / 垂直翻转 |
| `Ctrl+Alt+C` | 调整画布尺寸 |
| `Ctrl+Shift+B` / `Ctrl+Shift+U` | 高斯模糊 / USM 锐化 |
| `Esc` | 放弃正在画的这一笔（不写入蒙版） |

界面上都能看到这些键：菜单项后面直接跟着按键，工具栏按钮的提示里也写着。

没给裁剪用 `C`：本工具是**一键裁剪**而不是进入裁剪模式，一个裸字母太容易误触。
工具类按键（`M`、`I`）与 `Esc` 之外，其余快捷键都带修饰键，这样在输入框里打字不会被抢键。

有焦点的控件优先：在图层名输入框里打字、拖动滑块时，快捷键不会触发。
这条规则由 WinUI 的按键路由保证，**在进程内无法模拟验证**（相关 API 是事件而不是方法），
所以自检只强制了「除工具键与 Esc 外不得出现裸字母」和「快捷键表无重复」两条前置条件。

**清单里没有绑上的键**：`Ctrl+S` / `Ctrl+Shift+S`（另存为）没有对应功能——本工具只有「导出」；
`Ctrl+Shift+N` 与 `Ctrl+N` 重复，只保留一个；`Ctrl+G`（编组）与 `Ctrl+E` 用作合并图层（已被导出占用）
因为功能不存在而无法绑定；空格临时抓手与 `V`/`B`/`E` 工具切换同理——没有对应的工具可切换。

## 还没做

完整盘点见 [docs/MISSING_FEATURES.md](docs/MISSING_FEATURES.md)。要点：

- 文字工具、修复画笔 / 污点修复 / 仿制图章
- 完整画笔引擎（按颜色作画、压感、笔刷预设）与橡皮擦
- 图层样式（投影、内阴影、外发光、描边）、图层编组
- 矢量工具（路径、形状图层、矢量蒙版）
- 裁剪 / 旋转 / 自由变换、图像尺寸调整
- PSD 导入导出、CMYK、RAW、智能对象、动作与批处理、插件

本工具定位为**轻量级编辑器**，能做「打开 → 调整图层 → 合层 → 导出」这条主线，
不是 Photoshop 的替代品。

## 界面语言

界面文本全部走资源文件，不硬编码。默认简体中文，系统语言是英文时回退英文：

```
src/NaraPainter.App/Resources/Strings.resx      英文（默认回退）
src/NaraPainter.App/Resources/Strings.zh-CN.resx 简体中文
```

加一门语言就是加一个 `Strings.<culture>.resx`，代码不用动。
界面里怎么取文案（XAML 的 `{x:Bind Text.X}` 约定、加键步骤、字体与不翻译清单）见
[docs/modules/LOCALIZATION.md](docs/modules/LOCALIZATION.md)。


## 构建

需要 .NET 8 SDK。

```powershell
dotnet restore
dotnet build src/NaraPainter.App/NaraPainter.App.csproj -c Debug
```

调试构建是自包含的（`SelfContained` + `WindowsAppSDKSelfContained`），
所以产物目录里已经带了 .NET 运行时和 Windows App SDK，不需要另外装运行时，双击 `NaraPainter.exe` 就能跑。

打包成可以直接发给别人的压缩包：

```powershell
powershell -ExecutionPolicy Bypass -File packaging/pack.ps1
```

产物在 `dist/`：一个是自包含的 zip，一个是摊平好的 MSIX 负载目录
（要出 `.msix` 再对那个目录跑一次 `makeappx pack`，签名自备）。

## 测试

```powershell
dotnet test tests/NaraPainter.Tests/NaraPainter.Tests.csproj
```

测试集中在不依赖界面的部分：混合模式数值、调整算法、文件读写往返、图层模型操作。
`tools/verify/verify.ps1` 会把构建、测试和几条结构性检查（比如「仓库里不能出现 Apple 框架引用」）
一次跑完。

## 目录结构

```
src/
  NaraPainter.Models/       数据模型、混合数学、服务契约，不引用任何 Windows API
  NaraPainter.Imaging/      OpenCvSharp4 的图像读写与像素操作
  NaraPainter.Compositing/  Win2D 画布渲染与 GPU 合成
  NaraPainter.App/          WinUI 3 界面（Views / ViewModels / Services / Controls）
tests/
  NaraPainter.Tests/        xUnit 测试
tools/
  assets/                  测试图片与应用图标的生成器
  verify/                  一次性跑完的验收脚本
packaging/                 发布与 MSIX 打包
legacy/                    原项目 Swift 源码（只读参考）
docs/                      移植说明、构建说明、需求文档存档
```

`NaraPainter.Models` 刻意不引用 Windows 相关的包，混合与调整的公式只有一处实现，
界面预览和高分辨率导出走的是同一套代码。

## 许可

MIT，见 [LICENSE](LICENSE)。原项目同样采用 MIT，其许可证全文保留在
[legacy/LICENSE.upstream](legacy/LICENSE.upstream)。`legacy/` 下所有源码的版权归原作者所有。
