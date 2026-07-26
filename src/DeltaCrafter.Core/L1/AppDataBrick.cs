namespace DeltaCrafter.Core.L1;

/// <summary>
/// 应用数据目录规划(%LocalAppData%\DeltaCrafter)。可变数据一律在此,
/// 构建输出目录(Data\)只放默认模板,首次运行复制过来后即以本目录为准。
/// 默认模板缺失说明构建产物不完整,直接抛错终止启动。
/// </summary>
public sealed class AppDataBrick
{
    public string Root { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeltaCrafter");

    public string SettingsPath => Path.Combine(Root, "settings.json");
    public string PlanPath => Path.Combine(Root, "plan.json");
    public string StatePath => Path.Combine(Root, "state.json");
    public string AnchorsPath => Path.Combine(Root, "anchors.json");
    public string ItemsPath => Path.Combine(Root, "items.json");
    public string LogsDir => Path.Combine(Root, "logs");
    public string ShotsDir => Path.Combine(Root, "shots");

    /// <param name="defaultsDir">构建输出中的默认数据目录(exe 旁的 Data\)。</param>
    public void EnsureInitialized(string defaultsDir)
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(ShotsDir);
        CopyDefaultIfMissing(defaultsDir, "anchors.json", AnchorsPath);
        CopyDefaultIfMissing(defaultsDir, "items.json", ItemsPath);
    }

    private static void CopyDefaultIfMissing(string defaultsDir, string fileName, string targetPath)
    {
        if (File.Exists(targetPath)) return;
        string source = Path.Combine(defaultsDir, fileName);
        if (!File.Exists(source))
            throw new FileNotFoundException(
                $"默认数据文件缺失:{source}。构建产物不完整,请重新生成解决方案。", source);
        File.Copy(source, targetPath);
    }
}
