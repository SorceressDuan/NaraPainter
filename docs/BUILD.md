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
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
$env:DOTNET_CLI_UI_LANGUAGE = "en"     # 否则中文输出，脚本里难解析
cd $root
dotnet build Compositor.sln -c Debug
```

`$env:APPDATA` 那行是必须的：NuGet 会去读 `%APPDATA%\NuGet\NuGet.Config`，
不重定向就报「未授权访问」。

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

**`dotnet test` 在这个沙箱里必然失败**，不是代码问题：VSTest 的 testhost 会对父进程调
`OpenProcess` 来监听退出，沙箱拒绝，报 `Win32Exception (5): 拒绝访问`，一个测试都跑不起来。
`dotnet vstest <dll>` 直接对编译产物跑也一样。

替代方案是仓库自带的反射 runner，它在本进程里加载测试程序集、逐个执行 `[Fact]`/`[Theory]`：

```powershell
powershell -ExecutionPolicy Bypass -File tools/verify/verify.ps1
```

这条命令会依次跑 restore → 全解决方案构建（要求 0 error 且 0 warning）→ 结构检查 → 全量测试，
最后打印 PASS/FAIL 汇总。脚本先试 `dotnet test`，检测到 testhost abort 就回退到 runner，
并在汇总里注明 `via fallback runner`，不会假装成 `dotnet test` 的结果。

`-ExecutionPolicy Bypass` 是必须的：这台机器上 `powershell -File` 对任何脚本都报
`AuthorizationManager check failed`。

## 运行

```powershell
& "src\Compositor.App\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\Compositor.exe"
```

调试构建是自包含的，进程会在后台起来，沙箱里看不到窗口截图，验证靠 `MainWindowTitle`。
跑之前先确认没有残留的 `Compositor` 进程。

### 改完 App 的源码后要 Rebuild

改了 `Compositor.App` 的 XAML 或代码后再做增量构建，可能出现启动即 `XamlParseException`
（退出码 `0xC000027B`），而同一份源码从零构建完全正常。遇到过两次，都在「改源码 + 增量构建」之后；
空跑一次增量构建不会触发。所以验收前一律：

```powershell
dotnet build src/Compositor.App/Compositor.App.csproj -c Debug -t:Rebuild
```

### 不要用 Start-Process 重定向输出

`Start-Process -RedirectStandardOutput/-RedirectStandardError` 在这个沙箱里会让**父进程挂住不返回**。
要观察程序的输出，让它自己写文件，然后读文件。

### 不要并行构建

多个 `dotnet build` 同时跑会互相锁住 `obj/`，报
`CS2012: ... being used by another process` 或 XAML 的 `WMC9999`。
这时的表现是「0 error / 0 warning 但 exit≠0」，和前面那条命名管道的静默失败不是一回事。
验收前确认没有别人在构建。
