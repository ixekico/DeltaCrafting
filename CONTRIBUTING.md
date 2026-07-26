# 参与贡献

感谢你帮助改进 DeltaCrafter。这个项目会直接模拟用户输入，因此“明确失败”比“勉强继续”更重要。

## 开始之前

1. 阅读 [README.md](README.md)、[CLAUDE.md](CLAUDE.md) 和 [构建与校准指南](docs/构建与校准指南.md)。
2. 使用 Windows、.NET 8 SDK 与 Release/x64 配置构建。
3. 不要提交 `%LocalAppData%\DeltaCrafter` 下的设置、日志、状态或截图。

## 架构约束

- 依赖方向只能是 `L3 → L2 → L1 → L0`。
- L0 保存纯模型和不变量；L1 提供单一系统能力；L2 组织一个流程；L3 编排跨流程行为。
- UI 只消费 L3 与 L0，不跨层调用底层组件。
- 不为通过测试加入吞错、默认成功、静默跳过或兜底续跑。
- 关键点击必须有基于新截帧的后置校验。
- 所有 UI 角部必须使用连续平滑曲线，不允许尖锐角。

## 修改 OCR 或匹配规则

只根据可复现的日志和失败截图增加最小规则：

1. 保存原始 OCR 文本及对应画面。
2. 证明现有规则为什么无法表达该读法。
3. 检查新折叠是否会误伤目录内其他物品。
4. 为纯函数规则增加回归测试。

不要通过放宽全局距离阈值解决单个 OCR 误读。

## 提交前验证

```powershell
dotnet restore DeltaCrafter.sln --configfile nuget.config
dotnet build DeltaCrafter.sln -c Release -p:Platform=x64 --no-restore
dotnet test tests\DeltaCrafter.Core.Tests\DeltaCrafter.Core.Tests.csproj -c Release -p:Platform=x64 --no-build --no-restore
```

涉及 XAML 时还需检查深浅主题、100%/125%/150% DPI、窗口缩放及所有角部。
涉及发布工程时，运行 `scripts/build-installer.ps1`，并完成静默安装、覆盖升级、保留数据卸载
与清除数据卸载检查；不得用英文界面、旧 payload 或忽略退出码的方式伪造成功。

## 文档

- 新需求追加到 `任务进展/需求历史/`。
- 完成记录追加到当前 `任务进展-XXX.md`，每页不超过 100 行。
- 功能条目使用对应代码文件名作为稳定键。
- 历史页不重写；事实变化用新条目更正。

## Pull Request

PR 请说明：

- 用户可见变化与不变量
- 根因与证据
- 验证命令及结果
- 是否修改锚点、OCR 折叠、输入位置或管理员权限行为
- 是否包含个人数据或第三方素材
