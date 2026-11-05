# JSON 技术记录格式

当前 `SchemaVersion` 为 `7`。枚举值使用英文代码，不随界面语言变化。无法取得的值为 `null`。硬件名称（如 Windows 名称）保持系统返回的原文。

## 顶层

| 字段 | 说明 |
| --- | --- |
| `SchemaVersion`、`ToolVersion`、`GeneratedAt` | 格式版本、软件版本、生成时间 |
| `Hardware` | 硬件清单 |
| `Sensors` | 生成时的传感器快照 |
| `Cooling` | 散热推测；USB 水冷能报告产品名时含 `CoolerName`，否则为 `null` |
| `TestRun` | 测试结果；未运行时为 `null` |
| `Temperatures` | 温度汇总行：`IsCpu`、`Phase`、`Minimum`、`Maximum`、`Average`、`State`；未运行测试时为 `null` |
| `LoadMetrics` | 负载下的指标：`Metric`（`CpuPower`、`CpuClock`、`CpuVoltage`、`GpuPower`）、`Average`、`Maximum`、`Unit`；读不到的项目没有这一行 |
| `MemoryTest` | 内存测试结果：`End`、`TestedBytes`、`Passes`、`Errors`、`Seconds`、`Detail` |
| `DiskTest` | 硬盘测试结果：`End`、`Drive`、`SizeBytes`、读写速度、`VerificationErrors`、`Detail` |
| `AudioCheck` | 员工确认结果：`SpeakerHeard`、`MicrophoneHeard`；未运行的项目为 `null` |

## `Hardware`

| 字段 | 说明 |
| --- | --- |
| `Environment` | `OperatingSystem`、`Activation`、`IsAdministrator`、`IsPawnIoInstalled`、`IsSensorAccessLimited` |
| `Cpus`、`Gpus`、`Motherboard`、`Memory`、`Disks`、`Displays` | 设备信息 |
| `Diagnostics` | 读取失败的来源和原因 |
| `Network` | `Kind`（`WiFi`、`Wired`、`None`）、`Adapter`、`LinkMegabits`、`SignalPercent` |
| `Security` | `SecureBootEnabled`、`TpmVersion` |
| `SystemDrive` | `UsedBytes`、`TotalBytes` |
| `DiskHealth` | 每块硬盘的 `Status`（`Good`、`Caution`、`Bad`、`Unknown`）、寿命、温度、`MediaErrors`（0E）、通电时间等 |
| `Peripherals` | `Kind`（`Input`、`Speaker`、`Microphone`、`Camera`、`Printer`、`Touchpad`、`Touchscreen`、`Gamepad`）、`Name`、`IsBluetooth` |
| `WiredAdapters`、`WirelessAdapters` | 有线网卡和无线网卡名称 |
| `Environment.OperatingSystem.InstallDate` | Windows 安装日期 |
| `Locale` | `DisplayLanguage`、`Format`、`SystemLocale`、`AnsiCodePage` |
| `Reliability` | `WheaErrors`（近 30 天）、`MicrocodeRevision`、`LastBootAt` |
| `Cpus[]` 附加字段 | `Socket`、`L2CacheKilobytes`、`L3CacheKilobytes`、`Identifier`、`IsVirtualizationEnabled` |
| `Gpus[]` 附加字段 | `DriverProvider`、`DriverDate` |
| `Computer` | 品牌机和笔记本的 `Manufacturer`、`Model`、`IsLaptop`；自己组装的电脑为 `null` |
| `Battery` | `DesignedMilliwattHours`、`FullMilliwattHours`、`HealthPercent`；没有电池时为 `null` |

`Activation`：`Activated`、`GracePeriod`、`Notification`、`NotActivated`、`Unknown`。

`Gpus[].Kind`：`Discrete`、`Integrated`、`Software`、`Unknown`。

## `Sensors[]`

`HardwareId`、`HardwareName`、`HardwareType`、`SensorId`、`SensorName`、`SensorType`、`Value`、`Unit`、`Status`（`Ok`、`NoValue`、`Invalid`）、`Source`、`SampledAt`。

## `Cooling`

`Outcome`：`LikelyLiquid`、`LikelyAir`、`AbnormalWiring`、`NoData`。另有 `PumpRpm`、`CpuFanRpm`、`UnlabeledRunningFans`、`Notes`。

## `TestRun`

| 字段 | 说明 |
| --- | --- |
| `Profile` | 实际使用的时长和温度阈值 |
| `StartedAt`、`EndedAt` | 起止时间 |
| `Overall` | `NoObviousIssue`、`NeedsReview`、`Incomplete` |
| `Phases[]` | 见下 |
| `Statistics[]` | 见下 |
| `Anomalies[]` | `Code`、`Phase`、`At`、`Detail` |
| `AverageMethod` | 当前为 `ArithmeticMeanOfValidSamples` |
| `SensorReads`、`SensorReadFailures` | 读取次数和失败次数 |

### `Phases[]`

| 字段 | 说明 |
| --- | --- |
| `Phase` | `Idle`、`GpuStress`、`CpuStress` |
| `Status` | `Passed`、`IssueFound`、`Incomplete`、`Unsupported`、`NotRun`、`InsufficientData` |
| `IsRequested` | 本次是否要求运行。未要求的项目不影响整体结果 |
| `PlannedSeconds`、`ActualSeconds` | 计划和实际时长 |
| `TargetDevice` | 目标 GPU |
| `AverageLoadPercent`、`MaximumLoadPercent`、`LoadSource` | 负载和来源：`Sensor` 或 `ProcessTime` |
| `TemperatureSensor` | 用于判定的温度传感器 |
| `WorkUnits`、`VerificationErrors` | 完成的工作单元和校验错误数 |

### `Statistics[]`

每个传感器在每个阶段一条：`Phase`、`HardwareId`、`HardwareType`、`HardwareName`、`SensorId`、`SensorName`、`SensorType`、`Unit`、`Minimum`、`Maximum`、`Average`、`ValidSamples`、`TotalSamples`、`ExpectedSamples`、`LongestGapSeconds`、`IsSufficient`。

有效样本不足预期的一半时，`IsSufficient` 为 `false`。

双烤时 CPU 和显卡同时满载，这段时间的统计 `Phase` 为 `DualStress`；`Phases[]` 仍按 `GpuStress`、`CpuStress` 分别给出结果。

`Anomalies[].Code`：`CpuVerificationError`、`GpuVerificationError`、`GpuDeviceLost`、`GpuDriverReset`、`StressAbnormalExit`、`StressTimedOut`、`InsufficientSamples`、`LoadBelowTarget`、`LoadUnverified`、`TemperatureProtection`、`SensorLost`、`TemperatureUnavailable`、`IdleConditionNotMet`、`GpuUnsupported`、`UserStopped`。
