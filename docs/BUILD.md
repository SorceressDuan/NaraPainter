# 构建环境说明

沙箱里有几个坑，这个文档记录踩过的和绕过的办法。所有命令都在仓库根目录执行。

## 为什么有 .tools/

这台机器上没装 .NET SDK，也没有 Visual Studio，而且拿不到管理员权限。
.NET 8 SDK 因此被直接解包到 `.tools/dotnet`，工作区里跑，不动系统目录。

`.tools/` 已在 `.gitignore` 里，不要提交。目录内容：

| 路径 | 用途 |
| --- | --- |
| `dotnet/` | 便携版 .NET 8.0.404 SDK |
| `local-feed/` | 离线 NuGet 源，所有 `.nupkg` |
| `nuget-packages/` | restore 后的全局包目录 |
| `cli-home/`, `appdata/`, `bundle-extract/` | dotnet 与 NuGet 的临时家目录 |
| `fetch-packages.js`, `roots.json` | 依赖闭包下载器 |
| `cache/upstream/` | 上游源码快照，`legacy/` 的来源 |

## 沙箱的两个硬限制

1. **schannel 拿不到凭据。** `.NET`、`git`、`curl` 的 TLS 全部报
   `SEC_E_NO_CREDENTIALS`。Node 和 Python 的 TLS 栈正常，所以下载一律用它们做。
   NuGet 因此也走不了网络，必须用离线源。
2. **工作区以外不可写。** `C:\Users\...\.dotnet`、`%APPDATA%\NuGet` 都写不进去，
   所以 `APPDATA`、`DOTNET_CLI_HOME`、`NUGET_PACKAGES` 全部重定向到 `.tools/` 下面。

## 构建

每条命令都要带这套环境变量，别指望 shell 里有：

```powershell
$root = "C:\Users\Sorce\Downloads\compositor"
$env:APPDATA = "$root\.tools\appdata"
$env:DOTNET_ROOT = "$root\.tools\dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_CLI_HOME = "$root\.tools\cli-home"
$env:NUGET_PACKAGES = "$root\.tools\nuget-packages"
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = "$root\.tools\bundle-extract"
$env:TEMP = "$root\.tools\temp"     # MSBuild 在 %TEMP%\MSBuildTemp 下建目录，真实临时目录不可写
$env:TMP = $env:TEMP
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
$env:DOTNET_CLI_UI_LANGUAGE = "en"     # 否则中文输出，脚本里难解析
cd $root
dotnet build Compositor.sln -c Debug
```

`$env:APPDATA` 与 `$env:TEMP` 两行是必须的：

- NuGet 会去读 `%APPDATA%\NuGet\NuGet.Config`，不重定向就报「未授权访问」
- MSBuild 会创建 `%TEMP%\MSBuildTemp` 放 XAML 与 PRI 的中间产物，不重定向会在做任何事之前
  就报 `MSBUILD : error MSB1025: An internal failure occurred while running MSBuild` +
  `UnauthorizedAccessException: ...\Temp\MSBuildTemp`

### restore 必须单节点

沙箱禁止命名管道，MSBuild 的 worker 节点连不回来。直接后果是
`dotnet restore` 在跨项目引用时静默失败（只打印「Determining projects to restore...」
然后 exit 1，**0 Errors 0 Warnings**，非常难查）。

```powershell
dotnet restore Compositor.sln -m:1 -nodeReuse:false
```

`dotnet build` 因为自己串起 restore 和编译，多数时候不受影响；一旦遇到
「Build FAILED, 0 Error(s)」这种没有任何错误信息的失败，先加上 `-m:1 -nodeReuse:false` 重试。

## 加依赖

`NuGet.config` 只挂了 `local-feed`，所以新包必须先下载：

1. 把包写进 `.tools/roots.json`，`{ "id": ..., "version": ... }` 的形式。
2. `node .tools/fetch-packages.js .tools/roots.json`
   脚本会递归解析依赖闭包并把 `.nupkg` 全部放进 `local-feed/`。已有的包会跳过。
3. 正常 `dotnet restore`。

版本别用浮动的，离线源里是什么版本就只能用什么版本。

## 已知的警告与处理

**CS9057（OpenCvSharp4 分析器）。** 它的 Roslyn 分析器是按 4.14 编译器构建的，
而 .NET 8 SDK 跑的是 4.11，所以每个引用它的项目都会报版本不匹配。
根 `Directory.Build.props` 里用 `<NoWarn>$(NoWarn);CS9057</NoWarn>` 压掉。

注意 `ExcludeAssets="analyzers"` **拦不住它**，写在 csproj、`Directory.Build.props`
还是 `Directory.Build.targets` 都一样：NuGet 的 analyzer 资产不经过 `project.assets.json`
（dump 出来 `files` 和 `targets` 段里一条 analyzer 记录都没有），编译器是直接从包目录探测
`.dll` 的。别在这条路上浪费时间，压警告码是唯一可行的做法。

**NU1603（Win2D 依赖版本）。** Win2D 1.4.0 声明依赖 `Microsoft.WindowsAppSDK.WinUI >= 1.8.260204000`，
这个版本从没发布过，1.8 线发的是 1.8.260803003。根 `Directory.Build.targets` 里对引用 Win2D 的项目
显式 pin 了后者——把图里本来就会落到的版本写出来，警告消失，不用压。

**WIN2D0001（AnyCPU）。** Win2D 只有原生二进制，AnyCPU 构建会报警并把 dll 引用留到运行时处理。
App 和 Compositing 两个 csproj 里把 `Platform` 默认成 `x64`（`Platforms` 仍然保留 x64 与 ARM64，
用 `-p:Platform=ARM64` 仍可覆盖）。

## 时序上的坑：道具文件里的条件看不到 TargetFramework

`Directory.Build.props` 在项目 body **之前**求值，那时 `TargetFramework` 还是空的，
所以任何 `Condition="$(TargetFramework.Contains('-windows'))"` 在那里**永远为假，而且不报错**。
需要按目标框架区分的设置一律放 `Directory.Build.targets`。

但反过来，`Directory.Build.targets` 在 NuGet 的 `buildTransitive/*.targets` **之前**导入，
所以想影响 Win2D 那种包里自带的 targets，`Update` 是无效的（它们已经算完了）。
这种情形只能在项目文件里设，或者用命令行 `-p:` 覆盖。

## 加依赖

`NuGet.config` 只挂了 `local-feed`，所以新包必须先下载：

1. 把包写进 `.tools/roots.json`，`{ "id": ..., "version": ... }` 的形式。
2. `node .tools/fetch-packages.js .tools/roots.json`
   脚本会递归解析依赖闭包并把 `.nupkg` 全部放进 `local-feed/`。已有的包会跳过。
3. 正常 `dotnet restore`。

版本别用浮动的，离线源里是什么版本就只能用什么版本。

## 测试

一条命令跑完全部验收：

```powershell
powershell -ExecutionPolicy Bypass -File tools/verify/verify.ps1
```

依次是 restore → 全解决方案构建（要求 0 error 且 0 warning）→ 结构检查 → 全量测试 →
发布包布局检查，最后打印 PASS/FAIL 汇总。

`-ExecutionPolicy Bypass` 是必须的：这台机器上 `powershell -File` 对任何脚本都报
`AuthorizationManager check failed`。

### dotnet test 与回退 runner

`dotnet test` 曾经在这台机器上完全跑不起来：VSTest 的 testhost 会对父进程调 `OpenProcess`
来监听退出，被沙箱拒绝，报 `Win32Exception (5): 拒绝访问`，一个测试都跑不到。

把 `TEMP`（见上文构建环境）重定向到工作区之后**它恢复了可用**，现在验收跑的是真正的
`dotnet test`。推测是 testhost 起不来与临时目录不可写叠加所致，但没往下追究——只要改了
`TEMP` 就能跑，这一点连续三次验收复现过。

仓库里仍保留反射 runner（`tools/verify/NaraDreamPainter.TestRunner`，在本进程里加载测试程序集，
逐个执行 `[Fact]`/`[Theory]`），它是 `dotnet test` 不可用时的备用证据链。verify.ps1 先试
`dotnet test`，一旦检测到 testhost abort 就回退过去，并在汇总里注明 `via fallback runner`，
不会把回退结果冒充成 `dotnet test` 的结果。

### 发布包布局检查

`PACKAGE` 一段断言 `dist\Compositor-*-win-x64.zip`：顶层有 `NaraDreamPainter.exe` 与它同级的依赖、
有 `LICENSE` 与 `README.md`、没有把所有内容包住的套层文件夹、README 含运行指引那句。
`dist` 里没有 zip 时记 `SKIP`（默认验收跑 Debug 构建，而 zip 来自 Release），不影响总结果。
重新生成发布包用 `packaging/pack.ps1`。

## 运行

```powershell
& "src\NaraDreamPainter.App\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\NaraDreamPainter.exe"
```

调试构建是自包含的，进程会在后台起来，沙箱里看不到窗口截图，验证靠 `MainWindowTitle`。
跑之前先确认没有残留的 `Compositor` 进程。

### 改了 App 的 XAML 之后要清掉 App 的 obj/bin

改了 `NaraDreamPainter.App` 的 XAML 或代码后再做增量构建，产物可能坏掉，表现是启动即
`XamlParseException`（退出码 `0xC000027B`）。这时 `startup.log` 里只有 `[unhandled]`
而没有 `[OnLaunched]`，因为应用死在 XAML 解析阶段，`App` 构造函数里的兜底都没跑到。

修复配方（连续 4 次验证有效）：

```powershell
dotnet build-server shutdown
Remove-Item -Recurse -Force src\NaraDreamPainter.App\obj, src\NaraDreamPainter.App\bin
dotnet build Compositor.sln -c Debug
```

**不要用 `dotnet build -t:Rebuild`。** 它会把依赖项目一起重建，而 App 的 PRI 合并正好撞上
`NaraDreamPainter.Compositing.pri` 被清掉的瞬间，报 `PRI252 ... not found`，或者留下比增量构建
更坏的中间产物。只删 App 自己的 `obj`/`bin` 再整体构建是唯一稳定的做法。

### 启动进程不要用 Start-Process

`Start-Process` 带 `-RedirectStandard*`、或者 `UseShellExecute = $true`，在这个沙箱里都会
把父进程挂住不返回。用 `ProcessStartInfo` 显式关闭 shell 执行：

```powershell
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.WorkingDirectory = (Get-Location).Path
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.Arguments = "--selftest=assets\testimages\photo.jpg --log=.tools\s.log"
$p = [System.Diagnostics.Process]::Start($psi)
$null = $p.WaitForExit(180000)
$p.ExitCode
```

### 不要并行构建

多个 `dotnet build` 同时跑会互相锁住 `obj/`，报
`CS2012: ... being used by another process` 或 XAML 的 `WMC9999`。
这时的表现是「0 error / 0 warning 但 exit≠0」，和前面那条命名管道的静默失败不是一回事。
验收前确认没有别人在构建。

## 发布包布局：应用放进 app\ 子目录（实测结论）

面向使用者的包里，程序本体在 `app\`，根目录只留启动方式与文档。这不是为了好看，是因为
`app\` 里有一百多个运行时文件，全部摊平会让第一次使用的人找不到主程序。

**实测：WinUI 3 自包含应用放进子目录后可以正常运行**，`--selftest` 输出 `RESULT PASS`
（含 4000×3000 打开 191 ms、语言双向切换）。原因是应用相对**自身可执行文件**解析运行时与
`.pri` 资源，不依赖工作目录或上层目录结构。启动器仍然显式设置工作目录，不去赌这一点。

### 启动器

根目录放一个 `launcher\NaraDreamPainter.exe`，它只做一件事：把 `..\app\NaraDreamPainter.exe`
拉起来，并把工作目录设为 `app\`。

- 启动器是**自包含**的，所以在一台从未装过 .NET 的机器上也能启动
- 换成框架依赖的启动器会更小（根目录只要 3 个文件），但它要求目标机器装有 .NET 8 桌面运行时，
  对一个主打免安装的便携包来说不划算
- 启动器按 `..\app\` 解析，而不是按自己的工作目录。第一版写成 `app\` 直接放在启动器旁，
  结果指向了 `launcher\app\`，只能弹错误框——这个相对路径不要随手改

### 快捷方式不能预先打包

`.lnk` 存的是**绝对路径**，在打包机上生成的快捷方式会指向构建目录。所以发布包里给的是一个
`.bat`，在用户机器上现场生成，那时路径才是最终的。试过把目标改写成相对路径，
Windows 会把它规范化成 `C:\launcher\...`，不可用。

### 布局断言

`pack.ps1` 在打包后检查：根目录只有 `Run.bat`、快捷方式生成脚本、`README.md`、`LICENSE`、
`RUNNING.txt`、`app\`、`launcher\` 这 7 项，多一个就报错。

用到的图标是 `packaging/NaraDreamPainter.ico`，由 `.tools/make-icon/` 里的生成器画出来（一次性
工具，不在解决方案里）。图标写的是标准 DIB 条目，`biHeight` 必须是高度的两倍，否则
`System.Drawing.Icon` 与资源管理器都读不出来。

