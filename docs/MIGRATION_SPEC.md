# Compositor Windows 移植需求文档

本文档是本次移植工作的任务书与验收依据。项目来源与合规声明见根目录 [README](../README.md)。

## 0. 项目背景与合规前提

- 原项目：[robbietilton/Compositor](https://github.com/robbietilton/Compositor)
- 原项目采用 MIT 许可证。已确认：允许使用、修改、分发和发布。
- 本项目目标是制作**非官方 Windows 移植版**。
- 不需要联系原作者，不需要写「特别鸣谢」。

合规要求：

1. 项目根目录保留原项目 LICENSE 全文。
2. README 中客观写明来源、原作者、MIT 协议、非官方移植、无隶属或背书关系。
3. 不删除原源码中的版权声明。
4. 不暗示原作者认可或参与本项目。

## 1. 移植目标

将 Compositor 从 macOS 原生应用迁移为 Windows 原生应用。

优先级：先做最小可用版 MVP，再逐步补全功能。

MVP 必须包含：

- 打开图片
- 显示画布
- 新建/删除/排序图层
- 至少 3 种混合模式
- 基础调整：亮度、对比度、色相/饱和度
- 导出图片

## 2. 目标技术栈

| 原 macOS | 目标 Windows |
| --- | --- |
| Swift 5.9+ | C# 12 / .NET 8+ |
| SwiftUI + AppKit | WinUI 3 + Windows App SDK 1.6+ |
| Metal | Win2D 或 ComputeSharp.D2D1 |
| Apple Vision | OpenCvSharp4 |
| Core Image | OpenCvSharp4 Photo 模块 |
| Foundation | System.IO / Windows.Storage |
| Xcode | MSBuild / dotnet CLI |

NuGet 依赖：Microsoft.WindowsAppSDK、OpenCvSharp4、OpenCvSharp4.runtime.win、Win2D 或 ComputeSharp.D2D1。

## 3. 技术红线

1. 语言：C# 12 / .NET 8+，禁止 Swift。
2. UI：WinUI 3，禁止 WPF、WinForms、UWP。
3. 图像处理：统一走 OpenCvSharp4，禁止用 System.Drawing 做像素级操作。
4. 渲染：GPU 效果用 Win2D 或 ComputeSharp.D2D1。
5. 禁止出现任何 Apple 框架引用。
6. UI 命名空间用 `Microsoft.UI.Xaml.*`，禁止 `Windows.UI.Xaml.*`。
7. 异步：Swift async/await 映射到 C# async/await。
8. 不删除原 Swift 代码；放在 `legacy/` 目录作为参考。

## 4. 分阶段任务

### 阶段 0：合规与项目骨架

创建 WinUI 3 项目、配置 NuGet 依赖、放入原 LICENSE、写好 README 来源声明、
建立 `Models/`、`Services/`、`Views/`、`ViewModels/`、`legacy/` 目录、确保 `dotnet build` 通过。

### 阶段 1：纯逻辑迁移

翻译规则：

| Swift | C# |
| --- | --- |
| `T?` | `T?` |
| protocol + extension | interface + 扩展方法 |
| 闭包 | `Func<>` / `Action<>` |
| 带关联值 enum | 带参数 enum 或 record |
| `guard let` | early return |
| ARC | GC，非托管资源实现 `IDisposable` |

任务：迁移数据模型（图层、蒙版、选区、颜色）、混合模式数学、调整算法
（亮度、对比度、色相/饱和度、曲线、色阶）。所有图像操作调用 `Cv2.*`。

### 阶段 2：平台 API 替换

| 原功能 | 替换 |
| --- | --- |
| Vision 对象检测 / 主体选择 | `Cv2.FindContours()` / `Cv2.GrabCut()` |
| 内容感知填充 | `Cv2.Inpaint()` |
| 模糊 / 锐化 | `Cv2.GaussianBlur()` / `Cv2.Laplacian()` |
| 颜色空间转换 | `Cv2.CvtColor()` |
| Metal 纹理合成 | Win2D `CanvasRenderTarget` |

每个替换标注 `// MIGRATION: 原API -> 新API`，不确定的写 `// TODO-HUMAN: 需人工确认，原因...`。

### 阶段 3：UI 重写

- 主窗口：左侧图层面板 + 中央画布 + 右侧属性面板
- 画布：Win2D `CanvasControl`，支持缩放、平移
- 图层面板：列表、拖拽排序、透明度、混合模式、可见性
- 工具栏：画笔、橡皮擦、选区、渐变、文字
- 调整面板：滑条、输入框、曲线控件

控件映射：

| AppKit | WinUI 3 |
| --- | --- |
| `NSSlider` | `Slider` |
| `NSColorWell` | `ColorPicker` |
| `NSPopUpButton` | `ComboBox` |
| `NSTableView` | `ListView` + `DataTemplate` |
| `NSMenu` | `MenuFlyout` / `CommandBar` |

### 阶段 4：MVP 集成验证

打开/保存 PNG、JPEG、WebP；创建/删除/重排/合并图层；应用至少 3 种混合模式；
绘制蒙版并模糊/羽化；执行一次内容感知填充；调整色阶和曲线；导出并对比原应用输出。

### 阶段 5：打包发布

生成 MSIX 或独立发布包；保留 LICENSE 和 README 来源声明；确认无 macOS 依赖。

## 5. AI 工作规则

应该做：批量翻译纯逻辑代码；将 Apple API 替换为 OpenCvSharp / Win2D 并标注 `// MIGRATION`；
生成 WinUI 3 XAML；不确定时写 `// TODO-HUMAN`。

不应该做：修改 UI 交互逻辑后不说明；假设每个 Vision API 都有直接 OpenCV 对应函数；
一次混合多个模块迁移；删除原 Swift 代码；输出无法编译的伪代码。

每次只处理一个模块。输出：修改/新增文件列表、完整代码、迁移说明、待人工确认项。

## 5.1 代码风格与去 AI 痕迹要求

1. 注释克制：只在复杂逻辑、非显而易见的地方写注释。
2. 避免模板化注释：禁止 `// TODO: Implement this`、`// Add your code here`、`// 这里可以根据需要修改`。
3. 命名自然：使用常见人类命名习惯，不要机械翻译 Swift 命名。
4. 代码格式统一：Allman 花括号、4 空格缩进，不要多余空行与过度对齐。
5. 不写教程式代码：不要每个方法都配 `/// <summary>` 说明显而易见的功能。
6. 文档语言自然：不要出现「作为 AI」「根据你的要求」「希望这对你有帮助」等对话式残留。
7. 保持一致性：同一模块内风格统一。
8. 不要刻意模仿原项目：用 C# 社区常见写法，不要生硬保留 Swift 风格。

## 6. 验收标准

- `dotnet build` 零错误
- MVP 功能全部可用
- 无 `TODO-HUMAN` 未解决项
- 所有 `// MIGRATION` 都有对应替换代码
- 打开 4000×3000 图片到可编辑状态 < 2 秒
- 项目中不出现任何 Apple 框架引用
- 根目录有原 LICENSE
- README 有非官方移植声明
- 不暗示原作者背书
- 代码审查时不应出现明显 AI 生成特征：模板注释、对话式残留、过度统一的机械结构
- README 和文档应读起来像人类开发者写的项目说明
