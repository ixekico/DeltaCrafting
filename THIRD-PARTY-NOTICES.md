# 第三方组件声明

DeltaCrafter 直接使用以下 NuGet 软件包。其版权与许可证归各自权利人所有。

| 组件 | 版本 | 许可证 | 项目 |
|---|---:|---|---|
| Microsoft.WindowsAppSDK | 1.7.250606001 | Microsoft Windows App SDK License Terms | <https://github.com/microsoft/WindowsAppSDK> |
| CommunityToolkit.Mvvm | 8.4.0 | MIT | <https://github.com/CommunityToolkit/dotnet> |
| H.NotifyIcon.WinUI | 2.3.0 | MIT | <https://github.com/HavenDV/H.NotifyIcon> |
| Serilog | 4.2.0 | Apache-2.0 | <https://github.com/serilog/serilog> |
| Serilog.Sinks.File | 6.0.0 | Apache-2.0 | <https://github.com/serilog/serilog-sinks-file> |
| System.Text.Encoding.CodePages | 8.0.0 | MIT | <https://github.com/dotnet/runtime> |

测试工程还使用：

| 组件 | 版本 | 许可证 |
|---|---:|---|
| Microsoft.NET.Test.Sdk | 17.11.1 | MIT |
| xunit | 2.9.2 | Apache-2.0 |
| xunit.runner.visualstudio | 2.8.2 | Apache-2.0 |

发布工程使用 Inno Setup 6.7.3 编译安装包。仓库内的
`installer/Languages/ChineseSimplified.isl` 来自 Inno Setup 官方源码仓库
`is-6_7_3` 标签，文件头保留翻译维护者 Zhenghan Yang（Kira）的声明；来源、固定校验值
与分发条款链接见 `installer/Languages/README.md`。Inno Setup 编译器不随应用发布。

自包含发布目录中会包含 Windows App SDK 运行组件及其上游依赖。对应的许可证正文和上游声明位于发布包的 `licenses/` 目录；本文件不替代这些正文。
