using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ApprovalFlow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class ReviewRegressionTests : IClassFixture<Factory>
{
    private readonly Factory f;
    public ReviewRegressionTests(Factory factory) => f = factory;

    [Fact]
    public async Task DatabaseRoundTripAndBothApisPreserveUtc()
    {
        using var c = await f.Login();
        var id = await f.New(c);
        var instant = new DateTime(2026, 10, 28, 15, 30, 0, DateTimeKind.Utc);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            await db.Requests.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, instant));
            await db.Audits.Where(x => x.RequestId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.At, instant));
            Assert.Equal(DateTimeKind.Utc, (await db.Requests.AsNoTracking().SingleAsync(x => x.Id == id)).CreatedAt.Kind);
        }
        var detail = await c.GetFromJsonAsync<JsonElement>($"/api/requests/{id}");
        foreach (var timestamp in new[] { detail.GetProperty("request").GetProperty("createdAt").GetString()!, detail.GetProperty("history")[0].GetProperty("at").GetString()! })
        {
            Assert.EndsWith("Z", timestamp);
            Assert.Equal(new DateTimeOffset(instant), DateTimeOffset.Parse(timestamp));
        }
        var list = await c.GetFromJsonAsync<JsonElement>("/api/requests");
        var item = Assert.Single(list.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetInt32() == id);
        Assert.EndsWith("Z", item.GetProperty("createdAt").GetString()!);
    }
    [Fact]
    public async Task ProductionCookiesRequireSecureTransport()
    {
        using var production = new ProductionFactory();
        using var c = production.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var response = await c.GetAsync("/api/csrf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), x => x.Contains("secure", StringComparison.OrdinalIgnoreCase));
        var options = production.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>().Get(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Assert.Equal(Microsoft.AspNetCore.Http.CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
    }
    [Fact]
    public async Task RoleResponsesAreExplicitForEachPath()
    {
        using var owner = await f.Login(); using var approver = await f.Login("approver");
        var id = await f.New(owner);
        Assert.Equal(HttpStatusCode.NoContent, (await Factory.Act(owner, id, "submit", await Factory.Version(owner, id))).StatusCode);
        var v = await Factory.Version(owner, id);
        Assert.Equal(HttpStatusCode.Forbidden, (await approver.PutAsJsonAsync($"/api/requests/{id}", new RequestInput("not allowed", "not allowed", 1, v))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Factory.Act(owner, id, "approve", v)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Factory.Act(owner, id, "return", v, "reason")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Factory.Act(approver, id, "submit", v)).StatusCode);
    }
    [Fact]
    public async Task ExpiredCookieNeedsAnonymousCsrfBeforeRelogin()
    {
        using var isolated = new Factory(); using var c = await isolated.Login();
        isolated.Clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/login", new LoginInput("requester@example.test", "Test-Only!Password2026"))).StatusCode);
        await Factory.Token(c);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/login", new LoginInput("requester@example.test", "Test-Only!Password2026"))).StatusCode);
    }
    [Theory]
    [InlineData("submit")]
    [InlineData("approve")]
    [InlineData("return")]
    public async Task AuditFailureRollsBackEveryTransition(string action)
    {
        using var owner = await f.Login(); using var approver = await f.Login("approver");
        var id = await f.New(owner);
        if (action != "submit") Assert.Equal(HttpStatusCode.NoContent, (await Factory.Act(owner, id, "submit", await Factory.Version(owner, id))).StatusCode);
        var client = action == "submit" ? owner : approver;
        var before = await owner.GetFromJsonAsync<JsonElement>($"/api/requests/{id}");
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectReviewTransition ON Audits AFTER INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE Action IN ('Submitted','Approved','Returned')) THROW 51000, 'Injected audit failure', 1; END");
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await Factory.Act(client, id, action, await Factory.Version(client, id), "reason")).StatusCode);
            var after = await owner.GetFromJsonAsync<JsonElement>($"/api/requests/{id}");
            Assert.Equal(before.GetProperty("request").GetProperty("version").GetString(), after.GetProperty("request").GetProperty("version").GetString());
            Assert.Equal(before.GetProperty("request").GetProperty("status").GetString(), after.GetProperty("request").GetProperty("status").GetString());
            Assert.Equal(before.GetProperty("history").GetArrayLength(), after.GetProperty("history").GetArrayLength());
        }
        finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectReviewTransition"); }
    }
    [Fact]
    public async Task AuditFailureRollsBackCreation()
    {
        using var c = await f.Login();
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var count = await db.Requests.CountAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectReviewCreate ON Audits AFTER INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE Action = 'Created') THROW 51000, 'Injected audit failure', 1; END");
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await c.PostAsJsonAsync("/api/requests", new RequestInput("atomic-create", "test", 1, Guid.Empty))).StatusCode);
            Assert.Equal(count, await db.Requests.CountAsync());
            Assert.False(await db.Requests.AnyAsync(x => x.Title == "atomic-create"));
        }
        finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectReviewCreate"); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingVersionIsBadRequest(bool decision)
    {
        using var c = await f.Login(); var id = await f.New(c);
        var payload = new { title = "test", purpose = "test", amount = 1, action = "submit" };
        var response = decision ? await c.PostAsJsonAsync($"/api/requests/{id}/actions", payload) : await c.PutAsJsonAsync($"/api/requests/{id}", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
public sealed class DevelopmentInputTests : IClassFixture<DevelopmentFactory>
{
    private readonly DevelopmentFactory f;
    public DevelopmentInputTests(DevelopmentFactory factory) => f = factory;
    [Theory]
    [InlineData("{", "application/json", 400)]
    [InlineData("{\"title\":\"x\",\"purpose\":\"x\",\"amount\":1.5}", "application/json", 400)]
    [InlineData("{\"title\":\"x\",\"purpose\":\"x\",\"amount\":\"oops\"}", "application/json", 400)]
    [InlineData("", "application/json", 400)]
    [InlineData("{}", "text/plain", 415)]
    [InlineData("{\"version\":\"oops\"}", "application/json", 400)]
    [InlineData("{}", "", 415)]
    public async Task BadPayloadsAreClientErrorsInDevelopment(string payload, string contentType, int expected)
    {
        using var c = await f.Login();
        using var body = new StringContent(payload, Encoding.UTF8);
        body.Headers.ContentType = contentType.Length == 0 ? null : new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        Assert.Equal((HttpStatusCode)expected, (await c.PostAsync("/api/requests", body)).StatusCode);
    }
}

public sealed class ProductionFactory : Factory
{
    public ProductionFactory() : base("Production") { }
}
