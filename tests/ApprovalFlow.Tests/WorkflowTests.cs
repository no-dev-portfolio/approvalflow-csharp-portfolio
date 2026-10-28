using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApprovalFlow;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class Factory : WebApplicationFactory<Program>
{
    private string? connection;
    private readonly string environment;
    public Factory() : this("Testing") { }
    protected Factory(string environment) => this.environment = environment;
    public TestClock Clock { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var baseConnection = Environment.GetEnvironmentVariable("TEST_DATABASE") ?? throw new InvalidOperationException("TEST_DATABASE is required; tests use a real SQL Server.");
        var runId = Environment.GetEnvironmentVariable("VERIFY_RUN_ID") ?? "manual";
        if (runId.Length > 40 || !System.Text.RegularExpressions.Regex.IsMatch(runId, "^[A-Za-z0-9_-]+$")) throw new InvalidOperationException("Invalid VERIFY_RUN_ID");
        var cs = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(baseConnection) { InitialCatalog = "ApprovalFlowTest_" + runId + "_" + Guid.NewGuid().ToString("N") };
        connection = cs.ConnectionString;
        builder.UseEnvironment(environment);
        builder.ConfigureServices(services => services.PostConfigure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme, o => o.TimeProvider = Clock));
        builder.UseSetting("LoginPermitLimit", "1000");
        builder.UseSetting("ConnectionStrings:Database", cs.ConnectionString);
        builder.UseSetting("DemoPassword", "Test-Only!Password2026");
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && connection is not null)
        {
            using var db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlServer(connection).Options);
            db.Database.EnsureDeleted();
            connection = null;
        }
    }
    public async Task<HttpClient> Login(string who = "requester")
    {
        var c = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await Token(c);
        var r = await c.PostAsJsonAsync("/api/login", new LoginInput(who + "@example.test", "Test-Only!Password2026"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        await Token(c);
        return c;
    }
    public static async Task Token(HttpClient c)
    {
        var data = await c.GetFromJsonAsync<JsonElement>("/api/csrf");
        c.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        c.DefaultRequestHeaders.Add("X-CSRF-TOKEN", data.GetProperty("token").GetString());
    }
    public async Task<int> New(HttpClient c)
    {
        var r = await c.PostAsJsonAsync("/api/requests", new RequestInput("テスト申請", "試験用購入", 30000, Guid.Empty));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }
    public static async Task<Guid> Version(HttpClient c, int id) => (await c.GetFromJsonAsync<JsonElement>($"/api/requests/{id}")).GetProperty("request").GetProperty("version").GetGuid();
    public static Task<HttpResponseMessage> Act(HttpClient c, int id, string action, Guid version, string comment = "") => c.PostAsJsonAsync($"/api/requests/{id}/actions", new DecisionInput(action, comment, version));
}

public sealed class WorkflowTests : IClassFixture<Factory>
{
    private readonly Factory f;
    public WorkflowTests(Factory factory) => f = factory;
    [Fact] public async Task AnonymousCannotRead() { using var c = f.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/requests")).StatusCode); }
    [Fact] public async Task LoginRequiresCsrf() { using var c = f.CreateClient(); Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/login", new LoginInput("requester@example.test", "Test-Only!Password2026"))).StatusCode); }
    [Fact] public async Task BadPasswordDenied() { using var c = f.CreateClient(); await Factory.Token(c); Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/login", new LoginInput("requester@example.test", "wrong"))).StatusCode); }
    [Fact]
    public async Task OtherOwnerCannotReadOrModify()
    {
        using var a = await f.Login(); var id = await f.New(a); var v = await Factory.Version(a, id); using var b = await f.Login("other");
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/requests/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsJsonAsync($"/api/requests/{id}", new RequestInput("攻撃", "攻撃", 1, v))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Factory.Act(b, id, "submit", v)).StatusCode);
    }
    [Fact] public async Task ApproverCannotReadDraftOrCreate() { using var a = await f.Login(); var id = await f.New(a); using var b = await f.Login("approver"); Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/requests/{id}")).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await b.PostAsJsonAsync("/api/requests", new RequestInput("x", "x", 1, Guid.Empty))).StatusCode); }
    [Fact] public async Task RequesterCannotSelfApprove() { using var c = await f.Login(); var id = await f.New(c); Assert.Equal(HttpStatusCode.NoContent, (await Factory.Act(c, id, "submit", await Factory.Version(c, id))).StatusCode); Assert.Equal(HttpStatusCode.Conflict, (await Factory.Act(c, id, "approve", await Factory.Version(c, id))).StatusCode); }
    [Fact]
    public async Task ReturnEditResubmitApproveAndAudit()
    {
        using var a = await f.Login(); using var b = await f.Login("approver"); var id = await f.New(a);
        Assert.Equal(HttpStatusCode.NoContent, (await Factory.Act(a, id, "submit", await Factory.Version(a, id))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Factory.Act(b, id, "return", await Factory.Version(b, id), "見積の説明を追加してください")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await a.PutAsJsonAsync($"/api/requests/{id}", new RequestInput("修正済み申請", "見積を比較しました", 32000, await Factory.Version(a, id)))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Factory.Act(a, id, "submit", await Factory.Version(a, id))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Factory.Act(b, id, "approve", await Factory.Version(b, id), "確認しました")).StatusCode);
        var data = await a.GetFromJsonAsync<JsonElement>($"/api/requests/{id}");
        Assert.Equal("Approved", data.GetProperty("request").GetProperty("status").GetString());
        Assert.Equal(6, data.GetProperty("history").GetArrayLength());
    }
    [Fact] public async Task StaleEditDoesNotOverwrite() { using var c = await f.Login(); var id = await f.New(c); var v = await Factory.Version(c, id); Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync($"/api/requests/{id}", new RequestInput("先の修正", "説明", 1, v))).StatusCode); Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/api/requests/{id}", new RequestInput("古い修正", "説明", 2, v))).StatusCode); }
    [Fact]
    public async Task ConcurrentApprovalOnlyOneWins()
    {
        using var a = await f.Login(); var id = await f.New(a); await Factory.Act(a, id, "submit", await Factory.Version(a, id)); using var b = await f.Login("approver"); var v = await Factory.Version(b, id);
        var results = await Task.WhenAll(Factory.Act(b, id, "approve", v), Factory.Act(b, id, "return", v, "差戻し"));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.NoContent); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        var data = await b.GetFromJsonAsync<JsonElement>($"/api/requests/{id}"); Assert.Equal(3, data.GetProperty("history").GetArrayLength());
    }
    [Fact] public async Task SubmittedCannotEdit() { using var c = await f.Login(); var id = await f.New(c); await Factory.Act(c, id, "submit", await Factory.Version(c, id)); Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/api/requests/{id}", new RequestInput("書換", "説明", 1, await Factory.Version(c, id)))).StatusCode); }
    [Fact] public async Task ReturnRequiresReason() { using var a = await f.Login(); var id = await f.New(a); await Factory.Act(a, id, "submit", await Factory.Version(a, id)); using var b = await f.Login("approver"); Assert.Equal(HttpStatusCode.BadRequest, (await Factory.Act(b, id, "return", await Factory.Version(b, id))).StatusCode); }
    [Theory][InlineData(0)][InlineData(-1)][InlineData(100000001)] public async Task InvalidAmountRejected(long amount) { using var c = await f.Login(); Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/requests", new RequestInput("件名", "説明", amount, Guid.Empty))).StatusCode); }
    [Theory][InlineData("")][InlineData(" ")][InlineData("bad\u0085title")] public async Task InvalidTitleRejected(string title) { using var c = await f.Login(); Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/requests", new RequestInput(title, "説明", 1, Guid.Empty))).StatusCode); }
    [Fact] public async Task PaginationAndFiltersBounded() { using var c = await f.Login(); Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/requests?page=0")).StatusCode); Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/requests?status=invalid")).StatusCode); var data = await c.GetFromJsonAsync<JsonElement>("/api/requests?status=Approved&q=存在しない件名"); Assert.Empty(data.GetProperty("items").EnumerateArray()); }
    [Fact] public async Task LogoutRemovesBrowserCookie() { using var c = await f.Login(); Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync("/api/logout", null)).StatusCode); Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/me")).StatusCode); }
    [Fact] public async Task MutationWithoutCsrfDenied() { using var c = await f.Login(); c.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/requests", new RequestInput("件名", "説明", 1, Guid.Empty))).StatusCode); }
    [Fact] public async Task ReadyAndSecurityHeaders() { using var c = f.CreateClient(); var r = await c.GetAsync("/health/ready"); Assert.Equal(HttpStatusCode.OK, r.StatusCode); Assert.Contains("frame-ancestors 'none'", r.Headers.GetValues("Content-Security-Policy").Single()); }
    [Fact] public async Task DatabaseConstraintsRejectInvalidAmount() { using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDb>(); db.Requests.Add(new Request { OwnerId = 1, Title = "invalid", Purpose = "invalid", Amount = 0 }); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); }
    [Fact]
    public async Task AuditFailureRollsBackRequestUpdate()
    {
        using var c = await f.Login(); var id = await f.New(c); var v = await Factory.Version(c, id);
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectTestEdit ON Audits AFTER INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE Action = 'Edited') THROW 51000, 'Injected audit failure', 1; END");
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await c.PutAsJsonAsync($"/api/requests/{id}", new RequestInput("must rollback", "説明", 2, v))).StatusCode);
            Assert.Equal(v, await Factory.Version(c, id));
            var row = await c.GetFromJsonAsync<JsonElement>($"/api/requests/{id}");
            Assert.Equal("テスト申請", row.GetProperty("request").GetProperty("title").GetString());
            Assert.Equal(1, row.GetProperty("history").GetArrayLength());
        }
        finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectTestEdit"); }
    }

}

public sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan duration) => now += duration;
}
public sealed class DevelopmentFactory : Factory
{
    public DevelopmentFactory() : base("Development") { }
}
