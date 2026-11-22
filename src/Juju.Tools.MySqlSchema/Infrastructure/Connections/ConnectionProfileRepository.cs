using System.Text.Json;
using Juju.Core.Storage;
using Juju.Tools.MySqlSchema.Domain;
using Juju.Tools.MySqlSchema.Infrastructure.Security;

namespace Juju.Tools.MySqlSchema.Infrastructure.Connections;

public sealed class ConnectionProfileRepository(string toolDataDirectory, IAtomicFileWriter writer,
    WindowsCredentialProtector protector)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { WriteIndented = true };
    private readonly string _path = Path.Combine(Path.GetFullPath(toolDataDirectory), "connections.json");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<MySqlConnectionProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        { return (await ReadCoreAsync(cancellationToken)).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray(); }
        finally { _gate.Release(); }
    }

    public async Task<MySqlConnectionProfile> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var profile = (await ListAsync(cancellationToken)).SingleOrDefault(item => item.Id == id);
        return profile ?? throw new KeyNotFoundException("连接配置不存在或已删除。");
    }

    public async Task<MySqlConnectionProfile> SaveAsync(ConnectionProfileDraft draft,
        CancellationToken cancellationToken = default)
    {
        Validate(draft);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var profiles = await ReadCoreAsync(cancellationToken);
            var existing = string.IsNullOrWhiteSpace(draft.Id)
                ? null
                : profiles.SingleOrDefault(profile => profile.Id == draft.Id);
            if ( !string.IsNullOrWhiteSpace(draft.Id) && existing is null )
                throw new InvalidOperationException("要编辑的连接已不存在。");
            if ( profiles.Any(profile => profile.Id != existing?.Id &&
                                         string.Equals(profile.Name, draft.Name.Trim(), StringComparison.OrdinalIgnoreCase)) )
                throw new InvalidOperationException("连接名称已存在。");

            var now = DateTimeOffset.UtcNow;
            var protectedPassword = draft.KeepExistingPassword && existing is not null
                ? existing.ProtectedPassword
                : ProtectPassword(draft.Password);
            var saved = new MySqlConnectionProfile(existing?.Id ?? Guid.NewGuid().ToString("N"), draft.Name.Trim(),
                draft.Host.Trim(), draft.Port, draft.UserName.Trim(), protectedPassword,
                string.IsNullOrWhiteSpace(draft.DefaultDatabase) ? null : draft.DefaultDatabase.Trim(),
                draft.SslMode, existing?.CreatedAtUtc ?? now, now);

            if ( existing is null )
                profiles.Add(saved);
            else
                profiles[profiles.FindIndex(profile => profile.Id == existing.Id)] = saved;
            await WriteCoreAsync(profiles, cancellationToken);
            return saved;
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var profiles = await ReadCoreAsync(cancellationToken);
            if ( profiles.RemoveAll(profile => profile.Id == id) == 0 )
                return;
            await WriteCoreAsync(profiles, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public string RevealPassword(MySqlConnectionProfile profile)
    {
        if ( !OperatingSystem.IsWindows() )
            throw new PlatformNotSupportedException("MySQL connection passwords are protected with Windows DPAPI.");
        return protector.Unprotect(profile.ProtectedPassword);
    }

    public async Task<string> ExportJsonAsync(bool includePasswords,
        CancellationToken cancellationToken = default)
    {
        var profiles = await ListAsync(cancellationToken);
        var exported = profiles.Select(profile => new ConnectionProfileExport(profile.Id, profile.Name, profile.Host,
            profile.Port, profile.UserName, includePasswords ? RevealPassword(profile) : null,
            profile.DefaultDatabase, profile.SslMode)).ToArray();
        return JsonSerializer.Serialize(new ConnectionExportDocument(1, DateTimeOffset.UtcNow, includePasswords, exported),
            JsonOptions);
    }

    private async Task<List<MySqlConnectionProfile>> ReadCoreAsync(CancellationToken cancellationToken)
    {
        if ( !File.Exists(_path) )
        {
            var empty = new List<MySqlConnectionProfile>();
            await WriteCoreAsync(empty, cancellationToken);
            return empty;
        }

        var json = await File.ReadAllTextAsync(_path, cancellationToken);
        var profiles = JsonSerializer.Deserialize<List<MySqlConnectionProfile>>(json, JsonOptions) ??
                       throw new InvalidDataException("连接配置文件不是有效的连接列表。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ( var profile in profiles )
        {
            if ( string.IsNullOrWhiteSpace(profile.Id) || !ids.Add(profile.Id) ||
                 string.IsNullOrWhiteSpace(profile.Name) || !names.Add(profile.Name) ||
                 string.IsNullOrWhiteSpace(profile.Host) || profile.Port is 0 or > 65535 ||
                 string.IsNullOrWhiteSpace(profile.UserName) || string.IsNullOrWhiteSpace(profile.ProtectedPassword) )
                throw new InvalidDataException("连接配置包含重复或无效的连接项。");
        }
        var canonical = JsonSerializer.Serialize(profiles, JsonOptions);
        if ( !string.Equals(json, canonical, StringComparison.Ordinal) )
            await writer.WriteTextAsync(_path, canonical, cancellationToken);
        return profiles;
    }

    private Task WriteCoreAsync(IReadOnlyList<MySqlConnectionProfile> profiles, CancellationToken cancellationToken) =>
        writer.WriteTextAsync(_path, JsonSerializer.Serialize(profiles, JsonOptions), cancellationToken);

    private string ProtectPassword(string password)
    {
        if ( !OperatingSystem.IsWindows() )
            throw new PlatformNotSupportedException("MySQL connection passwords are protected with Windows DPAPI.");
        return protector.Protect(password);
    }

    private static void Validate(ConnectionProfileDraft draft)
    {
        if ( string.IsNullOrWhiteSpace(draft.Name) || draft.Name.Trim().Length > 100 )
            throw new InvalidOperationException("连接名称必填，且不能超过 100 个字符。");
        if ( string.IsNullOrWhiteSpace(draft.Host) )
            throw new InvalidOperationException("主机地址必填。");
        if ( draft.Port is 0 or > 65535 )
            throw new InvalidOperationException("端口必须在 1 到 65535 之间。");
        if ( string.IsNullOrWhiteSpace(draft.UserName) )
            throw new InvalidOperationException("用户名必填。");
        if ( !MySqlSslModes.All.Contains(draft.SslMode, StringComparer.Ordinal) )
            throw new InvalidOperationException("SSL 模式无效。");
    }
}

public sealed record ConnectionProfileExport(string Id, string Name, string Host, uint Port, string UserName,
    string? Password, string? DefaultDatabase, string SslMode);
public sealed record ConnectionExportDocument(int SchemaVersion, DateTimeOffset ExportedAtUtc,
    bool ContainsPasswords, IReadOnlyList<ConnectionProfileExport> Connections);
