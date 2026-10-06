# 图像导出 — 格式矩阵与参数契约

导出路径从 `PixelBuffer` 到文件只有一条：`NaraPainter.Imaging.Services.ImageCodec` 走 `Cv2.ImWrite`，
格式由扩展名决定，参数由 `ImageSaveOptions` 决定。这份文档写清每个格式能做什么、TIFF 的压缩怎么选、
以及为什么没有换掉 OpenCV 的 TIFF 编码器。影像层的其它内容见 [IMAGING.md](IMAGING.md)。

## 格式矩阵

| 格式 | 扩展名 | 读 | 写 | alpha | 质量参数 | 压缩参数 |
| --- | --- | --- | --- | --- | --- | --- |
| PNG | `png` | ✓ | ✓ | 保留，四字节逐字节一致 | 无 | 无 |
| JPEG | `jpg` / `jpeg` | ✓ | ✓ | **丢弃**，写前压平成 3 通道 | 1–100，默认 90 | 无 |
| WebP | `webp` | ✓ | ✓ | 保留，alpha 通道逐字节一致、颜色有损 | 1–100，默认 90 | 无 |
| BMP | `bmp` | ✓ | ✓ | 保留，四字节逐字节一致 | 无 | 无 |
| TIFF | `tif` / `tiff` | ✓ | ✓ | 保留，四字节逐字节一致 | 无 | None / Lzw / Deflate / PackBits，默认 Lzw |

「保留」的含义是解码回来的 RGBA 与写出去之前完全相等，这一列由测试逐字节断言，不是推测。
BMP 这一行容易让人意外：OpenCV 写的是 32 位 BMP，alpha 真实落盘并且读得回来，测试里带 alpha 的图案往返
四字节一致。JPEG 那一行的 alpha=255 是解码端补的，源图的 alpha 在 `BGRA2BGR` 那一步就没了。

质量参数只对 JPEG / WebP 有意义：给 PNG / BMP / TIFF 传 `Quality` 不会改变一个字节，测试里用固定图案
在 `Quality: 20` 与 `Quality: 95` 两个值上比过 SHA-256。

## 契约

契约在 `src/NaraPainter.Models/Services/IImageCodec.cs`，V0.2 里加了两个东西：

```csharp
public enum TiffCompression { None, Lzw, Deflate, PackBits }

public sealed record ImageSaveOptions(
    ImageFileFormat Format,
    int Quality = 90,
    TiffCompression Compression = TiffCompression.Lzw);

public sealed record ImageFormatCapabilities(
    ImageFileFormat Format,
    string Extension,
    bool SupportsAlpha,
    bool SupportsQuality,
    int DefaultQuality,
    IReadOnlyList<TiffCompression> Compressions);
```

`IImageCodec` 上多了两个成员：`Capabilities(ImageFileFormat)` 查单个格式，`WriteFormats` 每个可写格式一条，
自带规范扩展名（`jpg` 而不是 `jpeg`，`tif` 而不是 `tiff`），界面直接拿它填格式下拉框，
不用再从 `SupportedWriteExtensions` 里猜哪个是主扩展名。

**默认值不改变既有行为**：`new ImageSaveOptions(format)` 与 `new ImageSaveOptions(format, Quality: 90)`
的写法一个字都不用改。TIFF 的新参数默认 `Lzw`，因为 libtiff 在没有 `IMWRITE_TIFF_COMPRESSION` 时本来就是
LZW——「不传 Compression」与「显式传 `Lzw`」写出的文件 SHA-256 相同，这条有测试钉住。
`Quality` 默认仍是 90。

`DefaultQuality` 在格式没有质量旋钮时为 0，界面据此禁用质量控件，`SupportsQuality` 与它同真同假。
`SupportsAlpha` 不只用来给界面看：`ImageCodec.Write` 就是读它决定要不要 `BGRA2BGR`，
所以「查询说的」和「写出来的」是同一处事实，不会漂移。

给导出对话框接线大致是这样：

```csharp
foreach (ImageFormatCapabilities format in codec.WriteFormats)
{
    // format.Extension -> 默认文件名后缀
    // format.SupportsAlpha -> 「保留透明」开关是否可用
    // format.SupportsQuality / format.DefaultQuality -> 质量滑条是否可用、初值
    // format.Compressions -> TIFF 压缩下拉框的选项；其它格式为空
}
```

写文件时扩展名必须与 `ImageSaveOptions.Format` 一致，不一致直接抛 `ArgumentException`（既有约束，
OpenCV 按扩展名挑编码器，不一致会把质量参数喂给别的编码器）。

## TIFF 的压缩

`ImwriteFlags.TiffCompression` 的值不是 OpenCV 自己的枚举，而是直接透传给 libtiff 的压缩方案号，
OpenCvSharp 没有对应枚举，所以映射写死在 `ImageCodec.TiffConstant` 里，并用测试钉住：

| `TiffCompression` | 透传值 | libtiff 常量 | 16×8 RGBA 图案实测字节 |
| --- | --- | --- | --- |
| 不传参数 / `Lzw` | 5 | `COMPRESSION_LZW` | 282 |
| `None` | 1 | `COMPRESSION_NONE` | 674 |
| `Deflate` | 8 | `COMPRESSION_ADOBE_DEFLATE` | 232 |
| `PackBits` | 32773 | `COMPRESSION_PACKBITS` | 680 |

四种方案在上面那组图案上都通过了逐字节往返断言，压缩比差异也符合预期（Deflate 最省，
PackBits 对这种高熵图案反而略微膨胀）。`COMPRESSION_DEFLATE`（32946）与 8 是同一个编解码器的两个标签，
实测都能正常往返，这里只暴露 8，避免下拉框出现两个功能相同的选项。

`TiffCompression` 是普通枚举，不携带 libtiff 的数值：`NaraPainter.Models` 不引用任何影像库，
把库常量写进 Models 等于让契约偷偷依赖 OpenCV 的版本。

## TIFF 的已知限制：没有 ExtraSamples 标签

OpenCV 的 TIFF 编码器按 4 通道写出图片，但不写 `ExtraSamples`（tag 338）。本轮直接解析导出文件的 IFD 复核过：

```
photometric = 2 (RGB)   samplesPerPixel = 4   extraSamples = 缺失
```

按 TIFF 规范，RGB 加第 4 个样本必须用 `ExtraSamples` 说明它是什么，缺失时解码会打一条警告，本轮在测试宿主里
逐条实测到（每读一个 4 通道 TIFF 一条）：

```
[ WARN:0@0.005] global grfmt_tiff.cpp:122 cv::TIFF_Warning TIFFReadDirectory: Sum of Photometric type-related color
channels and ExtraSamples doesn't match SamplesPerPixel. Defining non-color channels as ExtraSamples.
```

这条正是 [IMAGING.md](IMAGING.md) 限制一节里提到的那条。库的兜底行为就是「把多出来的通道当 ExtraSamples」，
所以 alpha 照样解得出来、像素不丢——警告归警告，不影响往返。

**像素不受影响**：四种压缩方案下往返都逐字节一致，警告只是元数据不合规。没有去手工补这个标签，
原因是补它要重写 IFD——插入一条 12 字节的表项、挪动其后所有条目、再把 `StripOffsets` 之类的偏移整体平移，
一次算错就是把用户导出的 TIFF 写坏。用「可能损坏导出文件」换「消掉一条警告」不划算。
真要修，正确做法是等 OpenCV 的编码器修，或者在 `Read` 侧正常工作、不再依赖 OpenCV 的 reader 之后自带编码器，
而不是对已写出的文件做二进制补丁。目标应用如果确实因为缺这个标签读不出 alpha，再按这个顺序处理。

## 为什么不提供 JPEG 压缩的 TIFF

`IMWRITE_TIFF_COMPRESSION = 7` 能写出来，但实测往返不是逐像素一致——它是有损的，而且 4 通道下
既丢像素也拿不到无损 alpha。TIFF 在这个产品里的定位是「要无损时选它」，再给一个会丢像素的压缩项
只会让人误以为 TIFF 一定无损。所以 `TiffCompression` 只有无损方案。

## 测试

`tests/NaraPainter.Tests/TiffExportTests.cs`：

- 四种压缩方案各自的 TIFF 往返逐像素一致（含 alpha）
- 四种方案的 tag 259 与上表一致；不传参数时是 5，且与显式 `Lzw` 的字节完全相同
- `None` 的文件确实比 `Lzw` 大，证明标签不只是写上去了
- `assets/testimages/shapes.png` 以四种方案导出 TIFF 再读回，与源逐字节一致
- PNG / BMP 带 alpha 往返逐字节一致；JPEG q90 与 WebP q90 的平均色差仍在原有容差内
- `WriteFormats` 每条声明的 `SupportsAlpha` / `SupportsQuality` / `SupportsCompression` 都与实际写出的
  文件表现一致（质量变不变字节、alpha 是否留下、压缩参数是否被忽略），扩展名能反查回格式
- 未知压缩值（`(TiffCompression)7`）与未知格式值抛 `NotSupportedException`

`tests/NaraPainter.Tests/ImageCodecTests.cs` 里的七种扩展名映射、路径与格式不一致的拒绝路径、
4000×3000 解码计时都不受影响，V0.2 之前的行为没有回归。
