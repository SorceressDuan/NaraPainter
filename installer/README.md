# 安装包说明

`build-msix.ps1` 生成一个 MSIX 安装包，这是给普通用户的形态：装完从开始菜单启动，卸载干净，
看不到那几百个运行时文件。便携版 zip 仍然保留，见 [../packaging/pack.ps1](../packaging/pack.ps1)。

## 生成

```powershell
powershell -ExecutionPolicy Bypass -File installer/build-msix.ps1
```

产物：

| 文件 | 说明 |
| --- | --- |
| `dist/NaraDreamPainter-Setup.msix` | 安装包，约 122 MB |
| `dist/NaraDreamPainter.cer` | 自签名证书，安装前要先让本机信任它 |

脚本会顺手生成签名用的自签名证书（不需要管理员权限），存在
`Cert:\CurrentUser\My`，主题固定为 `CN=Nara Dream Painter`——这个字符串必须与
`Package.appxmanifest` 里的 `Publisher` 完全一致，否则安装会以签名错误告终。

## 安装

**这台开发机上到此为止跑不通，原因见下一节。** 在一台普通 Windows 上按下面三步走：

```powershell
# 1. 信任证书（需要管理员）
Import-Certificate -FilePath dist\NaraDreamPainter.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople

# 2. 允许旁加载（需要管理员，只需一次）
#    设置 → 系统 → 开发者选项 → 允许"旁加载应用"，或：
New-Item -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" -Force | Out-Null
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" `
    -Name AllowAllTrustedApps -Value 1 -Type DWord

# 3. 安装
Add-AppxPackage -Path dist\NaraDreamPainter-Setup.msix
```

也可以直接双击 `.msix`，系统会提示缺少受信任的证书，按提示先装 `.cer` 再装包。

## 当前开发机上为什么装不了

两条都是环境限制，不是包的问题：

1. **`signtool` 不支持 AppX 签名。** 仓库离线源里的 `Microsoft.Windows.SDK.BuildTools`
   带的是 `signtool.exe`，它对 `.msix` 报
   `This file format cannot be signed because it is not recognized`；
   给 `.pfx` 直接签名则报 `Store::ImportCertObject() failed (0x80090010)`。
   用完整 Windows SDK 里的 `signtool` 可以签，本机没装。
   `makeappx` 打包本身是成功的。
2. **系统未启用旁加载。** `HKLM\...\AppModelUnlock\AllowDevelopmentWithoutDevLicense` 为 `0`，
   所以 `Add-AppxPackage -Register <staged>\AppxManifest.xml` 报
   `0x80073CFF ... 需要有 Windows 开发者许可证或启用旁加载的系统`。
   改这个键需要管理员。

所以在有开发许可或已启用旁加载、且能拿到完整 SDK `signtool` 的机器上补上签名即可：
`makeappx pack /d dist\msix /p dist\NaraDreamPainter-Setup.msix` 之后
`signtool sign /fd SHA256 /sha1 <证书指纹> dist\NaraDreamPainter-Setup.msix`。

## 便携版

不想碰安装包就用 zip：解压后双击根目录的 `Run.bat`，或直接双击 `NaraDreamPainter.exe`。
两者都不需要安装 .NET 或 Windows App SDK。
