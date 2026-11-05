# AGENTS.md

## Project

- 秋和PC 硬件测试工具：Windows 硬件检测和压力测试。C#、.NET 10、WinForms、LibreHardwareMonitorLib、Vortice。
- UI 支持 `ja-JP`、`zh-CN`、`zh-TW`、`en-US`，默认跟随系统语言。
- 公开仓库，MIT 许可证。不提交客户信息、密钥、Token，也不提交开发机的个人路径或个人邮箱。
- 常用入口：`scripts/run.cmd`（双击打开窗口）、`scripts/run.ps1`、`scripts/test.ps1`、`scripts/publish.ps1`；VS Code 按 F5 调试，任务里有运行、测试和打包。
- 产品说明见 `README.md`，已验证和未验证内容见 `docs/validation-record.md`，发布步骤见 `docs/release-checklist.md`，JSON 格式见 `docs/json-format.md`。

## Communication

- 默认使用用户母语；结论优先，采用短句、分组列表和准确术语，仅保留必要证据、决定与限制。
- 不回贴整文件或整页结果。
- 完成修改后说明改动、验证、commit、push、Release 和网站状态；这些证据分别报告，不得互相替代。

## Authorization and Git

- “只读”“只检查”“先分析”“先不改”只允许调查和报告。诊断不自动授权修复。
- 完成用户授权的修改并通过验证后自动 commit；用户明确要求不提交时除外。未经明确授权不得 push 或发布 Release。
- 开始和提交前检查分支、HEAD、`git status` 与目标 diff；不 stash、不改写历史，不覆盖或夹带其他会话的修改。
- commit 前确认 `user.name` 为 `Expurgator`、`user.email` 为 GitHub noreply 地址，不得添加 `Co-authored-by`、AI 署名及生成工具标记。
- commit message 使用 `<type>(<scope>): <subject>`；subject 为英文且不超过 20 个字符。正文每行不超过 100 字符。

## Product rules

- **原生 UI**：只用系统标准控件和 Win32 原生菜单，不自绘、不自定义配色。Windows 按自身版本绘制，并跟随浅色或深色。状态色只用于表达异常。
- **信息显示**：读不到的项目和没有的设备都不显示，不写“无法取得”或“无”。不把缺失的数据当作硬件故障，也不把未验证的结果判为通过。
- **长内容**：换行，不用省略号。名称和版本只放系统标题栏。
- **文案**：用户可见文本都在 `Core/Localization/*.json`，四种语言同步。使用短词短句。
- **隐私**：不读取 Windows 产品密钥、磁盘文件内容、Wi-Fi 名称、主机名、用户名，也不读取主板、内存、硬盘、显示器的序列号。数据只保存在本地，不上传。
- **安全**：不做破坏性磁盘测试，不写自己的内核驱动，不改变温度保护的默认值而不说明理由。

## PawnIO 驱动

- 只内置官方签名、未修改的安装程序，运行前校验固定的 SHA-256；升级时同步哈希、`THIRD-PARTY-NOTICES.md` 和验证记录。
- 由本次运行安装的驱动在退出后删除。官方卸载程序不删服务和驱动包，由工具补删；主程序退出前占用驱动，所以由子进程在它退出后执行。
- 只有运行前系统里没有该服务时才删服务和驱动包，绝不删除其他软件在用的驱动。
- 自动提权和安装驱动会改变系统。测试前说明影响；普通测试加 `--no-elevate`。开发机若已关闭 UAC 提示，提权不会弹窗，不能代表客户电脑。

## Language, region and encoding

- 工具语言和 Windows 区域相互独立。显示给用户的数字和日期一律用 `CultureInfo.InvariantCulture`，不随区域变化。
- 调用带字符串的 Win32 接口必须用 Unicode 版本。文件一律 UTF-8。不解析系统工具的本地化文本输出，用错误码或直接核对结果。
- 涉及显示或报告的改动，要保证 `CultureTests` 覆盖到。

## Engineering

- 最小变更优先：只改需求相关代码，单次提交保持单主题；避免无关重构和批量格式化。
- 分层：`Core` 放硬件、测试、判定和报告，不依赖 WinForms；`App` 只放界面；`Probe` 是控制台诊断工具。
- 同一逻辑出现至少三次且语义一致时才抽取。函数保持单一职责，用守卫子句压平控制流。注释解释业务规则和原因，不复述代码。
- 业务阈值、时长、尺寸使用有单位和含义的 `SCREAMING_SNAKE_CASE` 常量。避免不必要的类型断言和警告抑制。
- 新增依赖前核对现有能力，并评估活跃度、体积和许可证；同步 `THIRD-PARTY-NOTICES.md` 和 `licenses/`。
- 未发布的废弃实现直接移除。JSON 字段发生破坏性变化时升 `CURRENT_SCHEMA_VERSION`，并同步 `docs/json-format.md` 和网站导入。

## Testing and evidence

- 修 bug 先写可复现测试并确认失败，再修复并确认通过。所有修改运行 `git diff --check`。
- 单元测试用 `scripts/test.ps1`。核对测试总数：数量意外减少说明有问题，即使显示通过。批量替换脚本在匹配失败时必须中止，不得写回文件。
- 界面改动要实际打开窗口检查，覆盖受影响的语言和状态；发布前用单文件发布版再跑一次。
- 分别报告单元测试、窗口实测、发布版实测和真实硬件验证；未执行的标记 `Unverified`。不得把模拟环境的结果说成真实系统通过。
- 只有真实运行过的结果才写入 `docs/validation-record.md`。

## Release

- 版本号在 `Directory.Build.props`。`scripts/publish.ps1` 生成固定文件名的 `AkikazuHardwareCheck-win-x64.zip`。
- 用正式 Release 并标为 latest，不用 prerelease 标记：`releases/latest/download` 不会指向 prerelease。未完成硬件验证时在标题和说明里写“预览版”。
- 发布后从 latest 链接下载并核对 SHA-256。
- 网站 `AkiCust-PC/my-site` 的 `app/data/tools/hardware-check-release.json` 要同步版本号和 SHA-256；网站导入按 `SchemaVersion` 精确匹配，升版本要同步 `app/utils/forms/hardwareReportImport.ts`。推送网站 `main` 会自动部署到生产环境，需要明确授权。
