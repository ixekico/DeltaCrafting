# DeltaCrafter · 三角洲特勤助手

DeltaCrafter 是一款面向 Windows 的本地桌面工具，用于按照计划管理《三角洲行动》国服特勤处的制造循环。

它会启动游戏、进入特勤处、观察四个制造设施、领取完成品、按计划续造，并依据游戏画面中的倒计时安排下一轮。整个过程只使用屏幕截图、Windows 简体中文 OCR 与系统模拟输入，不读取或调用游戏内部接口。

> [!CAUTION]
> 本项目不是腾讯、琳琅天上或《三角洲行动》的官方产品，也未获得其认可。自动化工具可能受到游戏协议或运营规则限制。使用前请自行确认适用规则，并自行承担账号与资产风险。

## 当前状态

当前版本为 `0.3.0`，核心链路已在 2560×1440、16:9 无边框窗口下完成实机验证。

- 启动器到游戏大厅、特勤处导航
- 四设施多遍共识识别
- 完成品领取与计划续造
- 材料不足时的一键补齐
- 游戏内倒计时驱动的自动调度
- 制造取消、失败退避、托盘与防睡眠
- 制造模式：自定义手选，或按 kkrb.net 利润推荐自动填充（每 2 小时更新）
- 深色、浅色与跟随系统主题
- 启动自动检查更新，确认后自动下载校验并覆盖安装

预启动、近时间任务衔接和长时间自动循环已实现；发布前后的验证状态以 [CHANGELOG.md](CHANGELOG.md) 为准。

## 安装要求

- Windows 10 版本 2004（内部版本 19041）或更高；推荐 Windows 11
- x64 处理器
- Windows 简体中文 OCR 组件
- 游戏使用 16:9 无边框窗口，例如 1920×1080 或 2560×1440
- 管理员权限运行

安装简体中文 OCR：Windows 设置 → 时间和语言 → 语言和区域 → 中文（简体）→ 语言选项 → 安装“光学字符识别”。

## 下载与启动

方式一（推荐）：安装包

1. 在 GitHub Releases 下载 `DeltaCrafter-Setup-<版本>.exe` 及对应 `.sha256` 文件并校验。
2. 运行安装程序（需要管理员权限），可选择创建桌面快捷方式，装完即可启动。
3. 卸载入口在 Windows「设置 → 应用」：卸载会先停止程序并清除「开机自启」计划任务，
   然后询问是否一并删除本机数据（制造计划、设置、日志与诊断截图；默认保留）。

静默卸载始终保留本机数据且不会弹出自定义确认框：

```powershell
& "$env:ProgramFiles\DeltaCrafter\unins000.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
```

方式二：免安装压缩包

1. 下载 `DeltaCrafter-win-x64-<版本>.zip` 及对应 `.sha256` 文件。
2. 校验压缩包 SHA-256，确认下载未损坏或被替换：

   ```powershell
   Get-FileHash .\DeltaCrafter-win-x64-0.3.0.zip -Algorithm SHA256
   ```

3. 解压到普通可写目录，不要直接在压缩包内运行。
4. 启动 `DeltaCrafter.exe`，按系统提示授予管理员权限。

应用暂未进行商业代码签名。Windows SmartScreen 首次运行时可能显示未知发布者；请只从本仓库 Releases 下载，并核对校验值。

## 首次配置

1. 在“设置”中选择游戏启动器或游戏可执行文件。
2. 先手动打开游戏，通过“定位窗口”绑定正确的标题与窗口类名。
3. 在“制造计划”中启用设施并选择物品。
4. 用“识别当前任务”接管游戏中已经进行的制造。
5. 首次自动执行前建议开启开发者模式，按“启动到大厅 → 进入特勤处 → 识别画面”逐段检查。

默认锚点按 2560×1440 标定并使用归一化坐标。游戏界面更新或点击位置偏移时，请参照 [构建与校准指南](docs/构建与校准指南.md) 调整。

## 更新

程序每次启动会自动检查一次更新，也可在「设置 → 关于」右侧点「检查更新」。发现新版本时会弹窗，确认后从 GitHub Releases 下载官方安装包、校验 SHA-256，通过后静默覆盖安装并自动重启。校验不通过会删除下载文件并报错，不会安装未通过校验的包。

制造计划、正在计时的制造进度与所有设置都保存在 `%LocalAppData%\DeltaCrafter`，覆盖安装只更新程序文件，不会清除这些数据；下载与安装期间会暂停自动调度，不会打断正在进行的制造。若窗口正驻留托盘，发现新版会改为系统通知提醒，打开窗口后再更新。

## 安全设计

DeltaCrafter 把“不要误点”放在“尽量继续运行”之前：

- 每次关键点击后重新截图，确认已经到达预期界面。
- 单次 OCR 不直接定案；同一设施需要多遍结果形成共识。
- 解析不出倒计时不等于制造完成。
- 校验失败只允许一次明确重试，之后中止本轮并保留现场。
- 配置损坏、OCR 缺失、材料不足等情况会明确显示，不静默改写或跳过。

日志、设置、计划、状态与失败截图只保存在：

```text
%LocalAppData%\DeltaCrafter
```

程序运行时不上传这些数据，也不包含遥测。提交问题前请检查截图和日志是否包含账号昵称、聊天内容或其他个人信息。

## 从源码构建

需要 .NET 8 SDK 和支持 WinUI 3 的 Windows 开发环境：

```powershell
dotnet restore DeltaCrafter.sln --configfile nuget.config
dotnet build DeltaCrafter.sln -c Release -p:Platform=x64 --no-restore
dotnet test tests\DeltaCrafter.Core.Tests\DeltaCrafter.Core.Tests.csproj -c Release -p:Platform=x64 --no-build --no-restore
```

生成与 GitHub Releases 相同结构的发布包：

```powershell
.\scripts\build-release.ps1 -Version 0.3.0
.\scripts\install-inno-setup.ps1
.\scripts\build-installer.ps1 -Version 0.3.0 -SkipBuild
```

安装包构建固定使用官方签名的 Inno Setup 6.7.3，并校验编译器、简体中文语言文件及复用
payload 的版本。任何输入缺失或版本不一致都会中止，不会生成降级安装包。

## 架构

核心依赖保持单向：

```text
L3 编排层 → L2 流程层 → L1 能力组件 → L0 领域模型
UI 只消费 L3 与 L0
```

参与开发前请阅读 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 已知限制

- 目前只对 2560×1440 做过完整实机验证；其他 16:9 分辨率仍需扩大验证样本。
- 游戏 UI、字体或 OCR 行为变化可能要求更新 `anchors.json` 或最小化文字折叠规则。
- 电脑必须处于唤醒状态；防睡眠功能不能替代关机或休眠后的唤醒任务。
- 当前仅发布 Windows x64；Setup 与免安装压缩包均未进行商业代码签名。

## 第三方组件

直接依赖与许可证见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

## 许可证

当前仓库默认保留全部权利，详见 [LICENSE](LICENSE)。
