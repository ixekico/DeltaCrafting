using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeltaCrafter.Core.L1;

/// <summary>
/// JSON 配置读写。写入走"临时文件 + 原子替换",避免断电/崩溃留下半截文件。
/// 读取失败(损坏/为空)抛 InvalidDataException 并附路径与修复建议——
/// 损坏的配置绝不静默重置,那会无声丢掉用户的计划与校准数据。
/// LoadOrCreate 仅在"文件不存在"时落默认值:这是首次初始化,不是错误兜底。
/// </summary>
public sealed class JsonStoreBrick
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 中文按原文输出,便于手工校准编辑
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public T Load<T>(string path)
    {
        try
        {
            var value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
            return value is not null ? value
                : throw new InvalidDataException("反序列化结果为 null");
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            throw new InvalidDataException(
                $"配置文件损坏:{path}\n请手工修复;或删除该文件后重启程序以重置为默认值。\n原因:{ex.Message}", ex);
        }
    }

    public T LoadOrCreate<T>(string path, Func<T> createDefault)
    {
        if (File.Exists(path)) return Load<T>(path);
        var created = createDefault();
        Save(path, created);
        return created;
    }

    public void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }
}
