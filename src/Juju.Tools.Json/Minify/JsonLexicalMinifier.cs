using System.Text;
using System.Text.Json;
using Juju.Core.Errors;

namespace Juju.Tools.Json.Minify;

/// <summary>
/// 保留 JSON token 原样拼写的词法压缩器：只删除字符串外的空白，不会重新排列属性、改变数字
/// 表示或转义方式。先用解析器验证输入，避免把无效文本误当 JSON 输出。
/// </summary>
public static class JsonLexicalMinifier
{
    /// <summary>
    /// 验证后用有限状态机扫描字符。<c>inString</c> 和 <c>escaped</c> 区分字符串内容、反斜杠
    /// 转义和结构性引号；解析异常包装为可供 UI 识别的 <see cref="JujuException"/>。
    /// </summary>
    public static string Minify(string content)
    {
        try
        {
            using var _ = JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            throw new JujuException(ErrorCode.InvalidJson, "Only valid JSON can be minified.", ex);
        }

        var output = new StringBuilder(content.Length);
        var inString = false;
        var escaped = false;
        foreach (var character in content)
        {
            if (inString)
            {
                output.Append(character);
                if (escaped) escaped = false;
                else if (character == '\\') escaped = true;
                else if (character == '"') inString = false;
            }
            else if (character == '"')
            {
                inString = true;
                output.Append(character);
            }
            else if (!char.IsWhiteSpace(character)) output.Append(character);
        }

        return output.ToString();
    }
}