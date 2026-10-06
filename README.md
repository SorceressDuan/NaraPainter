# Nara Dream Painter

> **运行方式：双击根目录的 `NaraDreamPainter.exe`，无需安装 .NET 或 Windows App SDK。**
>
> 或者使用安装包 `NaraDreamPainter-Setup.msix`，装好后从开始菜单启动，桌面会自动生成快捷方式。

Nara Dream Painter 是 [robbietilton/Compositor](https://github.com/robbietilton/Compositor)
的非官方 Windows 移植版。
原项目版权归 Robbie Tilton 所有，采用 MIT 许可证。
本项目与原作者无隶属或背书关系，也不是原项目的官方续作。
原始许可证见 `LICENSE`。

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
- 导出 PNG / JPEG / WebP / TIFF，可选 JPEG 质量

还没做：

- 画笔、橡皮擦、渐变、文字工具
- 图层变换（移动/缩放/旋转/翻转）与图层样式
- PSD 导入，以及 RAW / HEIC

## 界面语言

界面文本全部走资源文件，不硬编码。默认简体中文，系统语言是英文时回退英文：

```
src/NaraDreamPainter.App/Resources/Strings.resx      英文（默认回退）
src/NaraDreamPainter.App/Resources/Strings.zh-CN.resx 简体中文
```

加一门语言就是加一个 `Strings.<culture>.resx`，代码不用动。


## 构建

需要 .NET 8 SDK。

```powershell
dotnet restore
dotnet build src/NaraDreamPainter.App/NaraDreamPainter.App.csproj -c Debug
```

调试构建是自包含的（`SelfContained` + `WindowsAppSDKSelfContained`），
所以产物目录里已经带了 .NET 运行时和 Windows App SDK，不需要另外装运行时，双击 `NaraDreamPainter.exe` 就能跑。

打包成可以直接发给别人的压缩包：

```powershell
powershell -ExecutionPolicy Bypass -File packaging/pack.ps1
```

产物在 `dist/`：一个是自包含的 zip，一个是摊平好的 MSIX 负载目录
（要出 `.msix` 再对那个目录跑一次 `makeappx pack`，签名自备）。

## 测试

```powershell
dotnet test tests/NaraDreamPainter.Tests/NaraDreamPainter.Tests.csproj
```

测试集中在不依赖界面的部分：混合模式数值、调整算法、文件读写往返、图层模型操作。
`tools/verify/verify.ps1` 会把构建、测试和几条结构性检查（比如「仓库里不能出现 Apple 框架引用」）
一次跑完。

## 目录结构

```
src/
  NaraDreamPainter.Models/       数据模型、混合数学、服务契约，不引用任何 Windows API
  NaraDreamPainter.Imaging/      OpenCvSharp4 的图像读写与像素操作
  NaraDreamPainter.Compositing/  Win2D 画布渲染与 GPU 合成
  NaraDreamPainter.App/          WinUI 3 界面（Views / ViewModels / Services / Controls）
tests/
  NaraDreamPainter.Tests/        xUnit 测试
tools/
  assets/                  测试图片与应用图标的生成器
  verify/                  一次性跑完的验收脚本
packaging/                 发布与 MSIX 打包
legacy/                    原项目 Swift 源码（只读参考）
docs/                      移植说明、构建说明、需求文档存档
```

`NaraDreamPainter.Models` 刻意不引用 Windows 相关的包，混合与调整的公式只有一处实现，
界面预览和高分辨率导出走的是同一套代码。

## 许可

MIT，见 [LICENSE](LICENSE)。原项目同样采用 MIT，其许可证全文保留在
[legacy/LICENSE.upstream](legacy/LICENSE.upstream)。`legacy/` 下所有源码的版权归原作者所有。
