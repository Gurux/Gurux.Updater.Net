using Gurux.Updater.Tool;
using System.Net;
using System.Text.Json;
using Xunit;
namespace Gurux.Updater.Tool.Tests;
public class ManufacturerSettingsTests
{
    [Fact] public void ParsesStandaloneListAndRejectsConflictingCommands()
    {
        Assert.Equal("https://gurux.github.io/Gurux.DLMS.DeviceProfiles/manufacturers.json", GXUpdaterOptions.Parse(["check", "--list-manufacturer-settings"]).CatalogUrl.AbsoluteUri);
        Assert.True(GXUpdaterOptions.Parse(["check", "--list-manufacturer-settings"]).ListManufacturerSettings);
        Assert.Throws<ArgumentException>(()=>GXUpdaterOptions.Parse(["update", "--list-manufacturer-settings"]));
        Assert.Throws<ArgumentException>(()=>GXUpdaterOptions.Parse(["check", "--list-manufacturer-settings", "--list-releases"]));
    }
    sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        public bool FailCatalog;
        public bool MissingProduct;
        public bool FailIndex;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (FailCatalog) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            string body = request.RequestUri!.AbsolutePath.EndsWith("catalog.json")
                ? """{"schemaVersion":1,"items":[{"id":"Gurux.DLMS.DeviceProfiles","type":"configuration","releases":[{"version":"1.0.0","publishedAt":"2026-10-06T00:00:00Z","assets":[{"name":"manufacturers.json","downloadUrl":"https://example.test/manufacturers.json","size":0}]}]}]}"""
                : """{"SchemaVersion":2,"Manufacturers":[{"Name":"EDMI","Models":[{"Name":"Mk7","Interfaces":[{"Name":"HDLC","Versions":[{"Name":"RDF","Settings":[{"Id":"low","Name":"Low","Location":"https://example.test/Low.json"}]}]}]}]}]}""";
            if (MissingProduct && Calls == 1) body = "{\"schemaVersion\":1,\"items\":[]}";
            if (FailIndex) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
    [Fact] public async Task Catalog404IdentifiesUrlAndRemedy()
    {
        var options = GXUpdaterOptions.Parse(["check", "--list-manufacturer-settings"]);
        using var client = new HttpClient(new Handler { FailCatalog = true }); using var output = new StringWriter();
        var error = await Assert.ThrowsAsync<HttpRequestException>(()=>GXManufacturerSettingsCommand.RunAsync(client, options, output));
        Assert.Contains(options.CatalogUrl.AbsoluteUri, error.Message);
        Assert.Contains("--catalog-url", error.Message);
    }
    [Fact] public async Task MissingProductAndIndexErrorsProduceNoSuccessOutput()
    {
        var options = GXUpdaterOptions.Parse(["check", "--list-manufacturer-settings"]);
        using var output = new StringWriter();
        using var invalid = new HttpClient(new Handler { MissingProduct = true });
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>GXManufacturerSettingsCommand.RunAsync(invalid, options, output));
        using var failed = new HttpClient(new Handler { FailIndex = true });
        await Assert.ThrowsAsync<HttpRequestException>(()=>GXManufacturerSettingsCommand.RunAsync(failed, options, output));
        Assert.Equal("", output.ToString());
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task ListsIndexWithoutDownloadingProfile(bool json)
    {
        var options = GXUpdaterOptions.Parse(json ? ["check","--list-manufacturer-settings","--json"] : ["check","--list-manufacturer-settings"]);
        var handler = new Handler(); using var client = new HttpClient(handler); using var output = new StringWriter();
        await GXManufacturerSettingsCommand.RunAsync(client, options, output);
        Assert.Equal(1,handler.Calls); Assert.Contains("EDMI", output.ToString()); Assert.Contains("Low", output.ToString());
        if(json) { using var document=JsonDocument.Parse(output.ToString()); Assert.Equal("HDLC",document.RootElement[0].GetProperty("interface").GetString()); }
    }
}
