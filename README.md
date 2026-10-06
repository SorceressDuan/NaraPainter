# Compositor for Windows（非官方移植版）

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
- 画布显示，支持缩放、平移、适应窗口
- 图层的新建、删除、复制、拖拽排序、重命名、显示/隐藏、不透明度
- 24 种混合模式（Photoshop 的完整列表）
- 调整：亮度/对比度、色相/饱和度、色阶、曲线
- 导出 PNG / JPEG / WebP

还在做：

- 选区与蒙版的可视化编辑
- 内容感知填充（`Cv2.Inpaint`）
- 图层样式、变换、文字工具
- PSD 导入

## 构建

需要 .NET 8 SDK。仓库里没有 `.tools/`，那是本地开发用的工具链目录，已被 git 忽略。

```powershell
dotnet restore
dotnet build src/Compositor.App/Compositor.App.csproj -c Debug
```

调试构建是自包含的（`SelfContained` + `WindowsAppSDKSelfContained`），
所以产物目录里已经带了 .NET 运行时和 Windows App SDK，不需要另外装运行时，双击 `Compositor.exe` 就能跑。

打包 MSIX：

```powershell
dotnet publish src/Compositor.App/Compositor.App.csproj -c Release -p:Platform=x64
```

## 测试

```powershell
dotnet test tests/Compositor.Tests/Compositor.Tests.csproj
```

测试集中在不依赖界面的部分：混合模式数值、调整算法、文件读写往返、图层模型操作。

## 目录结构

```
src/
  Compositor.Core/         数据模型与混合数学，不引用任何 Windows API
  Compositor.Imaging/      OpenCvSharp4 的图像读写与像素操作
  Compositor.Compositing/  Win2D 画布渲染与 GPU 合成
  Compositor.App/          WinUI 3 界面
tests/
  Compositor.Tests/        xUnit 测试
legacy/                    原项目 Swift 源码（只读参考）
docs/                      移植说明与格式文档
assets/                    图标与测试图片
```

`Compositor.Core` 刻意不引用 Windows 相关的包，混合与调整的公式只有一处实现，
界面和高分辨率导出走的是同一套代码。

## 许可

MIT，见 [LICENSE](LICENSE)。原项目同样采用 MIT，其许可证全文保留在
[legacy/LICENSE.upstream](legacy/LICENSE.upstream)。`legacy/` 下所有源码的版权归原作者所有。
