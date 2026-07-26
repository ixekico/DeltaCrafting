using DeltaCrafter.Core.L0;
using DeltaCrafter.Core.L1;

namespace DeltaCrafter.Core.L3;

/// <summary>
/// 面向应用层的更新入口。L1 负责网络、文件和进程能力，
/// 本类保持 UI 只依赖 L3/L0，不直接穿透到积木层。
/// </summary>
public sealed class UpdateCoordinator
{
    private readonly UpdateBrick _brick = new();

    public Task<UpdateInfo> CheckLatestAsync(CancellationToken ct) =>
        _brick.CheckLatestAsync(ct);

    public Task<string> DownloadVerifiedSetupAsync(
        UpdateInfo info,
        string targetDir,
        IProgress<double>? progress,
        CancellationToken ct) =>
        _brick.DownloadVerifiedSetupAsync(info, targetDir, progress, ct);

    public void LaunchInstaller(string setupPath) =>
        _brick.LaunchInstaller(setupPath);
}
