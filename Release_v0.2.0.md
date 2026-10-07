# v0.2.0 — 那菈绘梦 / Nara Painter

轻量级 Windows 图像编辑器，图层 + 蒙版 + 内容感知填充 + TIFF 导出。这是开源的第一个正式版本。

**下载**：`NaraPainter-0.2.0-win-x64.zip`（149.69 MB）

- SHA-256：`7A00DDAB4A360D8FBDD648BD4DEF91136333306F92A96E8F6DDEE88B0A7D5BC5`
- 解压后双击 `launcher\NaraPainter.exe` 启动；想放桌面就再双击 `创建桌面快捷方式.bat`
- 便携版，**无需安装 .NET 或 Windows App SDK**

---

## 核心功能

| 分类 | 内容 |
| --- | --- |
| **图层** | 新建、复制、删除、拖拽排序、重命名、显示/隐藏、不透明度、**24 种混合模式**（含全部 W3C 非分离模式） |
| **调整图层** | 亮度/对比度、色相/饱和度、色阶、曲线，画布实时预览 |
| **选区与蒙版** | 矩形/椭圆选区、羽化、应用为蒙版、全选、取消选区、清除蒙版 |
| **蒙版画笔** | 涂抹/擦除，可调大小、硬度、不透明度；整条笔画算一个撤销步 |
| **内容感知填充** | 按选区或蒙版挖洞自动补内容，Telea / Nikolai 两种算法 |
| **裁剪 / 旋转 / 翻转** | 裁剪到选区；顺时针/逆时针 90°；水平/垂直翻转 |
| **尺寸调整** | 改画布尺寸（可保持长宽比）；**导出时也能改尺寸与质量** |
| **吸管** | 点画布取色，状态栏显示色块、`#RRGGBB` 与 RGB 分量，一键复制 |
| **滤镜** | 高斯模糊、USM 锐化（可调半径与强度） |
| **导出** | PNG / JPEG / BMP / WebP / TIFF；JPEG 可选质量，TIFF 支持四种压缩且保留 alpha |
| **撤销 / 重做** | 每一步都有名称；一次拖动 = 一个撤销步；导入图片也可撤销 |
| **界面语言** | 简体中文与 English，**即时切换无需重启**，选择会被记住 |

已验证的性能：打开 4000 × 3000 图片约 **190 ms**（Release，自包含构建）。

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
有焦点的控件优先——在图层名输入框里打字、拖动滑块时快捷键不会抢键。

## 系统要求

- **Windows 10 1809（build 17763）或更高**，64 位
- **不需要安装 .NET**，也不需要 Windows App SDK；运行时随包提供
- 约 300 MB 磁盘空间（解压后）

## 已知问题

- **图层面板的混合模式下拉框在切换界面语言后会短暂空白**，点开一次即恢复。
  底层数据是对的（图层混合模式没有被改动），只是 WinUI 已关闭的下拉框不重绘选中项文本。
  同一个选择器在右侧属性面板里正常。详见
  [docs/modules/LOCALIZATION.md](https://github.com/SorceressDuan/NaraPainter/blob/main/docs/modules/LOCALIZATION.md)。
- 首次运行可能出现 SmartScreen 提示，选「更多信息」→「仍要运行」。
- 安装包（MSIX）**未随本版本提供**：签名证书无法在当前构建环境生成，详见
  [installer/README.md](https://github.com/SorceressDuan/NaraPainter/blob/main/installer/README.md)。

## 这个版本不做什么

本工具定位为**轻量级编辑器**，不做 Photoshop 的替代品。完整清单见
[docs/MISSING_FEATURES.md](https://github.com/SorceressDuan/NaraPainter/blob/main/docs/MISSING_FEATURES.md)，要点：

文字工具、修复画笔 / 污点修复 / 仿制图章、完整画笔引擎与橡皮擦、图层样式与图层组、
矢量工具（路径 / 形状图层 / 矢量蒙版）、PSD 导入导出、CMYK、RAW、智能对象、
动作与批处理、插件体系。

## 开源声明

本项目是 [robbietilton/Compositor](https://github.com/robbietilton/Compositor) 的
**非官方 Windows 移植版**。原项目是一个 macOS 上的图像合成工具，用 Swift / SwiftUI / AppKit / Metal 写成。

- 原项目版权归 **Robbie Tilton** 所有，采用 **MIT 许可证**
- 本项目同样以 **MIT 许可证**发布，全文见
  [LICENSE](https://github.com/SorceressDuan/NaraPainter/blob/main/LICENSE)
- **与原作者无隶属或背书关系**，也不是原项目的官方续作
- 原始 Swift 源码完整保留在 `legacy/` 下作为移植对照，未作删改；其许可证全文见
  [legacy/LICENSE.upstream](https://github.com/SorceressDuan/NaraPainter/blob/main/legacy/LICENSE.upstream)

技术栈：C# 12 / .NET 8、WinUI 3 + Windows App SDK 1.6+、OpenCvSharp4、Win2D。
