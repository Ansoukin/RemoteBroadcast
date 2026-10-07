namespace ClassFabric.RemoteBroadcast.Abstractions;

/// <summary>
/// 契约类：教师目录提供方。由「手里有教师名单」的插件实现并注册到宿主 DI，
/// 远程广播只负责消费——本地宿主本身没有教师账户目录（教师名只是挂在课表上的一个字符串），
/// 所以名单来源必须做成插件间契约，谁有能力谁提供。
/// </summary>
/// <remarks>
/// 实现方注册示例（插件 Initialize 里）：
/// <code>services.AddSingleton&lt;ITeachersDirectoryProvider, MyDirectoryProvider&gt;();</code>
/// 没有提供方时，远程广播回退到设置页手动维护的接洽人名单。
/// </remarks>
public interface ITeachersDirectoryProvider
{
    /// <summary>目录名称，用于在界面上标明名单来源（如「云端账户」「教研组名册」）。</summary>
    string DisplayName { get; }

    /// <summary>取教师目录。实现方应保证不抛异常：拿不到名单就返回空列表并在来源里说明原因。</summary>
    Task<TeachersDirectoryResult> GetTeachersAsync(CancellationToken cancellationToken = default);
}

/// <summary>一位教师。</summary>
public sealed record TeacherInfo
{
    /// <summary>姓名。</summary>
    public required string Name { get; init; }

    /// <summary>分组/部门，可选。</summary>
    public string? Group { get; init; }
}

/// <summary>教师目录查询结果。</summary>
public sealed record TeachersDirectoryResult
{
    /// <summary>教师名单。</summary>
    public IReadOnlyList<TeacherInfo> Teachers { get; init; } = [];

    /// <summary>名单来源说明（无提供方时应写清「未接入目录」之类的原因）。</summary>
    public string Source { get; init; } = "";
}
