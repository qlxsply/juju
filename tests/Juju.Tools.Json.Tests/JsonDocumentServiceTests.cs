using Juju.Core.Errors;
using Juju.Core.Storage;
using Juju.Tools.Json.Documents;
using Juju.Tools.Json.Metadata;
using Juju.Tools.Json.Minify;
using Juju.Tools.Json.Storage;

namespace Juju.Tools.Json.Tests;

public sealed class JsonDocumentServiceTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "juju-tests", Guid.NewGuid().ToString("N"));
    private JsonDocumentService _documents = null!;

    public async Task InitializeAsync()
    {
        var writer = new AtomicFileWriter();
        await new DataRootService(writer).EnsureInitializedAsync(_root);
        _documents = new JsonDocumentService(_root, new JsonMetadataStore(_root, writer), writer, new DocumentRevisionService());
    }

    public Task DisposeAsync() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); return Task.CompletedTask; }

    [Fact]
    public async Task Create_uses_daily_sequence_and_persists_empty_object()
    {
        var created = await _documents.CreateAsync();
        Assert.EndsWith("001.json", created.FileName, StringComparison.Ordinal);
        var opened = await _documents.OpenAsync(created.Id);
        Assert.Equal("{}", opened.Content);
    }

    [Fact]
    public async Task Save_rejects_external_revision_change()
    {
        var created = await _documents.CreateAsync();
        var opened = await _documents.OpenAsync(created.Id);
        var path = Path.Combine(_root, "json", "documents", created.FileName);
        await File.WriteAllTextAsync(path, "{\"external\":true}");
        var error = await Assert.ThrowsAsync<JujuException>(() => _documents.SaveAsync(created.Id, "{}", opened.Revision));
        Assert.Equal(ErrorCode.ExternalModificationConflict, error.Code);
    }

    [Fact]
    public async Task Rename_preserves_document_identity_and_delete_moves_file_to_trash()
    {
        var created = await _documents.CreateAsync();
        await _documents.RenameAsync(created.Id, "payment-request");
        Assert.Equal("payment-request.json", (await _documents.ListAsync()).Single().FileName);
        await _documents.DeleteAsync(created.Id);
        Assert.Empty(await _documents.ListAsync());
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_root, "json", ".trash")));
    }

    [Theory]
    [InlineData("{ \"value\": 1e10, \"text\": \"a b\" }", "{\"value\":1e10,\"text\":\"a b\"}")]
    public void Minify_preserves_json_tokens(string input, string expected) => Assert.Equal(expected, new JsonLexicalMinifier().Minify(input));

    [Theory]
    [InlineData("CON")]
    [InlineData("../escape")]
    [InlineData("name/child")]
    public void Document_name_rejects_unsafe_values(string name) => Assert.Throws<JujuException>(() => JsonDocumentName.Normalize(name));
}
