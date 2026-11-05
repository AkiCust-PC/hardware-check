# 发布清单

全部完成前不要公开下载。

## 硬件验证

在有风扇的真实电脑上，双击运行并点“是”。详细结果记入 [验证记录](validation-record.md)。

- [ ] Intel LGA1700 / LGA1851 平台
- [ ] AMD AM5 平台
- [ ] NVIDIA 独立显卡
- [ ] AMD 独立显卡
- [ ] 不同厂商主板的 CPU_FAN 和 PUMP 传感器名称
- [ ] 水冷、风冷、只接 PUMP 三种接线的散热判断
- [ ] 无独立显卡的电脑
- [ ] 笔记本：整机型号、电池健康、触控板、内置屏幕和扬声器
- [ ] SATA 硬盘和 USB 硬盘盒的健康读取
- [ ] 接上扬声器和麦克风后的识别，扬声器测试和麦克风回放实际听一次
- [ ] 带 UAC 提示的电脑：点“是”、点“否”两种路径；未装 PawnIO 时自动安装，退出后自动卸载
- [ ] 查清并关闭验证记录中的启动崩溃（0xC0000409）
- [ ] USB 水冷的品牌显示；Wi-Fi 信号；无 TPM 或传统 BIOS 的电脑
- [ ] 真实双显卡（不同型号、同型号）的目标选择和传感器对应
- [ ] 未安装 .NET 的干净 Windows 10 和 Windows 11
- [ ] 标准流程完整运行一次（约 5 分钟）
- [ ] 驱动异常场景（设备丢失、驱动重置）
- [ ] 实际打印 A4 测试报告
- [ ] 有 CPU 传感器权限时的功耗、频率、电压读数；AMD 和 NVIDIA 的传感器名称
- [ ] 在有散热的电脑上跑完整双烤，确认整机功耗下负载和温度保护
- [ ] 安全启动未开启的电脑：重启进入 BIOS（`shutdown /r /fw`）
- [ ] 把真实工具导出的 JSON 导入网站服务表单并打印

## 法务

- [ ] 审阅 `THIRD-PARTY-NOTICES.md` 中的待确认项
- [ ] 确认报告文案（尤其是免责说明）的日文表述

## 发布

- [ ] 确认 `Directory.Build.props` 的版本号
- [ ] 运行 `scripts/publish.ps1`，确认体积没有异常增长（v0.1.2：程序 55.6 MB，压缩包 49.4 MB）
- [ ] 创建独立仓库，在 GitHub Release 上传 `AkikazuHardwareCheck-win-x64.zip`（需要单独授权）
- [ ] 在网站仓库的 `app/data/tools/hardware-check-release.json` 填入 `url`、`sha256`、`version`。`url` 为 `https://github.com/<owner>/<repo>/releases/latest/download/AkikazuHardwareCheck-win-x64.zip`，`sha256` 取 zip 的值。`url` 为空时网站下载按钮保持禁用
- [ ] 每次发新版都同步上述 `sha256` 和 `version`：`latest` 链接会自动指向新版，网页上的版本和校验值不会
- [ ] 尽可能完成代码签名，并在签名后重新计算 SHA-256
- [ ] 在网站发布：用途、支持的 Windows 版本、版本号、下载链接、文件大小、SHA-256、更新记录、使用注意事项
- [ ] 确认 ZIP 内含 `licenses/` 和 `THIRD-PARTY-NOTICES.md`（发布脚本已包含）

网站规划路径为 `/tools/hardware-check/` 和 `/downloads/`。网站页面和部署不在本目录内，需要单独授权和实施。
