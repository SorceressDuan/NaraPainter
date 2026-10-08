# 内置教程/帮助 方案

> **状态：第一版已实现**（2026-10）。方案与最终实现的差异记在文末「八、实现记录」。
> 下面的设计说明保留原样，作为当初的决策依据。

给新手一个能自己看懂的入口。

---

## 一、入口放哪

三个候选，我建议第一个：

| 方案 | 位置 | 好处 | 坏处 |
| --- | --- | --- | --- |
| **A. 状态栏按钮（推荐）** | 右下角「关于」旁边 | 常驻可见、不占工具栏、改一行 XAML | 状态栏已经比较满 |
| B. 工具栏按钮 | CommandBar 里 | 更显眼 | 窄窗口会被收进「更多选项」；`F1` 才是习惯位置 |
| C. 帮助菜单 | 需要新建菜单栏 | 最规范 | 现在没有菜单栏，为这一个功能加一整条不划算 |

**建议**：状态栏加一个「教程」按钮（`关于` 左边），同时绑 **`F1`**。
`F1` 是 Windows 上的通用习惯，而且不和现有 29 个快捷键冲突（现有表里没有 F 系列键）。

---

## 二、图文内容怎么承载

四种做法，推荐 **B**：

| 方案 | 做法 | 评价 |
| --- | --- | --- |
| A. 硬编码在 C# 里 | 步骤写在代码里 | 加一步就要改代码、重新编译；中英双语要写两遍 |
| **B. JSON 资源 + 图片占位（推荐）** | 结构写在 JSON，文案走现有 `.resx`，图片走文件夹 | **文案复用现有本地化机制**，加步骤只改 JSON |
| C. Markdown 文件 | 用 Markdown 渲染 | WinUI 3 **没有内置 Markdown 渲染器**，要引第三方包；而我们**只能离线 NuGet**，装不了 |
| D. 纯 HTML + WebView2 | 内嵌浏览器 | 体积大增（WebView2 运行时约 150 MB），与「轻量级」定位冲突 |

**选 B 的决定性理由**：C 和 D 都需要新依赖，而本项目的 `.tools/local-feed` 是**离线源，装不了新包**。
B 不需要任何新依赖。

### 目录结构

```
src/NaraPainter.App/Assets/Tutorial/
  tutorial.json           ← 步骤结构（中英共用，文案用 key）
  images/
    open-01.png           ← 先由设计师或手工截图提供，现在留占位
    open-02.png
    layers-01.png
    ...
```

### `tutorial.json` 结构

```json
{
  "version": 1,
  "chapters": [
    {
      "id": "open",
      "titleKey": "Tutorial_OpenTitle",
      "steps": [
        {
          "textKey": "Tutorial_OpenStep1",
          "image": "open-01.png",
          "imageWidth": 800,
          "imageHeight": 600
        },
        {
          "textKey": "Tutorial_OpenStep2",
          "image": "open-02.png",
          "imageWidth": 800,
          "imageHeight": 520
        }
      ]
    }
  ]
}
```

- `titleKey` / `textKey` 指向 `Strings.resx`，**沿用现有的中英双语机制**
  （`check-localization.ps1` 会自动管住两边键一致）。
- `image` 是 `Assets/Tutorial/images/` 下的文件名。
- `imageWidth/imageHeight` 是**建议尺寸**，加载后按窗口宽度等比缩放，避免图片把弹窗撑爆。

---

## 三、弹窗长什么样

```
┌──────────────────────────────────────────────────┐
│  使用说明                                    ✕   │
├────────────┬─────────────────────────────────────┤
│  打开图片   │  ① 点左上角的「打开」按钮           │
│  新建图层   │     ┌───────────────────────┐       │
│  污点修复   │     │                       │       │
│  导出       │     │    （图片占位）        │       │
│            │     │     800 × 600         │       │
│            │     └───────────────────────┘       │
│            │  ② 选一张 PNG 或 JPEG               │
├────────────┴─────────────────────────────────────┤
│                            [ 上一步 ]  [ 下一步 ] │
└──────────────────────────────────────────────────┘
```

- 左边章节列表，右边图文，底部翻页。**一屏一章，不做长滚动**。
- 图片没到位时显示占位框 + 建议尺寸文字，**不报错、不留空白**。
- 用 `ContentDialog` 实现，和现有 `TextDialog` 一致，不需要新窗口。

---

## 四、内容清单（第一批四章）

按你说的四项，每章 3~5 步：

### 第 1 章 打开图片
| 步 | 文案（要点） | 图片 |
| --- | --- | --- |
| 1 | 点工具栏的「打开」，或按 `Ctrl+O` | `open-01.png` 800×600 |
| 2 | 选一张 PNG / JPEG / WebP / BMP / TIFF | `open-02.png` 800×600 |
| 3 | 拖放文件到窗口也能打开 | `open-03.png` 800×600 |

### 第 2 章 新建与整理图层
| 步 | 文案要点 | 图片 |
| --- | --- | --- |
| 1 | 点图层面板底部的「+」新建图层，或 `Ctrl+N` | `layers-01.png` 800×600 |
| 2 | 每行能改名、调不透明度、换混合模式 | `layers-02.png` 800×600 |
| 3 | 选中一层按 `Ctrl+G` 建文件夹，可折叠 | `layers-03.png` 800×600 |
| 4 | 拖动图层可以放进文件夹；拖到行的上下缘是插到上下方 | `layers-04.png` 800×600 |
| 5 | `Ctrl+E` 向下合并，一步 `Ctrl+Z` 还原 | `layers-05.png` 800×600 |

### 第 3 章 污点修复
| 步 | 文案要点 | 图片 |
| --- | --- | --- |
| 1 | 按 `J` 打开污点修复（右侧属性面板可调大小、硬度） | `heal-01.png` 800×600 |
| 2 | 在瑕疵上按住拖动，划出要修的范围 | `heal-02.png` 800×600 |
| 3 | 松手时自动用周围纹理补回来 | `heal-03.png` 800×600 |
| 4 | 不满意就 `Ctrl+Z`，整条笔画一步撤销 | `heal-04.png` 800×600 |

### 第 4 章 导出
| 步 | 文案要点 | 图片 |
| --- | --- | --- |
| 1 | 按 `Ctrl+S`，或点工具栏的「导出」 | `export-01.png` 800×600 |
| 2 | 可选格式：PNG / JPEG / BMP / WebP / TIFF | `export-02.png` 800×600 |
| 3 | JPEG 可调质量，导出时也能改尺寸 | `export-03.png` 800×600 |
| 4 | 选好路径保存 | `export-04.png` 800×600 |

**共 16 步、16 张图。** 每张统一 **800×600**，等比缩放到弹窗宽度即可。

---

## 五、实现难度与工作量

| 部分 | 难度 | 说明 |
| --- | --- | --- |
| JSON 模型 + 读取 | 低 | 一个 record + `System.Text.Json`（.NET 内置，**无新依赖**） |
| 弹窗界面 | 中 | `ContentDialog` + `NavigationView` 或 `ListView`，约 150 行 XAML |
| 章节/翻页逻辑 | 低 | 一个 `TutorialViewModel`，约 80 行 |
| 文案（中英 16×2 条） | 低 | 走 `.resx`，`check-localization.ps1` 自动管 |
| 图片缺失时的占位 | 低 | 加载失败画一个虚线框 + 尺寸文字 |
| `F1` 快捷键 | 低 | `Shortcuts()` 表加一行 |
| **测试** | 中 | 见下 |

**纯代码工作量估计：半天到一天。**

### 测试怎么写（不依赖截图）

| 测试 | 断言什么 |
| --- | --- |
| JSON 能解析 | `tutorial.json` 读得出来，`version` 是 1 |
| **键都存在** | 每个 `titleKey` / `textKey` 在 `.resx` 里有对应项，中英都有 |
| **图片路径合法** | 每个 `image` 指向 `Assets/Tutorial/images/` 下的文件；**缺图时测试失败**，这样图片漏了会被立刻发现 |
| 尺寸合理 | `imageWidth/imageHeight` 都 > 0，且宽高比与建议尺寸误差在容差内 |
| 章节不空 | 每章至少一步 |

**图片到位后**，把「文件必须存在」作为硬断言，就能保证教程不会出现空白图。

---

## 六、需要你决定的三件事

1. **入口位置**：状态栏按钮 + `F1`（我的建议）？还是工具栏？
2. **图片谁来出**：设计师出图，还是**我按你现在这个窗口布局自己截**？
   我这边**没法程序化截真机界面**（上一轮试过，`PrintWindow` 抓不到 WinUI 的弹出层），
   但**你手工截图给我**完全可以——16 张 800×600，或者你截完我裁。
   我也可以先用**代码生成的示意图**（画矩形+标注）占位，等真图再换。
3. **先做哪几章**：四章一起做，还是先做「打开 + 导出」两章验证效果？

---

## 七、我不建议现在做的

- **不要引 Markdown / WebView2**：都要新依赖，离线源装不了。
- **不要做长滚动帮助页**：新手真正需要的是「四步做完一件事」，不是手册。
- **不要在没有图片的情况下先做完整界面**：会做出一堆占位框，返工概率高。
  建议**先把 JSON + 键校验 + 测试做完**（这部分不依赖图片），图片到了再补界面。

---

## 八、实现记录

第一版已按上面的方案做完，**只涉及教程界面与结构，没有动模型层、撤销栈和画布**。

### 实际文件

| 文件 | 作用 |
| --- | --- |
| `src/NaraPainter.App/Assets/Tutorial/tutorial.json` | 四章 16 步的结构、文案键、图片名与尺寸 |
| `src/NaraPainter.App/Services/TutorialCatalog.cs` | 读取 JSON；文件缺失或格式错**返回 null，不抛异常** |
| `src/NaraPainter.App/Views/TutorialDialog.cs` | 弹窗：左侧章节、右侧文字 + 图片/占位框、底部翻页 |
| `tests/NaraPainter.Tests/TutorialTests.cs` | 9 条测试 |
| `Strings.resx` / `Strings.zh-CN.resx` | 29 条中英文案（4 个章名 + 16 条正文 + 9 条界面词） |

### 与原方案的三处差异

1. **入口位置**：按你的决定做成状态栏「教程」按钮 + `F1`。
2. **图片**：不生成 PNG 占位图，改成**在 XAML 里画占位框**——
   虚线框内显示「示意图占位 800 × 600」。这样设计师把真图放进
   `Assets/Tutorial/images/` 后**自动替换**，不需要删占位文件。
   `Assets/Tutorial/images/` 目录**现在还不存在**，这是预期的。
3. **翻页是跨章的**：读到一章末尾按「下一步」会进入下一章，
   不必回左侧重新点章节。

### 缺图时的行为

`ShowPicture` 先查文件是否存在：存在就加载 `BitmapImage`，不存在就画占位框。
**任何一步缺图都只影响那一步的显示**，程序不会因此报错或退出。

### 测试覆盖（9 条）

| 测试 | 断言 |
| --- | --- |
| `TheGuideParses` | JSON 可解析、`version=1`、每章至少一步 |
| `TheGuideCoversTheFourChapters` | 四章 id 依次为 open/layers/heal/export，共 16 步 |
| `EveryStepKeyReadsInBothLanguages` | 每个键在中英 resx 里都有，**失败时报出是哪个键缺哪边** |
| `TwoStepsDoNotShareOneKey` | 没有两步共用一条文案 |
| `EveryPictureIsNamedForTheGuideFolder` | 图片名是纯文件名、以 `.png` 结尾、不含 `..` |
| `EveryPictureSitsWhereTheGuideLooks` | 拼接后不会逃出 `Assets/Tutorial/images/` |
| `EveryStepAsksForTheSamePictureSize` | 每步都是 800 × 600 |
| `TheGuideThatShipsWithTheAppCanBeRead` | `TutorialCatalog.Load()` 能从程序目录读到（**防止 JSON 没被复制出去**） |
| `AMalformedGuideIsRefusedRatherThanThrown` | 坏 JSON 返回 null 而不是抛异常 |

### 后续接设计师的图

**待办**：16 张 800 × 600 的图片由设计师提供，本版先用界面里的占位框。

1. 建目录 `src/NaraPainter.App/Assets/Tutorial/images/`
2. 按 JSON 里的文件名放入 16 张 800 × 600 的 PNG
3. **不用改任何代码**，重新构建即可

如果图片尺寸想改，改 JSON 里的 `imageWidth` / `imageHeight` 和
`TutorialTests.ImageWidth` / `ImageHeight` 两个常量即可（测试会盯着所有步保持一致）。