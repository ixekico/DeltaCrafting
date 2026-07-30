<div align="center">
  <img src="src/DeltaCrafter.App/Assets/AppIcon.png" alt="DeltaCrafter 图标" width="128" height="128">

  <h1>DeltaCrafter</h1>

  <p>三角洲特勤助手：面向《三角洲行动》国服特勤处的 Windows 本地制造计划与自动循环工具。</p>

  <p>
    <a href="https://github.com/ixekico/DeltaCrafting/releases/latest">
      <img src="https://img.shields.io/github/v/release/ixekico/DeltaCrafting?display_name=tag&sort=semver" alt="最新版本">
    </a>
    <a href="https://github.com/ixekico/DeltaCrafting/actions/workflows/ci.yml">
      <img src="https://github.com/ixekico/DeltaCrafting/actions/workflows/ci.yml/badge.svg" alt="持续集成">
    </a>
    <a href="LICENSE">
      <img src="https://img.shields.io/badge/license-MIT-yellow.svg" alt="MIT 许可证">
    </a>
    <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4?logo=windows11" alt="支持 Windows 10 和 Windows 11">
  </p>

  <p>
    <a href="https://github.com/ixekico/DeltaCrafting/releases/latest"><strong>下载最新版</strong></a>
    ·
    <a href="https://github.com/ixekico/DeltaCrafting/issues/new?template=bug_report.yml">报告问题</a>
    ·
    <a href="https://github.com/ixekico/DeltaCrafting/issues/new?template=feature_request.yml">功能建议</a>
    ·
    <a href="CHANGELOG.md">更新日志</a>
  </p>
</div>

> [!CAUTION]
> DeltaCrafter 不是腾讯、琳琅天上或《三角洲行动》的官方产品，也未获得其认可。自动化工具可能受到游戏协议或运营规则限制。使用前请自行确认适用规则，并自行承担账号与资产风险。

> [!WARNING]
> **免责声明：**本软件开源免费，仅供学习交流，**请勿用于非法用途！** 作者不对使用本软件产生的任何后果负责。

## 目录

- [项目简介](#项目简介)
- [功能特性](#功能特性)
- [运行要求](#运行要求)
- [下载与安装](#下载与安装)
- [使用方法](#使用方法)
- [自动更新](#自动更新)
- [安全与隐私](#安全与隐私)
- [已知限制](#已知限制)
- [从源码构建](#从源码构建)
- [项目架构](#项目架构)
- [参与贡献](#参与贡献)
- [许可证](#许可证)
- [致谢](#致谢)

## 项目简介

DeltaCrafter 用于按照预先设定的计划管理《三角洲行动》国服特勤处制造循环。它可以启动游戏、进入特勤处、检查四个制造设施、领取完成品、按计划续造，并根据游戏画面中的倒计时安排下一轮执行。

整个过程只使用屏幕截图、Windows 简体中文 OCR 和系统模拟输入，不读取游戏内存，也不调用游戏内部接口。设置、制造计划、运行状态、日志和失败截图均保存在本机。

当前开发版本为 `0.4.0`。2560×1440、16:9 无边框窗口下的核心链路已经完成实机验证；1920×1080 已完成关键页面与状态的 OCR、锚点回放验证。详细变更与验证结果请查看 [CHANGELOG.md](CHANGELOG.md)。

## 功能特性

- **制造循环：**自动进入特勤处、识别四个设施、领取完成品并按计划续造。
- **独立制造模式：**每个设施都能分别设置为自定义物品、每小时利润最高或总利润最高。
- **行情推荐：**利润数据来自 kkrb.net；应用启动后预热，并在每个整点更新，不受设施是否启用影响。缓存为空时切换到利润模式会立即获取。
- **自定义记忆：**利润模式只改变当前推荐；切回自定义模式会恢复该设施最后一次手动选择的物品。
- **材料补齐：**材料不足时可执行游戏内“一键补齐”；仓库空间不足时会停止当前流程并提醒用户清理仓库。
- **可靠识别：**关键状态使用新截图验证，同一设施需要多次 OCR 结果形成共识，无法读取倒计时不会被误判为制造完成。
- **自动调度：**依据游戏内倒计时安排下一次执行，支持取消、失败退避、防睡眠和托盘运行。
- **自动更新：**启动时检查 GitHub Releases，在更新窗口展示新版日志，校验 SHA-256 后执行覆盖安装。
- **桌面体验：**支持深色、浅色和跟随系统主题，窗口标题直接显示当前版本号。

### 制造模式

| 模式 | 行为 |
| --- | --- |
| 自定义物品 | 使用该设施最后一次手动选择的物品 |
| 每小时利润最高 | 使用当前行情中该设施单位时间利润最高的物品 |
| 总利润最高 | 使用当前行情中该设施单次制造总利润最高的物品 |

制造模式和启用状态均按设施单独保存。行情推荐不会覆盖保存的自定义选择。

## 运行要求

- Windows 10 版本 2004（内部版本 19041）或更高，推荐 Windows 11
- x64 处理器
- Windows 简体中文 OCR 组件
- 16:9 无边框游戏窗口，例如 1920×1080 或 2560×1440
- 管理员权限

Release 中的安装包和免安装压缩包均为自包含版本，普通用户无需另外安装 .NET 运行时。

如系统缺少简体中文 OCR，请前往：

> Windows 设置 → 时间和语言 → 语言和区域 → 中文（简体）→ 语言选项 → 安装“光学字符识别”

## 下载与安装

所有正式版本均发布在 [GitHub Releases](https://github.com/ixekico/DeltaCrafting/releases)。请只从本仓库下载，并同时取得对应的 `.sha256` 文件。

### 安装包（推荐）

1. 下载 `DeltaCrafter-Setup-<版本>.exe` 和 `DeltaCrafter-Setup-<版本>.exe.sha256`。
2. 校验文件的 SHA-256。
3. 运行安装程序并授予管理员权限；可以选择创建桌面快捷方式。
4. 安装完成后直接启动 DeltaCrafter。

卸载入口位于 Windows“设置 → 应用”。卸载程序会先停止 DeltaCrafter，并删除应用创建的“开机自启”计划任务，然后询问是否同时删除本机数据；默认保留制造计划、设置、日志和诊断截图。

静默卸载始终保留本机数据：

```powershell
& "$env:ProgramFiles\DeltaCrafter\unins000.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
```

### 免安装压缩包

1. 下载 `DeltaCrafter-win-x64-<版本>.zip` 和 `DeltaCrafter-win-x64-<版本>.zip.sha256`。
2. 校验文件的 SHA-256。
3. 解压到普通可写目录，不要直接在压缩包中运行。
4. 启动 `DeltaCrafter.exe`，并按系统提示授予管理员权限。

以 `0.4.0` 免安装包为例：

```powershell
Get-FileHash .\DeltaCrafter-win-x64-0.4.0.zip -Algorithm SHA256
Get-Content .\DeltaCrafter-win-x64-0.4.0.zip.sha256
```

两处哈希值应完全一致。应用暂未进行商业代码签名，Windows SmartScreen 首次运行时可能显示“未知发布者”。

## 使用方法

1. 打开“设置”，选择游戏启动器或游戏可执行文件。
2. 手动打开游戏，通过“定位窗口”绑定正确的窗口标题和窗口类名。
3. 在“制造计划”中为四个设施分别选择制造模式、物品和启用状态。
4. 如需接管游戏中已经进行的制造，使用“识别当前任务”读取剩余时间。
5. 首次自动运行前，建议打开开发者模式，依次验证“启动到大厅 → 进入特勤处 → 识别画面”。
6. 确认识别与点击位置正确后，再启动自动循环。

默认锚点按 2560×1440 标定，并使用归一化坐标适配其他 16:9 分辨率。游戏更新后如出现识别失败或点击偏移，请参照 [构建与校准指南](docs/构建与校准指南.md) 检查和调整。

## 自动更新

程序每次启动时自动检查一次更新，也可以在“设置 → 关于”中手动检查。

发现新版本后，更新窗口会先展示该版本的完整更新日志。用户确认后，程序将从 GitHub Releases 下载官方安装包并校验 SHA-256；只有校验通过才会静默覆盖安装并重新启动。校验失败时会删除下载文件并明确报错。

更新过程中会暂停自动调度，但不会清除制造计划、当前计时进度或设置。窗口驻留托盘时，程序会先发送系统通知，等待用户打开窗口后再决定是否更新。

## 安全与隐私

DeltaCrafter 将“避免错误输入”置于“尽量继续运行”之前：

- 每次关键点击后重新截图，确认已经进入预期界面。
- 单次 OCR 不直接定案，同一设施需要多次结果形成共识。
- 无法解析倒计时不等同于制造完成。
- 校验失败只进行一次明确重试，随后中止本轮并保留现场。
- 配置损坏、OCR 缺失、材料不足或仓库已满都会明确提示，不会静默跳过。

所有用户数据均保存在：

```text
%LocalAppData%\DeltaCrafter
```

程序运行时不会上传这些数据，也不包含遥测。提交问题前，请检查日志和失败截图是否包含账号昵称、聊天内容、Windows 用户名或其他个人信息。安全问题请按照 [SECURITY.md](SECURITY.md) 私下报告。

## 已知限制

- 2560×1440 已完成端到端实机验证；1920×1080 已使用 14 个关键页面和状态完成 OCR 与锚点回放，其中包含游戏客户区 1919×1080 的一像素偏差。其他 16:9 分辨率仍需更多样本。
- 游戏 UI、字体或 OCR 行为变化后，可能需要更新 `anchors.json` 或文字最小化折叠规则。
- 电脑必须保持唤醒；防睡眠功能不能代替关机或休眠后的唤醒任务。
- 当前只发布 Windows x64 版本。
- Setup 和免安装压缩包均未进行商业代码签名。

## 从源码构建

### 开发环境

- Windows 10/11 x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- 支持 WinUI 3 的 Windows 开发环境
- [Inno Setup 6](https://jrsoftware.org/isinfo.php)（仅构建安装包时需要）

### 构建与测试

```powershell
dotnet restore DeltaCrafter.sln --configfile nuget.config
dotnet build DeltaCrafter.sln -c Release -p:Platform=x64 --no-restore
dotnet test tests\DeltaCrafter.Core.Tests\DeltaCrafter.Core.Tests.csproj -c Release -p:Platform=x64 --no-build --no-restore
```

### 生成发布文件

```powershell
.\scripts\build-release.ps1 -Version 0.4.0
.\scripts\install-inno-setup.ps1
.\scripts\build-installer.ps1 -Version 0.4.0 -SkipBuild
```

安装包构建固定使用官方签名的 Inno Setup 6.7.3，并校验编译器、简体中文语言文件和复用 payload 的版本。任何输入缺失或版本不一致都会中止构建，不会生成降级安装包。

## 项目架构

DeltaCrafter 使用 C#、.NET 8、WinUI 3 和 Windows App SDK 构建。核心依赖保持单向：

```text
L3 编排层 → L2 流程层 → L1 能力组件 → L0 领域模型
UI 只消费 L3 与 L0
```

```text
src/
├─ DeltaCrafter.App/       WinUI 3 界面与桌面集成
└─ DeltaCrafter.Core/
   ├─ L0/                  领域模型与不变量
   ├─ L1/                  OCR、截图、输入、存储等单一能力
   ├─ L2/                  制造与导航流程
   └─ L3/                  自动化、行情与更新编排
tests/                     核心逻辑测试
scripts/                   构建、安装包与发布说明脚本
installer/                 Inno Setup 工程
docs/                      构建与画面校准文档
```

## 参与贡献

欢迎通过 Issue 报告可复现的问题，或提交 Pull Request 改进项目。开始前请先阅读 [CONTRIBUTING.md](CONTRIBUTING.md)，并确保修改符合分层约束及“失败必须明确”的原则。

- [报告 Bug](https://github.com/ixekico/DeltaCrafting/issues/new?template=bug_report.yml)
- [提出功能建议](https://github.com/ixekico/DeltaCrafting/issues/new?template=feature_request.yml)
- [安全问题报告](SECURITY.md)

## 许可证

本项目采用 [MIT 许可证](LICENSE)。你可以使用、复制、修改、合并、发布和分发本软件，但必须保留原版权声明与许可证文本。第三方组件仍分别适用其上游许可证，详见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

## 致谢

- 行情推荐数据来源：[kkrb.net](https://kkrb.net/)
- README 结构参考：[Best-README-Template](https://github.com/othneildrew/Best-README-Template)、[readme-template](https://github.com/iuricode/readme-template) 与 [standard-readme](https://github.com/RichardLitt/standard-readme)
- 直接依赖及其许可证见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)
