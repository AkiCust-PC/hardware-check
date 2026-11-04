# 第三方依赖与许可证

版本以 `src/AkikazuHardwareCheck.App/packages.lock.json` 为准。下表于 2026-11-04 依据 NuGet 包元数据和各项目官方页面核对。这是事实整理，不是法律意见。

## 随程序分发

| 依赖 | 版本 | 许可证 | 用途 | 来源 |
| --- | --- | --- | --- | --- |
| LibreHardwareMonitorLib | 0.9.6 | MPL-2.0 | 硬件传感器 | <https://github.com/LibreHardwareMonitor/LibreHardwareMonitor> |
| PawnIO.Modules（嵌入 LibreHardwareMonitorLib） | 随 0.9.6 | LGPL-2.1 | 传感器访问模块 | <https://github.com/namazso/PawnIO.Modules> |
| BlackSharp.Core | 1.0.7 | MPL-2.0 | LibreHardwareMonitorLib 的依赖 | <https://github.com/Blacktempel/BlackSharp> |
| DiskInfoToolkit | 1.1.2 | MPL-2.0 | LibreHardwareMonitorLib 的依赖 | <https://github.com/Blacktempel/DiskInfoToolkit> |
| RAMSPDToolkit-NDD | 1.4.2 | MPL-2.0 | LibreHardwareMonitorLib 的依赖 | <https://github.com/Blacktempel/RAMSPDToolkit> |
| HidSharp | 2.6.4 | Apache-2.0 | LibreHardwareMonitorLib 的依赖 | <https://software.seekye.com/hidsharp> |
| Mono.Posix.NETStandard | 1.0.0 | 见下方待确认 | LibreHardwareMonitorLib 的依赖 | <https://www.nuget.org/packages/Mono.Posix.NETStandard/1.0.0> |
| Vortice.Direct3D11、Vortice.DXGI、Vortice.D3DCompiler、Vortice.DirectX | 3.8.3 | MIT | Direct3D 11 GPU 负载和 GPU 类型判断 | <https://github.com/amerkoleci/Vortice.Windows> |
| PawnIO 安装程序（含驱动，未修改） | 2.2.0 | GPL-2.0-or-later，附带例外 | CPU 温度和主板传感器的驱动 | 见下方 |
| Vortice.Mathematics | 2.1.0 | MIT | Vortice 的依赖 | <https://github.com/amerkoleci/Vortice.Mathematics> |
| SharpGen.Runtime、SharpGen.Runtime.COM | 2.4.2-beta | MIT | Vortice 的依赖 | <https://github.com/SharpGenTools/SharpGenTools> |
| System.Management、System.IO.Ports 等 .NET 包和 .NET 运行时 | 见锁文件 | MIT | WMI、系统组件和自包含运行时 | <https://dot.net/> |

许可证全文在 `licenses/`。发布脚本会同时复制构建所用 .NET SDK 的 `LICENSE.txt` 和 `ThirdPartyNotices.txt`。

本工具未修改上述依赖的任何文件。依赖的完整源代码可从上表地址取得。

## 依赖选择理由

| 选择 | 理由 | 替代方案 |
| --- | --- | --- |
| Vortice（MIT） | 活跃维护，有完整类型，直接调用系统 Direct3D 11 和 `d3dcompiler_47.dll`，不带原生引擎 | 手写 COM 虚表：容易出错且难维护。第三方压力测试程序：发行方式和调用可靠性更差 |
| xunit.v3（仅开发） | 自托管运行，不依赖 testhost | MSTest 同样可用，但这里 xunit 用法更简单 |

## PawnIO 驱动

本工具内置官方安装程序 `third-party/pawnio/PawnIO_setup.exe`，运行时释放到临时目录并静默安装（`-install -silent`），事后删除临时文件。

- 版本 2.2.0，签名有效（namazso.eu），SHA-256 `1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032`。程序运行前校验此值。
- 再分发依据：安装程序自身包含文字 “This installer can be redistributed unmodified.”。文件未作任何修改。
- 驱动许可证全文：`licenses/PawnIO-COPYING.txt`。例外允许通过 device IO control 接口通信的独立模块组合分发。
- 源代码：<https://github.com/namazso/PawnIO>、<https://github.com/namazso/PawnIO.Setup>、<https://github.com/namazso/PawnIO.Modules>。

## 程序内查看许可证

下载的是单个程序，许可证全文和本文件也嵌入其中，可在“设置 → 第三方许可”打开。

## 正式发布前待确认

| 项目 | 现状 | 建议 |
| --- | --- | --- |
| Mono.Posix.NETStandard | 包内没有许可证文本，只有链接，该链接指向 Mono 项目的 `LICENSE` 文件。文件说明运行时和类库总体为 MIT，但没有点名这个包 | 采用 Mono 总体许可证并保留链接；介意时向 Mono 项目确认 |
| PawnIO 再分发 | 依据安装程序自身的再分发声明；GPL 驱动的二进制分发已附许可证全文和源码地址 | 公开下载前请法律审阅 |
| LGPL-2.1 义务 | 已附许可证全文和源码地址。LibreHardwareMonitorLib 0.9.6 嵌入模块对应的确切版本未核实，单文件发布也会影响替换模块的便利性 | 公开下载前请法律审阅，并确定嵌入模块对应的源码版本 |
