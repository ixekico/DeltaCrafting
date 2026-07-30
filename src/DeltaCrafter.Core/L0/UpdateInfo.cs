namespace DeltaCrafter.Core.L0;

/// <summary>
/// 一次正式版更新检查的结论。IsNewer=true 时安装包字段必然有值，
/// 该不变量由 Release 解析阶段强制保证。
/// </summary>
public sealed record UpdateInfo(
    Version Current,
    Version Latest,
    string TagName,
    bool IsNewer,
    string ReleaseNotes,
    string? SetupName,
    string? SetupUrl,
    string? ChecksumUrl,
    long SetupBytes);
