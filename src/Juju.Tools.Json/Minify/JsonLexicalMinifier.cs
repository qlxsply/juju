using System.Text;
using System.Text.Json;
using Juju.Core.Errors;

namespace Juju.Tools.Json.Minify;

public sealed class JsonLexicalMinifier
{
    public string Minify(string content)
    {
        try { using var _ = JsonDocument.Parse(content); }
        catch (JsonException ex) { throw new JujuException(ErrorCode.InvalidJson, "Only valid JSON can be minified.", ex); }

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
            else if (character == '"') { inString = true; output.Append(character); }
            else if (!char.IsWhiteSpace(character)) output.Append(character);
        }
        return output.ToString();
    }
}
