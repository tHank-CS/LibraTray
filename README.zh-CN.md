# LibraTray

![许可证：Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-blue.svg)
![平台：Windows 10 和 11](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4.svg)

LibraTray 是一个正在开发中的、托盘优先、纯局域网的 Windows 控制器，目标设备为
**Yeelight Libra Pro**。该产品也常以 **Yeelight LED Screen Light Bar Pro**
或 **Yeelight Monitor Light Bar Pro** 销售，硬件型号为 **YLTD003**，局域网协议
内部型号为 `lamp15`。项目重点是即时控制和可靠的双通道状态同步。

> **当前状态：**仓库已完成阶段 A/B，阶段 C 正在进行。初版托盘优先 WPF 外壳、
> 可信局域网发现、已验证的主灯/氛围灯控制、通知复读、受限重试/节流和冷启动恢复
> 均已实现。全局快捷键、设置、Windows 自动化、打包、签名和 GitHub Release
> 仍待完成。氛围灯左右分区 RGB 命令虽能即时生效，但它与已观察到的冷启动故障
> 之间尚未完成因果隔离，因此仍未开放。

[English](README.md) | 简体中文

## 截图

初版快速面板已经实现。完成视觉验收后会用真实截图替换本段；当前不会把效果图冒充为
已实现的软件。

## 为什么创建 LibraTray？

现有 Yeelight 工具通常是智能家居集成、通用灯泡客户端，或针对其他显示器挂灯的工具。
LibraTray 刻意保持更窄的范围：

- 只通过局域网工作，不要求小米/Yeelight 账号、云端 Token 或公网服务；
- 分别维护主灯与氛围灯状态；
- 未来提供紧凑的 Windows 托盘工作流，而非常驻仪表盘；
- 明确协调软件命令、设备通知、状态查询与实体旋钮产生的变化；
- 先通过诊断探测工具验证协议，再把产品专用行为放入正式应用。

后三项是设计目标。当前里程碑提供探测与协议基础，并非完整桌面应用。

## 支持设备与身份

| 显示名称 | 硬件型号 | 内部型号 | 状态 |
| --- | --- | --- | --- |
| Yeelight Libra Pro | YLTD003 | `lamp15` | 首要目标；固件 38 的部分阶段 B 行为已实机验证 |

Yeelight 官方资料对 YLTD003 使用了不止一个商品名。`lamp15` → YLTD003 →
Yeelight Libra Pro 是基于多个官方来源形成的**高可信跨来源推断**，而不是 Yeelight
在单一声明中给出的全球统一命名。LibraTray 选择“Yeelight Libra Pro”作为默认友好名，
同时分开保留硬件型号和内部型号供诊断使用。详见
[产品身份调研](docs/research/product-identity.md)。

项目绝不会仅凭名称中出现“Libra”“Pro”或“Screen Light Bar”就识别设备。未知型号
保持未知；没有证据时，不把其他 Libra、Pro、Pura 或 YLTD 产品当作 `lamp15`。

## 功能

当前里程碑已有：

- Yeelight 局域网 UDP 发现；
- 手动地址协议探测；
- 以 CRLF 分隔的 JSON 请求/响应分帧；
- 命令 ID、超时、取消和有界输入处理；
- 安全的产品身份映射与未知设备回退；
- 面向脱敏的诊断输出；
- Windows 托盘图标、右键菜单与实时快速面板；
- 已验证的主灯电源、亮度和 3000–6500 K 色温控制；
- 已验证的氛围灯电源、亮度和整灯 RGB 预设；
- 有界重试、通知复读和单连接命令节流；
- 自动化测试与模拟设备测试面。

实机验证后计划实现：

- 可配置全局快捷键与可选 OSD；
- 本地预设；
- 可逐项选择的锁定、解锁、睡眠、唤醒与显示器电源自动化；
- 便携包和安装程序。

屏幕拾色、音乐/游戏灯效以及通用 Yeelight 客户端不属于首个稳定版范围。

## 系统要求

- Windows 10 21H2（内部版本 19044）或更高版本，或 Windows 11；
- x64 处理器；
- 从源码构建需要 .NET 10 SDK；
- 电脑与挂灯位于同一可信局域网，且网络允许组播；
- 如果当前地区与固件的 Yeelight/米家应用提供局域网控制开关，需要先启用它。

仅仍处于 Microsoft 服务周期内的 Windows 版本属于正式支持范围。在已停止服务的
Windows 构建上运行只能视为尽力兼容。

## 下载、安装与便携版

目前没有可下载的安装包或 GitHub Release。请不要从非官方软件下载站获取 LibraTray。

未来的便携版计划提供自包含 `win-x64` 压缩包：解压到当前用户可写目录后直接运行，
不需要管理员权限。只有正式 Release 发布后，这些步骤才可实际执行。项目未获得代码
签名证书前，Windows 可能对未签名程序显示警告。

## 首次连接与协议探测

1. 将 Yeelight Libra Pro 与电脑连接到同一可信局域网。
2. 如果厂商应用在当前设备、固件和地区提供“局域网控制”，请先启用。
3. 将 Windows 网络配置文件设为“专用”。如果防火墙询问，只允许专用网络。
4. 查看当前版本探测工具的命令：

   ```powershell
   dotnet run --project tools/LibraTray.Probe -- --help
   ```

5. 启动发现：

   ```powershell
   dotnet run --project tools/LibraTray.Probe -- discover
   ```

6. 在尝试任何产品专用实验前，确认原始型号精确为 `lamp15`。先执行只读发现与状态
   检查，不发送未记录命令。

v0.1.0 前命令行可能调整；当前检出版本的 `--help` 才是准确信息。实机测试前请阅读
[测试指南](docs/testing-guide.md)。默认 JSONL 日志创建在
`%LOCALAPPDATA%\LibraTray\logs`。如果使用 `--log-path`，必须指定一个尚不存在的
新文件；探针不会追加或覆盖已有日志。

## 托盘、快捷键与 Windows 自动化

托盘图标、右键菜单、可信设备发现、已验证控制和实体控制器复读已经实现。快捷键和
Windows 自动化尚未实现。计划默认快捷键如下：

| 操作 | 计划默认值 |
| --- | --- |
| 切换主灯 | `Ctrl+Alt+L` |
| 切换氛围灯 | `Ctrl+Alt+A` |
| 主灯亮度增/减 | `Ctrl+Alt+Up` / `Ctrl+Alt+Down` |
| 主灯色温增/减 | `Ctrl+Alt+Right` / `Ctrl+Alt+Left` |

快捷键将支持修改；注册失败不会让应用退出。生命周期自动化将逐项启用并进行防抖。
普通界面不会把 `lamp15` 显示成设备名称。

## 从源码构建

在仓库根目录打开 PowerShell：

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\scripts\restore.ps1 -Locked
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\scripts\verify.ps1 -SkipRestore
```

锁定还原使用已提交的 `packages.lock.json`。验证脚本检查格式和分析器、执行 Release
构建、运行全部测试项目并生成 TRX/Cobertura 结果，同时审计存在漏洞的依赖。项目使用
.NET 10 LTS，主要目标为 Windows x64。构建和测试不需要任何私有文件、真实设备 IP、
账号凭据或云端 Token。

开发阶段可用以下命令运行当前托盘应用：

```powershell
.\.dotnet\dotnet.exe run --project .\src\LibraTray.App\LibraTray.App.csproj -c Release -- --show
```

不传入 `--show` 时，应用会以托盘优先方式启动，窗口默认隐藏。

## 故障排除

如果发现不到设备，请检查局域网控制是否可用、Wi-Fi 客户端隔离、组播转发、
Windows“专用网络”配置以及本地防火墙规则。存在 VPN、隧道或虚拟网卡时，可用
`--local-address <电脑局域网IPv4>` 将 discovery 绑定到物理局域网地址。只有在设备
地址已知且可信时才手动填写设备 IP。
Yeelight 公开协议限制同时 TCP 连接数量和命令速率，诊断时应关闭其他局域网客户端。

详见[故障排除](docs/troubleshooting.md)。切勿公开未脱敏的诊断日志。

## 隐私与安全

LibraTray 设计为直接局域网通信，不要求米家账号、Yeelight 账号、密码、设备 Token
或云端登录。Yeelight 局域网协议可能是明文，因此只能在可信网络中使用。项目不提供
本地 Web 服务，也不监听公网端口。

诊断数据仍可能包含设备 ID、固件、IP 地址、主机名或文件路径，分享前必须脱敏。
详见[隐私与安全](docs/privacy-and-security.md)和 [SECURITY.md](SECURITY.md)。

## 路线图

- **v0.1.0：**协议探测工具；
- **v0.2.0：**经实机验证的基础双通道控制；
- **v0.3.0：**托盘、快速面板和快捷键；
- **v0.4.0：**状态协调与自动重连；
- **v0.5.0：**Windows 生命周期自动化；
- **v1.0.0：**稳定公开版。

以上是计划，不代表已有 Release，也不承诺具体日期。

## 参与贡献

请先阅读 [CONTRIBUTING.md](CONTRIBUTING.md)。设备行为报告应包含固件版本、精确步骤、
预期/实际结果以及**已脱敏**的探测日志。不要提交厂商账号、Token、私有网络标识、
闭源反编译代码或许可证不兼容的代码。

## 许可证与第三方致谢

LibraTray 采用 [Apache License 2.0](LICENSE)。选择过程见
[ADR 0002](docs/adr/0002-license.md)。洁净室协议调研中查看过的公开项目列在
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)；本项目没有复制它们的源代码。

## 非官方项目免责声明

**This project is an independent, unofficial open-source project and is not
affiliated with, endorsed by, or sponsored by Yeelight or Xiaomi.**

本项目是独立的非官方开源项目，与 Yeelight 或小米没有隶属、认可、赞助关系。
Yeelight、Xiaomi、米家及相关产品名称是各自权利人的商标，仅用于说明兼容性。
