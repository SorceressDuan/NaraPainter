# Compositor for Windows（非官方移植版）

> **运行方式：双击根目录的 `Compositor.exe`，无需安装 .NET 或 Windows App SDK。**

本项目基于 [robbietilton/Compositor](https://github.com/robbietilton/Compositor)。
原项目版权归 Robbie Tilton 所有，采用 MIT 许可证。
本项目是非官方 Windows 移植版，与原作者无隶属或背书关系。
原始许可证见 `LICENSE`。

---

## 这是什么

Compositor 原本是一个 macOS 上的图像合成与后期工具，用 Swift、SwiftUI/AppKit 和 Metal 写成。
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
- 调整图层：亮度/对比度、色相/饱和度、色阶、曲线，画布实时预览
- 撤销/重做，一次拖动合并成一步
- 导出 PNG / JPEG / WebP

库里有、界面还没接上的：

- 蒙版画笔（`MaskService.Paint`）与内容感知填充（`Cv2.Inpaint`）

还没做：

- 画笔、橡皮擦、渐变、文字工具
- 图层变换（移动/缩放/旋转/翻转）与图层样式
- PSD 导入

## 构建

需要 .NET 8 SDK。

```powershell
dotnet restore
dotnet build src/Compositor.App/Compositor.App.csproj -c Debug
```

调试构建是自包含的（`SelfContained` + `WindowsAppSDKSelfContained`），
所以产物目录里已经带了 .NET 运行时和 Windows App SDK，不需要另外装运行时，双击 `Compositor.exe` 就能跑。

打包成可以直接发给别人的压缩包：

```powershell
powershell -ExecutionPolicy Bypass -File packaging/pack.ps1
```

产物在 `dist/`：一个是自包含的 zip，一个是摊平好的 MSIX 负载目录
（要出 `.msix` 再对那个目录跑一次 `makeappx pack`，签名自备）。

## 测试

```powershell
dotnet test tests/Compositor.Tests/Compositor.Tests.csproj
```

测试集中在不依赖界面的部分：混合模式数值、调整算法、文件读写往返、图层模型操作。
`tools/verify/verify.ps1` 会把构建、测试和几条结构性检查（比如「仓库里不能出现 Apple 框架引用」）
一次跑完。

## 目录结构

```
src/
  Compositor.Models/       数据模型、混合数学、服务契约，不引用任何 Windows API
  Compositor.Imaging/      OpenCvSharp4 的图像读写与像素操作
  Compositor.Compositing/  Win2D 画布渲染与 GPU 合成
  Compositor.App/          WinUI 3 界面（Views / ViewModels / Services / Controls）
tests/
  Compositor.Tests/        xUnit 测试
tools/
  assets/                  测试图片与应用图标的生成器
  verify/                  一次性跑完的验收脚本
packaging/                 发布与 MSIX 打包
legacy/                    原项目 Swift 源码（只读参考）
docs/                      移植说明、构建说明、需求文档存档
```

`Compositor.Models` 刻意不引用 Windows 相关的包，混合与调整的公式只有一处实现，
界面预览和高分辨率导出走的是同一套代码。

## 许可

MIT，见 [LICENSE](LICENSE)。原项目同样采用 MIT，其许可证全文保留在
[legacy/LICENSE.upstream](legacy/LICENSE.upstream)。`legacy/` 下所有源码的版权归原作者所有。
