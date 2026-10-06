# NaraDreamPainter.Imaging — OpenCvSharp 影像层

`src/NaraDreamPainter.Imaging/`。这一层只碰像素：解码、编码、调整、选区覆盖度。文档模型、图层顺序、
混合公式都在 `NaraDreamPainter.Models`，这里不重复实现。

接口定义在 `src/NaraDreamPainter.Models/Services/`（`NaraDreamPainter.Models.Services`），实现类都在
`NaraDreamPainter.Imaging.Services`，都是无参构造，界面直接 `new`。

| 文件 | 职责 |
| --- | --- |
| `Services/OpenCvInterop.cs` | `PixelBuffer` ↔ `Mat`，通道顺序与连续性 |
| `Services/ImageCodec.cs` | `IImageCodec`：读、写、扩展名与格式 |
| `Services/AdjustmentFilter.cs` | `IAdjustmentFilter`：亮度/对比度、色相/饱和度、色阶、曲线 |
| `Services/SelectionMaskBuilder.cs` | `ISelectionMaskBuilder`：选区覆盖度与按选区混合 |

## API 替换

| 原 API | 替换 | 备注 |
| --- | --- | --- |
| `CGImageSourceCreateImageAtIndex`、`CIImage.oriented(forExifOrientation:)` | `Cv2.ImRead(path, ImreadModes.Unchanged)` | `Unchanged` 保留文件原有的通道数，PNG/WebP 的 alpha 不会被压掉；EXIF 方向 OpenCV 自己处理，原项目是手动转的 |
| `CGImageDestination`、`NSBitmapImageRep` | `Cv2.ImWrite` | 编码器按扩展名选；JPEG 用 `ImwriteFlags.JpegQuality`，WebP 用 `WebPQuality` |
| `CIColorControls`、`CILevels`、`CIColorCurves` | `Cv2.LUT` | 每通道一张 256 项表，表来自设置对象自己的 `Table()`/`Tables()`；alpha 不参与查表 |
| `CIHueAdjust` | `HueSaturationSettings.Adjust` 逐像素 | Photoshop 的分区间衰减没有对应的 OpenCV 调用，数学留在 Models，别在这里重写一份 |
| `NSBezierPath` / `CGPath` 填充 | `Cv2.Rectangle` / `Cv2.Ellipse` | 覆盖度画在 `CV_8UC1` 上，越界部分 OpenCV 自己裁 |
| `CIGaussianBlur` | `Cv2.GaussianBlur` | 羽化，sigma = feather / 2，与 macOS 选择代码同参数 |
| `CIBlendWithMask` | 覆盖度插值 | `original + (adjusted - original) * coverage / 255` |

`// MIGRATION:` 注释就打在每条管线入口上（`ImageCodec.Read/Write`、`AdjustmentFilter`、
`SelectionMaskBuilder`），改这些地方时顺手把对照表也更新。

## 通道顺序

OpenCV 是 BGR/BGRA，`PixelBuffer` 是直通（非预乘）alpha 的 RGBA。换位集中在 `OpenCvInterop`，
逐字节显式做，没有走 `Cv2.CvtColor`：文件里有几个通道是运行时才知道的，让通用转换去猜顺序，
等于把红蓝互换的风险藏进库里。灰度按 R=G=B 展开并补 alpha=255，三通道补 alpha=255，四通道原样换位。

查表路径同理，`Cv2.Split` 拆成 B/G/R/A，蓝吃 `tables[2]`、绿吃 `tables[1]`、红吃 `tables[0]`，
alpha 不进表，最后 `Cv2.Merge` 合回去。设置对象给的表是 RGB 序，不是 BGR。

## 限制与取舍

- **位深**：读入时统一降到 8 位。16 位按 `1/257` 缩放；32F/64F 当成已经归一化到 0-1（乘 255）。
  存别的量程的浮点 TIFF 会顶到白，属于已知取舍，不是 bug。
- **JPEG 没有 alpha**：`Write` 遇到 JPEG 先 `BGRA2BGR` 丢掉 alpha。`Png`/`WebP`/`Bmp`/`Tiff`
  都写 4 通道，实测往返逐字节一致（含 alpha）。
- **写文件时扩展名必须和 `ImageSaveOptions.Format` 一致**：OpenCV 按扩展名挑编码器，不一致会写出
  调用方没要的格式，质量参数也会喂给错误的编码器，所以直接抛 `ArgumentException`。
- **TIFF 的 alpha 标签**：OpenCV 的 TIFF 编码器不写 `ExtraSamples`，libtiff 解码时打一条
  `TIFFReadDirectory: Sum of Photometric type-related color channels and ExtraSamples...` warning。
  像素往返完全一致，warning 是噪音；要消掉只能换编码路径。
- **羽化是跨边界对称的**：接口注释写的是 “inwards from the boundary”，实现跟着 macOS 那版走
  `applyingGaussianBlur(sigma: feather / 2)`，衰减跨边界两侧——Photoshop 的常规语义，边界处约 50%。
  `feather = 8` 实测：边界 140、向外 2px 90、向外 6px 21、向内 10px 254。要严格只向内，得在模糊后
  乘回硬 mask，代价是边界出现 0→128 的跳变，反而不像 Photoshop，所以没这么做。
- **色相/饱和度是逐像素的**：12M 像素要跑一遍 HSL 加 7 个区间权重求和，比查表路径慢一个量级。
  预览如果卡，正确做法是按 32³ 色彩立方做三维查表（原项目 `Rendering/LevelsPixels.c` 里的
  `cube_apply` 就是这个思路），而不是把每像素数学换成近似。
- **空选区**（`SelectionRegion.IsEmpty`）返回全 0 覆盖度；「没有选区」用 `Full()`。
- **未知调整类型**抛 `NotSupportedException`：以后在 Models 里加了新的 `AdjustmentSettings` 而忘了
  在这里接线，会立刻炸在调用处，不会静默跳过。

## 性能

4000×3000 PNG `Read`（Debug 构建，连续读多次，文件缓存已热）：多次测得 183.8~203.4 ms，最好 183.8 ms、
中位约 195 ms，验收线 800 ms。瓶颈在 libpng 解码，通道换位只是 48 MB 的单遍扫描。

`RuntimeIdentifiers` 只留 `win-x64`：App 自己就是单 RID，MVP 没必要为 arm64 的 runtime pack 和构建时间
买单。要出 arm64 时和 App 一起放宽，别只改这里。RID 对不上时 restore 会 `NU1101`，整条 `dotnet build`
退化成只打印「生成失败 / 0 个错误」，看不出原因。

## 验证方式

一次性 harness（不提交进仓库）在临时目录里引用 `NaraDreamPainter.Imaging`，覆盖：

- 七种扩展名 → `ImageFileFormat` 的映射，大小写与未知扩展名
- 五种格式往返：PNG/BMP/TIFF 逐字节一致，JPEG q90 与 WebP q90 在噪点+硬边图案上 mean 差 12.4 / 11.9
  （lossy 的正常量级），WebP 的 alpha 无损
- 直通 alpha：`(255,0,0,0)`、`(0,255,0,128)` 这类像素过 PNG 往返后 RGBA 四字节完全不变，没有被预乘
- `assets/testimages/` 的 `gradient.png`、`shapes.png` PNG 往返逐字节一致，`transparent.webp` → PNG
  保留 93600 个半透明像素
- 四种调整逐通道对齐设置对象的 `Table()`/`Tables()` 输出，alpha 原样；`red +120°` 落到绿、
  `saturation -100` 落回灰；`ApplyAll` 等于逐个 `Apply`；源 buffer 不被改写
- 选区：矩形/椭圆内部 255、外部 0、边界排他、越界裁剪、羽化剖面、`BlendThrough` 在覆盖度 0/128/255
  三个点的插值
- 4000×3000 读 6 次计时

跑仓库测试要绕开一个沙箱限制：`dotnet test` / `dotnet vstest` 都起不来 testhost
（`Win32Exception (5) 拒绝访问`，testhost 拿不到父进程句柄做退出回调），仓库里 `tools/verify/NaraDreamPainter.TestRunner`
就是为此写的反射 runner，直接吃测试 dll。`ImageCodecTests` 覆盖了上面大部分编码路径，
`SelectionMaskBuilder` 目前只有这个一次性 harness 覆盖。
