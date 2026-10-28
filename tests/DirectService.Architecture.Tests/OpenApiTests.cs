using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace DirectService.Architecture.Tests;

/// <summary>
/// The exported API description (platform design §e, E-36/E-37): routes, operations and DTO schema names stay exactly as at the restore tag
/// in Stages 2–4. The file is re-exported from the running host at the end of each stage and must still match (BUILD_STATUS proof).
/// </summary>
public sealed class OpenApiTests
{
    [Fact]
    public void The_exported_openapi_document_equals_the_restore_tag()
    {
        var g = Golden.OpenApi;
        var text = File.ReadAllText(Path.Combine(Golden.RepoRoot, g["file"]!.GetValue<string>())).Replace("\r\n", "\n", StringComparison.Ordinal);
        var doc = JsonNode.Parse(text)!;
        var paths = doc["paths"]!.AsObject();
        Assert.Equal(g["title"]!.GetValue<string>(), doc["info"]!["title"]!.GetValue<string>());
        Assert.Equal(g["paths"]!.GetValue<int>(), paths.Count);
        Assert.Equal(g["operations"]!.GetValue<int>(), paths.Sum(p => p.Value!.AsObject().Count(o => o.Key is "get" or "post" or "put" or "patch" or "delete")));
        Assert.Equal(g["schemas"]!.GetValue<int>(), doc["components"]!["schemas"]!.AsObject().Count);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        Assert.True(hash == g["sha256Lf"]!.GetValue<string>(),
            "docs/api/openapi-v1.json differs from the restore tag (routes, operations or DTO names changed). Re-export it from the host and compare.");
    }
}
