using System.Text.RegularExpressions;

namespace ClassFabric.RemoteBroadcast.Services;

/// <summary>播报构造失败（映射 HTTP 400 可读错误）。</summary>
public class AnnouncementException(string message) : Exception(message);

/// <summary>播报文本与视觉文本。</summary>
public sealed record AnnouncementResult(string VisualText, string SpeechText);

/// <summary>
/// 4.3 播报序列机制：单/多学生统一「段序列循环」模型——
/// 单学生 = 单句模板；多学生 = 引导句 + 名单段（「、」连接）；整体循环 N 次（1–5），
/// 语音一次性合成（句号天然分句停顿），Overlay 只显示一次完整循环文本（不堆叠）。
/// </summary>
public static partial class AnnouncementComposer
{
    private const int MaxSpeechLength = 1000;

    private static readonly string[] AllowedPlaceholders = ["Students", "Destination", "Caller", "Contact"];

    [GeneratedRegex(@"\{([^}]*)\}")]
    private static partial Regex PlaceholderRegex();

    /// <summary>
    /// 接洽人语义拆分。{Caller} 是「发起人」（固定为登记本人，Mask 报「{Caller} 正在呼叫」），
    /// {Contact} 是「接洽人」（正文里的「找谁」，网页端三段选择：自己/目录老师/不指定）。
    /// 老配置的 {Caller} 句式照常工作（向后兼容），新默认模板改用 {Contact}。
    /// </summary>
    public static AnnouncementResult Compose(
        IReadOnlyList<string> studentNames, string destination, string caller, string? contact,
        string templateSingle, string templateMultiGuide, int loopCount)
    => Compose(studentNames, destination, caller, contact,
        templateSingle, templateMultiGuide, null, null, loopCount);

    /// <summary>三段选择配套：contact 为空（不指定）时自动切到无接洽人模板对。</summary>
    public static AnnouncementResult Compose(
        IReadOnlyList<string> studentNames, string destination, string caller, string? contact,
        string templateSingle, string templateMultiGuide,
        string? templateSingleNoContact, string? templateMultiGuideNoContact, int loopCount)
    {
        // 三态语义（与 HTTP 请求体一一对应）：
        //   非空 = 明确指定接洽人 → 主模板对，{Contact} 填该人；
        //   空串 = 明确选「不指定」→ 无接洽人模板对；
        //   null = 调用方没这个概念（老版网页/插件 API 缺省）→ 主模板对、{Contact} 填发起人，升级前行为原样。
        // NoContact 对为空时回退主模板对——老配置没这两项，行为照旧。
        var trimmedContact = contact?.Trim();
        var hasContact = !string.IsNullOrEmpty(trimmedContact);
        var useNoContactPair = contact != null && !hasContact;
        ValidateTemplate(templateSingle, nameof(templateSingle));
        ValidateTemplate(templateMultiGuide, nameof(templateMultiGuide));
        if (useNoContactPair)
        {
            if (!string.IsNullOrEmpty(templateSingleNoContact))
            {
                ValidateTemplate(templateSingleNoContact, nameof(templateSingleNoContact));
            }

            if (!string.IsNullOrEmpty(templateMultiGuideNoContact))
            {
                ValidateTemplate(templateMultiGuideNoContact, nameof(templateMultiGuideNoContact));
            }
        }

        loopCount = Math.Clamp(loopCount, 1, 5);
        // 名单连接符用「、」不是随便选的：EdgeTTS 对顿号有自然停顿，正好拿来当名字间隔（任务书 4.3 定稿）。
        var roster = string.Join("、", studentNames);
        var contactText = hasContact ? trimmedContact! : caller;

        var singleCycle = studentNames.Count == 1
            ? Fill(ResolveTemplate(hasContact, useNoContactPair, templateSingle, templateSingleNoContact),
                roster, destination, caller, contactText)
            : Fill(ResolveTemplate(hasContact, useNoContactPair, templateMultiGuide, templateMultiGuideNoContact),
                roster, destination, caller, contactText) + roster + "。";
        var visual = singleCycle;

        // 语音文本：段序列整体重复 N 次，段间以句号自然分隔
        var speech = string.Concat(Enumerable.Repeat(singleCycle, loopCount));

        if (speech.Length > MaxSpeechLength)
        {
            // 500 人名单 × 5 次循环的极端场景护栏——真到这一步让手机端提示减人，比让 EdgeTTS 合成一段十分钟的长文强。
            throw new AnnouncementException(
                $"播报文本超出 {MaxSpeechLength} 字符上限（当前 {speech.Length} 字符），请减少学生数或循环次数。");
        }

        return new AnnouncementResult(visual, speech);
    }

    /// <summary>
    /// 自定义内容播报：正文本身就是视觉文本，语音把同一段正文循环 N 次。
    /// 与学生呼叫共用 1000 字符总护栏——正文 120 字 × 5 轮绰绰有余，真超限让手机端提示减轮次，
    /// 比让合成器憋一段几分钟的长音频强。
    /// </summary>
    public static AnnouncementResult ComposeCustom(string body, int loopCount)
    {
        var text = body.Trim();
        if (text.Length is < 2 or > 120)
        {
            throw new AnnouncementException("通知正文需为 2–120 个字符");
        }

        loopCount = Math.Clamp(loopCount, 1, 5);

        // 语音层面补个句读：正文没带句尾标点就补个句号，循环时 EdgeTTS 才有自然的停顿断句，
        // 不然两轮连读会黏成一句令人窒息的绕口令。
        var singleCycle = text.EndsWith('。') || text.EndsWith('！') || text.EndsWith('？') || text.EndsWith('…')
            ? text
            : text + "。";
        var speech = string.Concat(Enumerable.Repeat(singleCycle, loopCount));

        if (speech.Length > MaxSpeechLength)
        {
            throw new AnnouncementException(
                $"播报文本超出 {MaxSpeechLength} 字符上限（当前 {speech.Length} 字符），请减少字数或循环次数。");
        }

        return new AnnouncementResult(text, speech);
    }

    /// <summary>校验模板：占位符仅限 {Students}/{Destination}/{Caller}/{Contact}，出现其他 {…} 视为非法。</summary>
    public static void ValidateTemplate(string template, string fieldName)
    {
        foreach (var m in PlaceholderRegex().Matches(template))
        {
            if (m is Match match && !AllowedPlaceholders.Contains(match.Groups[1].Value))
            {
                throw new AnnouncementException(
                    $"模板 {fieldName} 含非法占位符 {{{match.Groups[1].Value}}}（仅允许 {{Students}}/{{Destination}}/{{Caller}}/{{Contact}}）。");
            }
        }
    }

    /// <summary>按接洽人三态选模板；仅「明确不指定」且 NoContact 变体存在时才切变体，其余一律主模板。</summary>
    private static string ResolveTemplate(bool hasContact, bool useNoContactPair, string primary, string? noContactVariant)
    {
        return !hasContact && useNoContactPair && !string.IsNullOrEmpty(noContactVariant)
            ? noContactVariant
            : primary;
    }

    private static string Fill(string template, string students, string destination, string caller)
    {
        return Fill(template, students, destination, caller, caller);
    }

    private static string Fill(string template, string students, string destination, string caller, string contact)
    {
        return template
            .Replace("{Students}", students)
            .Replace("{Destination}", destination)
            .Replace("{Caller}", caller)
            .Replace("{Contact}", contact);
    }

    /// <summary>语音提示模板只含 {Caller} 一个占位符，走同一个校验器。</summary>
    public static string FillHint(string template, string caller)
    {
        ValidateTemplate(template, "语音提示模板");
        return template.Replace("{Caller}", caller);
    }
}
