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
cd $root
dotnet build Compositor.sln -c Debug
```

`$env:APPDATA` 那行是必须的：NuGet 会去读 `%APPDATA%\NuGet\NuGet.Config`，
不重定向就报「未授权访问」。

## 加依赖

`NuGet.config` 只挂了 `local-feed`，所以新包必须先下载：

1. 把包写进 `.tools/roots.json`，`{ "id": ..., "version": ... }` 的形式。
2. `node .tools/fetch-packages.js .tools/roots.json`
   脚本会递归解析依赖闭包并把 `.nupkg` 全部放进 `local-feed/`。已有的包会跳过。
3. 正常 `dotnet restore`。

版本别用浮动的，离线源里是什么版本就只能用什么版本。

## 已知的可选优化

SDK 自带的 `Microsoft.Windows.SDK.NET.Ref` 是 10.0.19041.53，
上项目引用 OpenCvSharp4 4.13 会报 `CS9057`：「分析器引用了 4.14.0.0 版编译器，
高于当前 4.11.0.0」。CI 开了 `TreatWarningsAsErrors` 就会挂。

两条路，二选一：

- 升级到 .NET 9 或 10 SDK（分析器版本要求随之满足）。需要重新下载 SDK 和对应运行时包，
  `fetch-packages.js` 那套流程照用，把 `BundledVersions.props` 里的版本号填进 `roots.json`。
- 保持 .NET 8，在 `Directory.Build.props` 里显式关掉这个分析器：
  `<PackageReference Include="OpenCvSharp4" ... ExcludeAssets="analyzers" />`。

还没定，谁先碰到谁定，定完更新 README 的构建章节。

## 运行

```powershell
& "src\Compositor.App\bin\Debug\net8.0-windows10.0.19041.0\win-x64\Compositor.exe"
```

调试构建是自包含的，进程会在后台起来，沙箱里看不到窗口截图，
验证靠 `MainWindowTitle` 和退出码。跑之前先确认没有残留的 `Compositor` 进程。
