# LigPrint 首次提交清单

生成时间：2026-11-25  
仓库状态：已初始化（`git init -b main`），尚未暂存、尚未提交。  
当前版本：1.3.0  
待提交：40 个文件，约 1.01 MiB。

> 本清单本身也建议一并提交，方便后续维护者了解本次开源边界。

---

## 1. 建议的提交边界

### 1.1 始终提交（源码与开源资料）

| 分类 | 文件 | 数量 |
|---|---|---|
| 仓库配置 / 文档 | `.gitignore`、`LICENSE`、`README.md`、`使用说明.md`、`COMMIT-PLAN.md` | 5 |
| 构建 / 安装包 | `build.ps1`、`installer.iss` | 2 |
| 源码与资源 | `src/*.cs`、`src/public.xml`、`src/sunflower.ico`、`src/toolbar.png` | 16 |
| 工具与测试样例 | `tools/Decode-Log.ps1`、`tools/Verify.ps1`、`tools/VerifyEditor.cs`、`tools/fixtures/labels.xlsx`、`tools/fixtures/legacy.xls` | 5 |
| 第三方依赖与许可 | `vendor/ExcelDataReader.dll`、`vendor/ExcelNumberFormat.dll`、`vendor/zxing.dll` 及 3 份 LICENSE | 6 |

### 1.2 可选提交（分析资料）

| 文件 | 说明 |
|---|---|
| `analysis/功能分析.md` | 功能介绍与证据摘要，适合随源码发布 |
| `analysis/对照与验证.md` | 版本迭代与验证记录，适合随源码发布 |
| `analysis/inspect_reference.py`、`analysis/list_resources.py` | 分析脚本，可选 |
| `analysis/native-evidence.txt`、`analysis/pe-resources.json` | REA 原始证据。`pe-resources.json` 中的本机路径已在上传前替换为 `<local-path>` 占位符，证据哈希与结构保持不变。 |

### 1.3 禁止提交（已由 `.gitignore` 排除）

| 路径 | 原因 |
|---|---|
| `tools/maintenance-private.xml` | 日志解密私钥，泄露后可解密历史维护日志 |
| `dist/LigPrint.exe`、`dist/LigPrint-Setup-x64.exe` | 构建产物，建议在 GitHub Releases 单独发布并附 SHA-256 |
| `feedback/*.png` | 问题反馈截图，可能包含真实业务数据 |
| `analysis/.qa-path`、`analysis/qa-*/`、`*.lplog` | 本地验证输出 / 加密日志 |

---

## 2. 提交前检查

```bash
cd /h/Codex/20261119/LigPrint

# 1. 查看将纳入的文件
git status --short

# 2. 确认敏感文件被忽略
git check-ignore -v tools/maintenance-private.xml dist/LigPrint.exe dist/LigPrint-Setup-x64.exe feedback/

# 3. （建议）确认没有私钥内容混入待提交文本
grep -RIn "BEGIN RSA PRIVATE KEY\|BEGIN PRIVATE KEY" --exclude-dir=.git --exclude-dir=dist --exclude-dir=feedback . || true

# 4. 确认版本一致
grep -RIn "1\.3\.0" README.md src/Startup.cs installer.iss
```

当前 Git 环境未配置 `user.name` / `user.email`，首次提交前请设置：

```bash
git config user.name "你的 GitHub 用户名"
git config user.email "你的 GitHub 邮箱"
```

---

## 3. 暂存与提交

建议采用单个初始提交：

```bash
cd /h/Codex/20261119/LigPrint

git add -A

git diff --cached --stat

git commit -m "feat: initial LigPrint 1.3.0 source release

- WinForms/.NET Framework label designer and batch printing
- DDL template conversion and local printer enumeration
- Excel data binding, PDF export and ZXing barcodes
- encrypted maintenance logs with separated private key
- build scripts, documentation, third-party licenses and REA notes"
```

若希望把分析资料单独成一个提交，可分两步：

```bash
git add .gitignore LICENSE README.md 使用说明.md COMMIT-PLAN.md build.ps1 installer.iss src tools vendor
git commit -m "feat: initial LigPrint 1.3.0 source release"

git add analysis
git commit -m "docs: add REA-based feature analysis and verification notes"
```

> `analysis/pe-resources.json` 中的本机路径已替换为 `<local-path>`，可直接执行 `git add analysis`。

---

## 4. 推送到 GitHub

```bash
git remote add origin https://github.com/<你的用户名>/LigPrint.git
git branch -M main
git push -u origin main
```

发布二进制时，在 GitHub Releases 单独上传：

- `dist/LigPrint.exe`（便携版，SHA-256 以实际产物为准）
- `dist/LigPrint-Setup-x64.exe`（安装包，SHA-256 以实际产物为准）
- 对应版本的更新说明

---

## 5. 提交后复核

```bash
# 确认私钥和产物没有被提交
git ls-files | grep -E "maintenance-private\.xml|^dist/|^feedback/" || true

# 确认提交内容
git show --stat --oneline HEAD

# 确认分支与远端
git status -sb
```

---

## 6. 待人工确认事项

1. 是否公开 `analysis/` 下全部 REA 证据；本机路径已处理为占位符，仍建议最终复核。
2. Git 提交身份尚未配置。
3. 真实实体标签机走纸与真实 `.ddl` 样本仍建议现场验证；这不影响源码提交。
