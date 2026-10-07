# 分辨「哪个才是仓库」并安全推送

给分不清两个同名文件夹的情况写的。**核心结论先给**：

| 位置 | 是什么 | 有 `.git` | 有源码 | 能 push |
| --- | --- | --- | --- | --- |
| `C:\Users\Sorce\Downloads\compositor` | **真仓库**：28 个提交、`main` 分支、`NaraPainter.sln` | ✅ | ✅ | ✅ **只有它** |
| `C:\Users\Sorce\Desktop\NaraPainter` | **发布包解压产物**：`app/`(368 文件) + `launcher/`(186 文件) + 4 个文档 | ❌ | ❌ 无 `src/`、无 `.sln` | ❌ |
| `C:\Users\Sorce\Desktop\github` | 只有 `github-recovery-codes.txt`，与本项目无关 | ❌ | ❌ | ❌ |

---

## 一、怎么快速分辨（不用记路径）

### 命令 1：这个目录是 git 仓库吗？

```powershell
git rev-parse --is-inside-work-tree
```

- 输出 `true` → **是**仓库（或在仓库的子目录里）
- 输出 `fatal: not a git repository (or any of the parent directories): .git` → **不是**

### 命令 2：仓库的根目录在哪？（最重要的一条）

```powershell
git rev-parse --show-toplevel
```

它告诉你**当前所在的仓库根目录的绝对路径**。无论你在哪层子目录里，答案都是同一个根。

判据：**输出必须是 `C:/Users/Sorce/Downloads/compositor`**。
如果是别的路径，你就站错地方了。

### 命令 3：当前分支和最近提交

```powershell
git branch --show-current
git log --oneline -3
```

期望：

```
main
73e1080 Add the release notes export and a step-by-step publishing guide
a5f5bdd Prepare the repository for a public release
efad9f2 Give the newer tools their shortcuts, and stop the language tests racing each other
```

### 命令 4：远端地址对不对

```powershell
git remote -v
```

- **完全没输出** → 还没配远端（当前就是这个状态），用第五节的命令配上
- 有输出 → 必须看到 `https://github.com/SorceressDuan/NaraPainter.git`，**用户名和仓库名都不能错**

### 命令 5：一次性体检（推荐）

```powershell
cd C:\Users\Sorce\Downloads\compositor
git rev-parse --show-toplevel
git branch --show-current
git status --short
git remote -v
```

四条的输出对照：

| 命令 | 期望结果 |
| --- | --- |
| `rev-parse --show-toplevel` | `C:/Users/Sorce/Downloads/compositor` |
| `branch --show-current` | `main` |
| `status --short` | 没有输出（工作区干净） |
| `remote -v` | 两行 `origin ... SorceressDuan/NaraPainter.git` |

### 一条保命命令：push 之前先自检

把下面这段整个粘贴执行，**任何一项不对它会拒绝继续**：

```powershell
cd C:\Users\Sorce\Downloads\compositor
$top = (git rev-parse --show-toplevel) -replace '/', '\'
$remote = (git remote get-url origin 2>$null)

$ok = $true
if ($top -ne 'C:\Users\Sorce\Downloads\compositor') { Write-Host "✗ 目录不对: $top" -ForegroundColor Red; $ok = $false } else { Write-Host "✓ 目录: $top" -ForegroundColor Green }
if ((git branch --show-current) -ne 'main') { Write-Host "✗ 分支不是 main" -ForegroundColor Red; $ok = $false } else { Write-Host "✓ 分支: main" -ForegroundColor Green }
if ($remote -ne 'https://github.com/SorceressDuan/NaraPainter.git') { Write-Host "✗ 远端不对: $remote" -ForegroundColor Red; $ok = $false } else { Write-Host "✓ 远端: $remote" -ForegroundColor Green }
if ((git status --porcelain)) { Write-Host "✗ 工作区不干净" -ForegroundColor Red; $ok = $false } else { Write-Host "✓ 工作区干净" -ForegroundColor Green }

if ($ok) { Write-Host "`n可以推送: git push -u origin main" -ForegroundColor Cyan } else { Write-Host "`n先解决上面的问题，不要推送" -ForegroundColor Yellow }
```

## 二、桌面上那个 NaraPainter 是什么，能删吗

**它是发布包解压出来的东西**，是给最终用户的东西，不是开发用的。证据：

- 有 `app/`（368 文件）、`launcher/`（186 文件）、`NaraPainter.exe`
- **没有** `src/`、**没有** `.sln`、**没有** `.git`
- 总共 558 个文件 373 MB

**它没有任何独有内容**：你桌面上的 `NaraPainter-0.2.0-win-x64.zip`（149.69 MB）解压出来就是它，
而那个 zip 在 `C:\Users\Sorce\Downloads\compositor\dist\` 里也有一份完全相同的东西。

> 注意：它是用**你桌面上那个 zip** 解压的，还是别人给的，都无法从内容区分——但两者内容一致，
> 所以删掉它不丢任何东西。

### 怎么安全地删

**先确认它确实是解压产物，再删**：

```powershell
$d = "C:\Users\Sorce\Desktop\NaraPainter"

# 1) 必须没有 .git
Test-Path "$d\.git"                      # 期望 False
# 2) 必须没有源码
Test-Path "$d\src"                       # 期望 False
(Get-ChildItem $d -Filter *.sln -File).Count   # 期望 0
# 3) 必须不像一个 git 仓库
git -C $d rev-parse --is-inside-work-tree 2>&1   # 期望 fatal: not a git repository
```

三条都符合预期后，才删除：

```powershell
# 先重命名而不是直接删，给自己留一次反悔机会
Rename-Item "C:\Users\Sorce\Desktop\NaraPainter" "NaraPainter-解压产物-可删"
```

用几天没问题，再真正删掉：

```powershell
Remove-Item "C:\Users\Sorce\Desktop\NaraPainter-解压产物-可删" -Recurse -Force
```

> **不要删** `C:\Users\Sorce\Desktop\github` 里的 `github-recovery-codes.txt`：
> 那是你 GitHub 账号的两步验证恢复码，跟本项目无关，很重要。建议把它移到一个私密位置
> （不要留在桌面，也不要提交进任何仓库）。

> **不要删** `NaraPainter-0.2.0-win-x64.zip`：发 Release 时要把这个附件上传。

## 三、如果桌面那个才是真仓库怎么办

按第一节的命令 2 检查就能确定，结论是**它不可能是**（没有 `.git`）。
真仓库的全盘搜索结果只有四处，与本项目相关的只有 `Downloads\compositor`：

```
C:\Users\Sorce\Downloads\compositor\.git      ← 本项目
C:\Users\Sorce\Downloads\知识殿堂\.git
C:\Users\Sorce\Desktop\是魔法\.git
C:\Users\Sorce\Desktop\灰眸\模拟器\.git
```

万一你以后遇到「桌面那个真有 `.git`」的情况，判断依据是**提交数和提交历史**，不是文件夹名：

```powershell
cd "桌面那个目录"
git log --oneline | Measure-Object -Line    # 提交数
git log --oneline -3                        # 最近三条
git remote -v                               # 远端
```

本项目的真仓库特征是：**28 个提交、最近三条是 `73e1080` / `a5f5bdd` / `efad9f2`**。
对不上就不是它。

## 四、推送前建议：先修提交身份

现在你的仓库里记录的作者是占位值，**这会让 GitHub 上的 28 个提交全显示成 "Compositor Port"，
而且不关联到你的账号**（你的贡献图也不会亮）。

```powershell
cd C:\Users\Sorce\Downloads\compositor

git config user.name  "SorceressDuan"
git config user.email "你的GitHub邮箱"

# 确认
git config user.name
git config user.email
```

> `user.email` 填你 GitHub 账号绑定的那个邮箱（GitHub → Settings → Emails 里能看到）。
> 填对了提交才会算到你名下。

**这不会改动已有的 28 个提交**，它们仍然是 "Compositor Port"。想让历史也归到你名下，
需要在**首次 push 之前**重写全部提交的作者：

```powershell
# 警告：会改写全部 28 个提交的 SHA，本地尚未 push 所以安全；push 之后就不能这么做了
git filter-branch --env-filter '
export GIT_AUTHOR_NAME="SorceressDuan"
export GIT_AUTHOR_EMAIL="你的GitHub邮箱"
export GIT_COMMITTER_NAME="SorceressDuan"
export GIT_COMMITTER_EMAIL="你的GitHub邮箱"
' -- --all
```

要不要做由你决定：**不做也能正常开源**，只是提交记录显示的是 "Compositor Port"。

## 五、配置远端并推送

```powershell
cd C:\Users\Sorce\Downloads\compositor
git remote add origin https://github.com/SorceressDuan/NaraPainter.git
git push -u origin main
```

推送时如果要密码：**不能用 GitHub 登录密码**，要用 Personal Access Token
（生成步骤见 [GITHUB_GUIDE.md](GITHUB_GUIDE.md) 第二节的「怎么弄 Token」）。

推送成功的标志：

```
 * [new branch]      main -> main
branch 'main' set up to track 'origin/main'.
```

推送大小约 4.5 MB（**不含那个 149.69 MB 的 zip**，它被 `.gitignore` 排除，走 Release 附件）。

推完立刻验证：

```powershell
git remote -v
git status
git ls-files | Select-String -Pattern "\.(zip|msix)$"    # 必须没有输出
```

`git status` 期望看到 `Your branch is up to date with 'origin/main'.`

## 六、顺序建议

1. 跑第一节的「保命命令」自检 —— 确认目录 / 分支 / 远端 / 干净
2. 跑第四节的 `git config` 修身份（可选但建议）
3. 跑第五节的 `git remote add` + `git push`
4. 推完跑第五节的验证
5. 然后按 [GITHUB_GUIDE.md](GITHUB_GUIDE.md) 第三、四步发 Release、上传 zip、核对 SHA-256
6. 都成功后，再按第二节把桌面那个解压产物删掉

**第 6 步放最后**：万一推送过程中需要对照文件，留着它没坏处。
