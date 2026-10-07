# GitHub 发布步骤

本文只讲**怎么把 v0.2.0 发出去**。发布前的状态已经核对过，结论在最后一节。

发布物：

| 项 | 值 |
| --- | --- |
| 版本 | `v0.2.0` |
| 发布资产 | `dist/NaraPainter-0.2.0-win-x64.zip` |
| 大小 | 156,965,792 字节（149.69 MB） |
| SHA-256 | `7A00DDAB4A360D8FBDD648BD4DEF91136333306F92A96E8F6DDEE88B0A7D5BC5` |
| 发布说明 | [docs/RELEASE_NOTES_v0.2.0.md](RELEASE_NOTES_v0.2.0.md) |

## 为什么 zip 不能 git push

GitHub 的两条限制不一样，别弄混：

| 限制 | 数值 | 影响 |
| --- | --- | --- |
| git 仓库里的单个文件 | **100 MB 硬上限**（50 MB 起警告） | 149.69 MB 的 zip 直接 push 会被拒 |
| Release 附件 | **每个 2 GB** | 走 Release 就没问题 |

所以流程是：**代码 push 到仓库，zip 作为 Release 附件上传**，zip 永远不进 git。
`.gitignore` 已经排除 `dist/`、`*.zip`、`*.msix`，从源头避免误提交。

参考：[GitHub 大文件说明](https://docs.github.com/en/repositories/working-with-files/managing-large-files/about-large-files-on-github)、[Release 附件容量讨论](https://github.com/orgs/community/discussions/196657)。

## 0. 前置：确认干净

```powershell
cd C:\Users\Sorce\Downloads\NaraPainter

# 工作区必须干净，否则先提交
git status --short

# 最后一次全量验收
powershell -ExecutionPolicy Bypass -File tools/verify/verify.ps1
```

期望 `RESULT: PASS`。

## 1. 在 GitHub 上建空仓库

网页操作，**不要**勾选 "Add a README"、"Add .gitignore"、选 License —— 本地已经有了，
勾了会产生一次无关的初始提交，push 时要先合并。

- 仓库名建议 `NaraPainter`
- 可见性按你的意愿（公开则需确认合规声明已在 README 顶部）
- 建完拿到地址，形如 `https://github.com/SorceressDuan/NaraPainter.git`

## 2. 关联远端并推送代码

```powershell
cd C:\Users\Sorce\Downloads\NaraPainter

git remote add origin https://github.com/SorceressDuan/NaraPainter.git
git branch -M main
git push -u origin main
```

首次 push 要认证。**推荐用 Personal Access Token**（GitHub 已不接受账号密码）：

1. GitHub → Settings → Developer settings → Personal access tokens → Fine-grained tokens
2. 仓库访问权限选刚建的仓库，权限给 `Contents: Read and write`
3. push 时用户名填 GitHub 用户名，密码粘贴 token

也可以用 GitHub CLI 一次认证：

```powershell
gh auth login
git push -u origin main
```

推送后确认仓库里**没有** zip：

```powershell
git ls-files | Select-String -Pattern '\.(zip|msix)$'
# 应当没有任何输出
```

## 3. 打 tag

```powershell
cd C:\Users\Sorce\Downloads\NaraPainter

git tag -a v0.2.0 -m "v0.2.0 - first public release"
git push origin v0.2.0
```

## 4. 创建 Release 并上传 zip

### 方式 A：网页（最省事，推荐第一次用）

1. 仓库 → **Releases** → **Draft a new release**
2. **Choose a tag** 选 `v0.2.0`
3. Release title 填 `v0.2.0 — 那菈绘梦 / Nara Painter`
4. 描述框粘贴 `docs/RELEASE_NOTES_v0.2.0.md` 的内容
5. **Attach binaries** 拖入或选择 `dist\NaraPainter-0.2.0-win-x64.zip`
   （149.69 MB，页面会显示上传进度，需要等一会儿）
6. 勾选 **Set as the latest release**，点 **Publish release**

### 方式 B：GitHub CLI

```powershell
cd C:\Users\Sorce\Downloads\NaraPainter

gh release create v0.2.0 `
  "dist\NaraPainter-0.2.0-win-x64.zip" `
  --title "v0.2.0 - 那菈绘梦 / Nara Painter" `
  --notes-file "docs\RELEASE_NOTES_v0.2.0.md" `
  --latest
```

`gh` 会自动处理大文件上传，无需额外参数。

### 方式 C：curl 调 API（没装 gh 时）

需要 token，且 `curl.exe` 在 Windows 10 1803+ 自带。

**注意 `-F` 上传时文件名前面必须带 `@`**，形如 `"file=@C:\path\to.zip"`。

```powershell
$token = "<你的 PAT>"
$repo  = "SorceressDuan/NaraPainter"
$tag   = "v0.2.0"
$zip   = "C:\Users\Sorce\Desktop\NaraPainter-0.2.0-win-x64.zip"

# 1) 建 release。若网页已建好 release，跳过这步，直接从网页地址里抄 release id 填到下面。
$release = curl.exe -s -X POST `
  -H "Authorization: Bearer $token" `
  -H "Accept: application/vnd.github+json" `
  "https://api.github.com/repos/$repo/releases" `
  -d "{`"tag_name`":`"$tag`",`"name`":`"v0.2.0 Nara Painter`",`"draft`":false}" | ConvertFrom-Json

# 2) 上传附件。upload_url 形如 https://uploads.github.com/...{?name,label}，要把末尾的花括号部分去掉。
$upload = $release.upload_url -replace '\{\?name,label\}', ''
$name   = Split-Path $zip -Leaf

curl.exe -X POST `
  -H "Authorization: Bearer $token" `
  -H "Content-Type: application/zip" `
  --data-binary "@$zip" `
  "$upload`?name=$name"
```

上面最后一行里 `` `? `` 是 PowerShell 的转义写法（反引号加问号），**那一对反引号和问号都要原样输入**，
不能写成单独的 `?`——PowerShell 会把 `$upload?name` 当成一个变量名而报错。

**这个方式对 149.69 MB 的断点续传支持不好**，网络不稳定时容易传一半失败。
如果你网络一般，**用方式 A（网页）或方式 B（gh CLI）更稳**。

上传 149.69 MB 可能要几分钟，不要中途打断。

## 5. 发布后核对

```powershell
# tag 已在远端
git ls-remote --tags origin v0.2.0

# 附件存在、大小对得上
gh release view v0.2.0 --json assets --jq '.assets[] | "\(.name)  \(.size) bytes"'
```

期望输出 `NaraPainter-0.2.0-win-x64.zip  156965792 bytes`。

**下载自己的发布包再验一次**（这一步别省，它是唯一能证明用户拿到的东西是好的）：

```powershell
$tmp = "$env:TEMP\nrp-check"
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
gh release download v0.2.0 --pattern "*.zip" --dir $tmp

# 校验和必须与上表一致
(Get-FileHash "$tmp\NaraPainter-0.2.0-win-x64.zip" -Algorithm SHA256).Hash

# 解压后跑自检
Expand-Archive "$tmp\NaraPainter-0.2.0-win-x64.zip" -DestinationPath "$tmp\app" -Force
& "$tmp\app\launcher\NaraPainter.exe" --selftest=(Resolve-Path "assets\testimages\photo.jpg") --log "$tmp\selftest.log"
Get-Content "$tmp\selftest.log" | Select-String "RESULT"
```

## 6. 下一次发版

```powershell
# 1. 改版本号
#    Directory.Build.props 里的 <Version>
# 2. 重新打包
powershell -ExecutionPolicy Bypass -File packaging/pack.ps1
# 3. 提交、打 tag、推送、建 Release
git commit -am "Bump the version to 0.3.0"
git tag -a v0.3.0 -m "v0.3.0"
git push origin main --tags
gh release create v0.3.0 "dist\NaraPainter-0.3.0-win-x64.zip" --title "v0.3.0" --notes-file docs\RELEASE_NOTES_v0.3.0.md --latest
```

## 发布前状态核对（已执行）

| 检查项 | 结果 |
| --- | --- |
| 工作区 | 干净 |
| 受版本控制文件 | 411 个 |
| 超过 5 MB 的文件 | **无** |
| 最大的受控文件 | 0.59 MB（`legacy/` 里的上游图标） |
| `dist/`、`.tools/`、`publish/` 是否混入 | 未混入 |
| 全量验收 | `RESULT: PASS`（272 测试，0 警告） |
| 自检 | `RESULT PASS`，23 个快捷键无重复，4000×3000 打开 193 ms |

### 有一件事需要你决定

`assets/testimages/large-4000x3000.png`（7.30 MB）**仍在 git 历史里**（`ad6a57f` 引入）。
我已经把它从当前跟踪中移除并写进 `.gitignore`，但**历史里的那份不会因此消失**。

- 实际影响很小：`.git` 目录只有 **4.55 MB**（1131 个松散对象，PNG 被压缩存储），克隆不会明显变慢
- 若你希望历史里也彻底没有它，必须在**首次 push 之前**重写历史：

```powershell
# 需要先装 git-filter-repo：pip install git-filter-repo
git filter-repo --path assets/testimages/large-4000x3000.png --invert-paths
# 重写后所有 commit SHA 都会变，远端有协作者时不要这么做
```

我没有替你执行这条：它会改写全部提交哈希，属于破坏性操作，而且当前影响（4.55 MB）不值得。
