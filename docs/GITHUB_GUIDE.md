# 从零把 v0.2.0 发到 GitHub（保姆级）

写给不常用 Git 的人。每一步都写清：**在哪操作、输入什么、会看到什么**。

你的信息（已确认）：

| 项 | 值 |
| --- | --- |
| GitHub 用户名 | `SorceressDuan` |
| 仓库名 | `NaraPainter` |
| 仓库地址 | `https://github.com/SorceressDuan/NaraPainter` |
| 本地项目目录 | `C:\Users\Sorce\Downloads\NaraPainter` |
| 待上传的 zip | `C:\Users\Sorce\Desktop\NaraPainter-0.2.0-win-x64.zip` |
| zip 大小 | 149.69 MB（156,965,792 字节） |
| zip SHA-256 | `7A00DDAB4A360D8FBDD648BD4DEF91136333306F92A96E8F6DDEE88B0A7D5BC5` |

**全程大约 20–30 分钟**，其中大半是等 zip 上传。

---

# 第一步：在网页上建仓库

## 1.1 打开新建页面

浏览器登录 GitHub，右上角 **`+`** → **New repository**。
直接打开这个地址也行：<https://github.com/new>

## 1.2 逐项怎么填

| 字段 | 填什么 | 说明 |
| --- | --- | --- |
| **Owner** | `SorceressDuan` | 已默认选中 |
| **Repository name** | `NaraPainter` | 注意大小写：`N` 和 `P` 大写，中间没有空格 |
| **Description** | `轻量级 Windows 图像编辑器，Compositor 的非官方移植版` | 可留空，不影响 |
| **Public / Private** | 选 **Public** | 开源发布必须公开。想先私有试，之后再改也行 |
| **Add a README file** | **不要勾** | 你本地已经有 README.md |
| **Add .gitignore** | **不要勾**（选 `None`） | 你本地已经有 .gitignore |
| **Choose a license** | **不要选**（选 `None`） | 你本地已经有 LICENSE |

> **为什么这三个都不勾？** 勾任何一个，GitHub 都会替你做一次提交，于是远端会有一个你本地没有的提交。之后 push 会被拒（提示 `rejected` / `fetch first`），还要多走一步合并。**全不勾，仓库就是完全空的，push 最顺。**

## 1.3 创建

点绿色 **Create repository**。

**会看到什么**：一个标题是 "Quick setup — if you've done this kind of thing before" 的页面，中间有个空仓库的说明。
**这是正常的**，这个页面就是给你复制命令用的，不用管它。

---

# 第二步：本地推送代码

## 2.1 打开 PowerShell

按 **Win 键**，输入 `powershell`，回车。
会弹出一个蓝色/黑色的窗口。**下面所有命令都在这个窗口里粘贴执行。**

## 2.2 进到项目目录

粘贴这行，回车：

```powershell
cd C:\Users\Sorce\Downloads\NaraPainter
```

**会看到什么**：命令行提示符前面变成了 `C:\Users\Sorce\Downloads\NaraPainter>`。

## 2.3 确认当前状态是干净的

```powershell
git status
```

**期望看到**：`nothing to commit, working tree clean`，以及 `On branch main`。

- 如果看到一堆红色/绿色的文件名 → 有改动没提交，先告诉我，别继续
- 如果看到 `On branch master` → 分支名是 `master`，**记住这一点**，2.5 步要用

## 2.4 关联远端

```powershell
git remote add origin https://github.com/SorceressDuan/NaraPainter.git
```

**会看到什么**：没有任何输出。**没输出就是成功**（Git 的习惯）。

验证一下：

```powershell
git remote -v
```

**期望看到**两行：

```
origin  https://github.com/SorceressDuan/NaraPainter.git (fetch)
origin  https://github.com/SorceressDuan/NaraPainter.git (push)
```

## 2.5 确认分支名是 main

```powershell
git branch -M main
```

这行做两件事：如果当前分支叫 `master`，改名为 `main`；如果已经叫 `main`，什么都不做。
**没有输出就是成功。**

> GitHub 现在默认分支名是 `main`。你的本地仓库本来就是 `main`（我在准备时确认过），所以这行大概率是无操作。

## 2.6 推送

```powershell
git push -u origin main
```

**接下来会发生什么**——这一步最关键：

### 情况 A：弹出登录窗口

会弹出一个 **"Connect to GitHub"** 或 **"Sign in to GitHub.com"** 的对话框。
选 **Sign in with your browser**（用浏览器登录）最省事，按提示授权即可。

### 情况 B：命令行里要你输入用户名和密码

你会看到：

```
Username for 'https://github.com':
```

输入 `SorceressDuan`，回车。然后：

```
Password for 'https://SorceressDuan@github.com':
```

**这里不能填你的 GitHub 登录密码。** GitHub 从 2021 年起就不接受密码了。你需要一个 **Personal Access Token**。

### 怎么弄 Token（情况 B 才需要）

1. 浏览器打开：<https://github.com/settings/tokens?type=beta>（这就是 Fine-grained tokens 页面）
2. 点 **Generate new token**
3. **Token name**：随便填，比如 `narapainter-push`
4. **Expiration**：选 `30 days`（够用了；选 No expiration 也可以，但风险更大）
5. **Repository access**：选 **Only select repositories**，然后在下拉里选 `NaraPainter`
6. **Permissions** → 展开 **Repository permissions** → 找到 **Contents** → 把权限改成 **Read and write**
7. 拉到底点 **Generate token**
8. 页面顶部会出现一串 `github_pat_` 开头的字符串，**只显示这一次**
9. 点旁边的复制按钮

回到 PowerShell 的 `Password for ...` 提示处：

- **粘贴那串 token**，然后回车
- **注意**：粘贴时屏幕上**不会显示任何字符**（连 `*` 都没有），这是正常的，不是没粘上
- PowerShell 里粘贴用**鼠标右键**，或者 `Ctrl+Shift+V`（直接 `Ctrl+V` 在旧版 PowerShell 里可能无效）

### 推送成功的标志

```
Enumerating objects: 500, done.
Counting objects: 100% (500/500), done.
...
Writing objects: 100% (500/500), 3.2 MiB | 1.5 MiB/s, done.
To https://github.com/SorceressDuan/NaraPainter.git
 * [new branch]      main -> main
branch 'main' set up to track 'origin/main'.
```

看到 `* [new branch] main -> main` 就成了。

**大小提示**：这次推送大约 4.5 MB（代码 + `legacy/` 里的上游素材，已压缩），**不含那个 149.69 MB 的 zip** —— zip 被 `.gitignore` 排除了，它走第四步的 Release 附件。

---

# 第三步：打 tag 并创建 Release

## 3.1 打 tag（本地）

tag 就是给某个提交起个版本号，GitHub 用它来认 Release。

```powershell
cd C:\Users\Sorce\Downloads\NaraPainter
git tag -a v0.2.0 -m "v0.2.0 - first public release"
```

**没有输出就是成功。**

确认一下：

```powershell
git tag
```

**期望看到**：`v0.2.0`

## 3.2 把 tag 推到 GitHub

```powershell
git push origin v0.2.0
```

**期望看到**：

```
To https://github.com/SorceressDuan/NaraPainter.git
 * [new tag]         v0.2.0 -> v0.2.0
```

## 3.3 在网页上创建 Release

1. 浏览器打开：<https://github.com/SorceressDuan/NaraPainter/releases/new>
   （或者：仓库页面 → 右侧 **Releases** → **Draft a new release**）
2. **Choose a tag**：点下拉，能看到 `v0.2.0`，选中它
   （如果没看到，说明 3.2 的推送没成功，回去重做）
3. **Release title** 填：

   ```
   v0.2.0 — 那菈绘梦 / Nara Painter
   ```

4. **Describe this release** 大文本框：把项目根目录 `Release_v0.2.0.md` 的**全部内容**粘进去

   > 怎么复制：用记事本打开 `C:\Users\Sorce\Downloads\NaraPainter\Release_v0.2.0.md`，
   > `Ctrl+A` 全选 → `Ctrl+C` 复制 → 回到网页文本框 `Ctrl+V` 粘贴。
   >
   > GitHub 的文本框支持 Markdown，表格、标题、链接都会正常渲染。

5. 勾选 **Set as the latest release**（让这个版本成为「最新」）

## 3.4 上传 zip 附件

在同一页面往下找 **Attach binaries by dropping them here or selecting them**。

**两种做法，任选**：

**做法 1（拖拽）**：
打开文件资源管理器到桌面，把 `NaraPainter-0.2.0-win-x64.zip` 直接**拖到那个虚线框里**。

**做法 2（选择）**：
点那个区域，在文件对话框里定位到 `C:\Users\Sorce\Desktop\`，选中 `NaraPainter-0.2.0-win-x64.zip`，打开。

**接下来会发生什么**：

- 虚线框里会出现一行文件名，右侧显示上传进度条
- **149.69 MB 按普通家庭宽带大约要 1–5 分钟**，期间不要关页面、不要刷新
- 上传完成后文件名前会出现一个绿色对勾图标

## 3.5 发布

点页面底部的绿色 **Publish release**。

**会看到什么**：跳转到这个 Release 的页面，标题下方有 `v0.2.0` 标签，正文是你粘的内容，
最底下 **Assets** 区域有 `NaraPainter-0.2.0-win-x64.zip  149.69 MB`。

### 为什么 zip 不能直接用 git push 传？

因为 GitHub 有两条不同的限制：

| 限制 | 数值 | 管什么 |
| --- | --- | --- |
| git 仓库里的单个文件 | **100 MB 硬上限**（50 MB 起就警告） | 用 `git push` 传的东西 |
| Release 附件 | **每个 2 GB** | 在 Release 页面上传的东西 |

你的 zip 是 **149.69 MB，超过 100 MB**，所以 `git push` 会被 GitHub 直接拒绝，报类似
`remote: error: File ... is 149.69 MB; this exceeds GitHub's file size limit of 100.00 MB`。
而 Release 附件上限是 2 GB，走这条路完全没问题。

**结论**：代码用 `git push`，zip 用 Release 附件。两条路分开走，这是标准做法。

---

# 第四步：验证

## 4.1 验证代码推上去了

浏览器打开：<https://github.com/SorceressDuan/NaraPainter>

**应该看到**：

- 页面顶部显示 `SorceressDuan / NaraPainter`
- 文件列表里有 `src`、`tests`、`docs`、`legacy`、`LICENSE`、`README.md` 等
- README 内容正常渲染（有中文标题和「本工具定位为轻量级 Windows 图像编辑器…」那段声明）

命令行也能验：

```powershell
cd C:\Users\Sorce\Downloads\NaraPainter
git status
```

**期望**：`Your branch is up to date with 'origin/main'.` 和 `nothing to commit, working tree clean`

再确认远端确实有 tag：

```powershell
git ls-remote --tags origin
```

**期望看到**包含 `refs/tags/v0.2.0` 的一行。

## 4.2 验证 zip 没有混进代码仓库

这一步很重要，确认 149.69 MB 的 zip 没被误提交：

```powershell
git ls-files | Select-String -Pattern "\.(zip|msix)$"
```

**期望：完全没有输出。** 有输出就说明 zip 进了仓库，需要处理，告诉我。

## 4.3 下载附件，核对 SHA-256

**这是唯一能证明用户拿到的东西是好的方法**，别省。

### 在网页上下载

1. 打开 <https://github.com/SorceressDuan/NaraPainter/releases>
2. 点进 `v0.2.0`
3. 在 Assets 区域点 `NaraPainter-0.2.0-win-x64.zip` 下载
4. 保存到桌面（会覆盖你原来那份，没关系，内容应该完全一样）

### 核对校验和

在 PowerShell 里：

```powershell
Get-FileHash "C:\Users\Sorce\Desktop\NaraPainter-0.2.0-win-x64.zip" -Algorithm SHA256
```

**期望看到**：

```
Algorithm       Hash                                                                   Path
---------       ----                                                                   ----
SHA256          7A00DDAB4A360D8FBDD648BD4DEF91136333306F92A96E8F6DDEE88B0A7D5BC5       C:\Users\Sorce\Desk...
```

把 Hash 那串和下面这个**逐字符比对**（全大写，共 64 位）：

```
7A00DDAB4A360D8FBDD648BD4DEF91136333306F92A96E8F6DDEE88B0A7D5BC5
```

**一致 → 发布包完好。不一致 → 下载不完整或文件被改动，删掉重新下载。**

### 再跑一次自检

```powershell
$tmp = "$env:TEMP\nrp-release-check"
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
Expand-Archive "C:\Users\Sorce\Desktop\NaraPainter-0.2.0-win-x64.zip" -DestinationPath $tmp
```

检查解压出来的结构：

```powershell
Get-ChildItem $tmp
```

**期望看到**：`app`、`launcher` 两个文件夹，加 `LICENSE`、`README.md`、`RUNNING.txt`、`创建桌面快捷方式.bat`。

跑自检：

```powershell
cd C:\Users\Sorce\Downloads\NaraPainter
& "$tmp\launcher\NaraPainter.exe" --selftest="$PWD\assets\testimages\photo.jpg" --log="$tmp\selftest.log"
Start-Sleep -Seconds 30
Get-Process NaraPainter -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Content "$tmp\selftest.log" | Select-String "RESULT|openLarge|shortcuts"
```

**期望看到**：

```
shortcuts count=23 duplicates=0 bareKeys=[M,Escape,I] textBoxFocusable=yes
openLarge file=large-4000x3000.png size=4000 × 3000 layers=1 ms=193
RESULT PASS
```

---

# 可能踩的坑

## 密码相关

| 症状 | 原因 | 怎么办 |
| --- | --- | --- |
| `Password for ...` 输了登录密码，报 `Authentication failed` | GitHub 不接受账号密码 | 用 2.6 节的 Personal Access Token |
| Token 粘进去没反应、显示为空 | 密码输入不回显是正常的 | 直接回车即可；粘贴用**鼠标右键**或 `Ctrl+Shift+V` |
| `remote: Permission to ... denied` | Token 权限不够 | 检查 Token 的 **Contents** 权限是 **Read and write**，且仓库访问包含了 `NaraPainter` |
| Token 过期了 | 设了 30 天有效期 | 重新生成一个 |

## 推送被拒

| 症状 | 原因 | 怎么办 |
| --- | --- | --- |
| `rejected ... fetch first` 或 `Updates were rejected` | 远端有你本地没有的提交（多半是建仓库时勾了 README / .gitignore / License） | 跑 `git pull --rebase origin main`，然后再 `git push -u origin main`。会弹出编辑器让你写合并说明，直接关掉即可 |
| `remote: error: File ... exceeds GitHub's file size limit of 100.00 MB` | 有超过 100 MB 的文件被提交了 | 这不该发生（zip 已被忽略）。跑 4.2 节的检查确认，有输出就告诉我 |
| `error: remote origin already exists` | 已经加过 remote 了 | 先 `git remote remove origin`，再重新 `git remote add origin ...` |

## 网络问题

| 症状 | 怎么办 |
| --- | --- |
| push 卡在 `Writing objects` 很久 | 正常的，约 4.5 MB 一般不到 1 分钟；超过 5 分钟且无进度，`Ctrl+C` 中断后重试 |
| `Failed to connect to github.com` / 超时 | 网络到 GitHub 不通。检查代理/VPN；国内网络访问 GitHub 不稳定，可多试几次，或换个时段 |
| `SSL certificate problem` | 公司网络做了证书拦截。**不要**用 `GIT_SSL_NO_VERIFY` 绕过；找网络管理员或用手机热点 |
| zip 上传进度条卡住 / 失败 | 刷新页面后 zip 附件会丢，需要重新上传。换网络或用第四步的 `gh` CLI 方式（支持断点重试更好些） |

## 本地仓库

| 症状 | 怎么办 |
| --- | --- |
| `fatal: not a git repository` | 忘了 `cd` 到项目目录。先 `cd C:\Users\Sorce\Downloads\NaraPainter` |
| `git status` 显示一堆改动 | 别急着重置。先看清楚是什么文件，告诉我 |
| 分支叫 `master` 而不是 `main` | 2.5 步的 `git branch -M main` 会改名，照做即可 |
| 想撤销刚才的操作 | 只要没 push，本地都能退。**先别自己 `git reset --hard`**，那会丢改动，先问我 |

## Release 相关

| 症状 | 怎么办 |
| --- | --- |
| `Choose a tag` 下拉里没有 `v0.2.0` | tag 没推上去。回 3.2 重做 `git push origin v0.2.0` |
| 上传中断，附件不见了 | 重新拖一次。已填的标题和说明还在，不用重填 |
| 发布后想改说明 | Release 页面右上角铅笔图标可以编辑 |
| 想删掉重发 | Release 页面右上角 **Delete**；tag 要单独删：`git push --delete origin v0.2.0` 和 `git tag -d v0.2.0` |
| 附件传错了 | Release 页面点附件右侧的 ✕ 可以删掉重传 |

---

# 全部完成后的样子

- 代码在 <https://github.com/SorceressDuan/NaraPainter>
- Release 在 <https://github.com/SorceressDuan/NaraPainter/releases/tag/v0.2.0>
- 附件 `NaraPainter-0.2.0-win-x64.zip`（149.69 MB）可下载，SHA-256 与上表一致
- 仓库里**没有** zip（zip 只在 Release 附件里）

之后的版本发法见 [PUBLISHING.md](PUBLISHING.md) 的「下一次发版」。
