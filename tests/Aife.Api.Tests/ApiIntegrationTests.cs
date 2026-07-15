using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Aife.Api.Tests;

public class ApiIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ApiIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_returns_healthy()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        body["status"]!.ToString().Should().Be("healthy");
    }

    [Fact]
    public async Task Full_flow_upload_session_artifacts_review()
    {
        var client = _factory.CreateClient();

        // ── Upload prototype ──
        var uploadBody = new { html = "<button>Submit</button><table></table>", css = "" };
        var uploadResponse = await client.PostAsJsonAsync("/api/v1/prototypes", uploadBody);

        uploadResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var uploadJson = JObject.Parse(await uploadResponse.Content.ReadAsStringAsync());
        var prototypeId = uploadJson["id"]!.ToString();
        prototypeId.Should().StartWith("p-");

        // ── Start session ──
        var sessionBody = new { prototypeId };
        var sessionResponse = await client.PostAsJsonAsync("/api/v1/sessions", sessionBody);

        sessionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessionJson = JObject.Parse(await sessionResponse.Content.ReadAsStringAsync());
        var sessionId = sessionJson["sessionId"]!.ToString();
        sessionId.Should().StartWith("s-");

        // ── Get session status ──
        var statusResponse = await client.GetAsync($"/api/v1/sessions/{sessionId}");
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var statusJson = JObject.Parse(await statusResponse.Content.ReadAsStringAsync());
        statusJson["status"]!.ToString().Should().Be("Completed");

        // ── Get artifacts ──
        var artifactsResponse = await client.GetAsync($"/api/v1/sessions/{sessionId}/artifacts");
        artifactsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var artifactsJson = JArray.Parse(await artifactsResponse.Content.ReadAsStringAsync());
        artifactsJson.Should().NotBeEmpty();
        artifactsJson[0]!["path"]!.ToString().Should().Be("src/Generated.tsx");

        // ── Get review ──
        var reviewResponse = await client.GetAsync($"/api/v1/sessions/{sessionId}/review");
        reviewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var reviewJson = JObject.Parse(await reviewResponse.Content.ReadAsStringAsync());
        // Stub reviewer returns score=100 by default
        ((int)reviewJson["score"]!).Should().Be(100);
    }

    [Fact]
    public async Task Upload_prototype_returns_201_with_id()
    {
        var client = _factory.CreateClient();

        var uploadBody = new { html = "<div>Hello</div>", css = ".x { }" };
        var response = await client.PostAsJsonAsync("/api/v1/prototypes", uploadBody);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = JObject.Parse(await response.Content.ReadAsStringAsync());
        json["id"]!.ToString().Should().NotBeNullOrEmpty();
        json["html"]!.ToString().Should().Be("<div>Hello</div>");
    }

    [Fact]
    public async Task Get_nonexistent_prototype_returns_404()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/prototypes/nonexistent");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Start_session_with_nonexistent_prototype_returns_404()
    {
        var client = _factory.CreateClient();

        var sessionBody = new { prototypeId = "nonexistent" };
        var response = await client.PostAsJsonAsync("/api/v1/sessions", sessionBody);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Conformance_endpoint_returns_findings_for_prototype_with_drift()
    {
        var client = _factory.CreateClient();

        // Upload a prototype with a hardcoded color (not in the token palette)
        var uploadBody = new { html = """<div style="background:#123456"><button>OK</button></div>""", css = "" };
        var uploadResponse = await client.PostAsJsonAsync("/api/v1/prototypes", uploadBody);
        var uploadJson = JObject.Parse(await uploadResponse.Content.ReadAsStringAsync());
        var prototypeId = uploadJson["id"]!.ToString();

        // Request conformance report
        var conformanceResponse = await client.GetAsync($"/api/v1/prototypes/{prototypeId}/conformance");

        conformanceResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var reportJson = JObject.Parse(await conformanceResponse.Content.ReadAsStringAsync());
        var findings = reportJson["findings"] as JArray;
        findings.Should().NotBeNull();
        findings!.Should().NotBeEmpty();
        findings.Should().Contain(f => f!["ruleId"]!.ToString() == "CONF_COLOR_OFF_TOKEN");
    }

    [Fact]
    public async Task Generate_endpoint_produces_conformant_prototype_from_intent()
    {
        var client = _factory.CreateClient();

        var generateBody = new
        {
            intent = "A dashboard with a table and button.",
            pages = new[]
            {
                new
                {
                    name = "Dashboard",
                    layout = "AppLayout",
                    regions = new[]
                    {
                        new { slot = "main", component = "BUSGrid" }
                    },
                    actions = new[]
                    {
                        new { slot = "main", component = "BUSButton", label = "Add" }
                    }
                }
            }
        };

        var generateResponse = await client.PostAsJsonAsync("/api/v1/prototypes/generate", generateBody);

        generateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var generateJson = JObject.Parse(await generateResponse.Content.ReadAsStringAsync());
        var prototypeId = generateJson["prototypeId"]!.ToString();
        prototypeId.Should().StartWith("p-gen-");

        // Fetch the generated prototype
        var getResponse = await client.GetAsync($"/api/v1/prototypes/{prototypeId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var prototypeJson = JObject.Parse(await getResponse.Content.ReadAsStringAsync());
        prototypeJson["html"]!.ToString().Should().NotBeEmpty();
        prototypeJson["css"]!.ToString().Should().NotBeEmpty();
    }
}
