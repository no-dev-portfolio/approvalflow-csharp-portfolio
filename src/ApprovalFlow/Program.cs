using System.Security.Claims;
using System.Threading.RateLimiting;
using ApprovalFlow;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<AppDb>(o =>
{
    var connection = builder.Configuration.GetConnectionString("Database") ?? throw new InvalidOperationException("ConnectionStrings:Database is required.");
    o.UseSqlServer(connection);
});
builder.Services.AddScoped<Workflow>();
// Keep malformed JSON and binding failures as 4xx responses in every environment.
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(o => o.ThrowOnBadRequest = false);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "ApprovalFlow.Session";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    o.SlidingExpiration = false;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF-TOKEN";
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("login", c => RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue<int?>("LoginPermitLimit") ?? 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
if (args.Contains("--export-schema"))
{
    using var scope = app.Services.CreateScope();
    Console.Write(scope.ServiceProvider.GetRequiredService<AppDb>().Database.GenerateCreateScript());
    return;
}
app.UseExceptionHandler(handler => handler.Run(async c => { c.Response.StatusCode = 500; await c.Response.WriteAsJsonAsync(new { error = "処理に失敗しました。時間をおいて再試行してください。" }); }));
app.Use(async (c, next) =>
{
    c.Response.Headers["X-Content-Type-Options"] = "nosniff";
    c.Response.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'self'; script-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    c.Response.Headers["Referrer-Policy"] = "same-origin";
    if (c.Request.Path.StartsWithSegments("/api")) c.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
// All mutating API calls, including login, require a cookie-bound CSRF token.
app.Use(async (c, next) =>
{
    if (c.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(c.Request.Method) && !HttpMethods.IsHead(c.Request.Method))
    {
        try { await c.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(c); }
        catch (AntiforgeryValidationException) { c.Response.StatusCode = 400; await c.Response.WriteAsJsonAsync(new { error = "画面を再読み込みして操作してください。" }); return; }
    }
    await next();
});
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (AppDb db, CancellationToken ct) => await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));
app.MapGet("/api/csrf", (HttpContext c, IAntiforgery a) => Results.Ok(new { token = a.GetAndStoreTokens(c).RequestToken }));
app.MapPost("/api/login", async (LoginInput input, AppDb db, HttpContext c, CancellationToken ct) =>
{
    if ((input.Email?.Length ?? 0) > 200 || (input.Password?.Length ?? 0) > 200) return Results.Unauthorized();
    var user = await db.Users.SingleOrDefaultAsync(x => x.Email == (input.Email ?? "").Trim().ToLower(), ct);
    var hasher = new PasswordHasher<UserAccount>();
    // A dummy verification also performs the password hash on unknown email addresses.
    var target = user ?? new UserAccount { PasswordHash = DummyHash.Value };
    var valid = hasher.VerifyHashedPassword(target, target.PasswordHash, input.Password ?? "") != PasswordVerificationResult.Failed;
    if (user is null || !valid) return Results.Unauthorized();
    var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.Name), new Claim(ClaimTypes.Role, user.Role) }, CookieAuthenticationDefaults.AuthenticationScheme);
    await c.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Ok(new { user.Name, user.Role });
}).RequireRateLimiting("login");
var api = app.MapGroup("/api").RequireAuthorization();
api.MapPost("/logout", async (HttpContext c) => { await c.SignOutAsync(); return Results.NoContent(); });
api.MapGet("/me", (ClaimsPrincipal u) => Results.Ok(new { id = Id(u), name = u.Identity!.Name, role = Role(u) }));
api.MapGet("/requests", async (AppDb db, ClaimsPrincipal u, int? page, string? status, string? q, CancellationToken ct) =>
{
    if (page is < 1 or > 10000 || (q?.Length ?? 0) > 100 || (status is not null && !new[] { "Draft", "Submitted", "Approved", "Returned" }.Contains(status))) return Results.BadRequest(new { error = "検索条件が不正です。" });
    var query = Visible(db, u);
    if (status is not null) query = query.Where(x => x.Status == status);
    if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => x.Title.Contains(q));
    var total = await query.CountAsync(ct);
    var items = await query.OrderByDescending(x => x.Id).Skip(((page ?? 1) - 1) * 20).Take(20).Select(x => new { x.Id, x.Title, x.Amount, x.Status, x.Version, owner = x.Owner.Name, x.CreatedAt }).ToListAsync(ct);
    return Results.Ok(new { total, page = page ?? 1, items });
});
api.MapGet("/requests/{id:int}", async (int id, AppDb db, ClaimsPrincipal u, CancellationToken ct) =>
{
    var row = await Visible(db, u).Where(x => x.Id == id).Select(x => new { x.Id, x.OwnerId, x.Title, x.Purpose, x.Amount, x.Status, x.Version, owner = x.Owner.Name, x.CreatedAt }).SingleOrDefaultAsync(ct);
    if (row is null) return Results.NotFound();
    var history = await db.Audits.Where(x => x.RequestId == id).OrderBy(x => x.Id).Select(x => new { x.Action, x.Comment, x.At, actor = x.Actor.Name }).ToListAsync(ct);
    return Results.Ok(new { request = row, history });
});
api.MapPost("/requests", async (RequestInput input, AppDb db, ClaimsPrincipal u, CancellationToken ct) =>
{
    if (Role(u) != "Requester") return Results.Forbid();
    if (Workflow.Validate(input) is { } error) return Results.BadRequest(new { error });
    var row = new Request { OwnerId = Id(u), Title = input.Title!.Trim(), Purpose = input.Purpose!.Trim(), Amount = input.Amount };
    db.Requests.Add(row);
    db.Audits.Add(new Audit { Request = row, ActorId = Id(u), Action = "Created" });
    await db.SaveChangesAsync(ct);
    return Results.Created($"/api/requests/{row.Id}", new { row.Id, row.Version });
});
api.MapPut("/requests/{id:int}", async (int id, RequestInput input, AppDb db, Workflow flow, ClaimsPrincipal u, CancellationToken ct) =>
{
    if (Role(u) != "Requester") return Results.Forbid();
    var row = await Visible(db, u).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
    if (row is null) return Results.NotFound();
    if (Workflow.Validate(input) is { } error) return Results.BadRequest(new { error });
    if (input.Version == Guid.Empty) return Results.BadRequest(new { error = "更新にはVersionが必要です。再読み込みしてください。" });
    return await flow.Edit(row, input, Id(u), ct) ? Results.NoContent() : Results.Conflict(new { error = "状態が変更されました。再読み込みしてください。" });
});
api.MapPost("/requests/{id:int}/actions", async (int id, DecisionInput input, AppDb db, Workflow flow, ClaimsPrincipal u, CancellationToken ct) =>
{
    var row = await Visible(db, u).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
    if (row is null) return Results.NotFound();
    if (input.Version == Guid.Empty) return Results.BadRequest(new { error = "更新にはVersionが必要です。再読み込みしてください。" });
    if (input.Action is not ("submit" or "approve" or "return") || (input.Comment?.Length ?? 0) > 500 || (input.Comment?.Any(c => char.IsControl(c) && c != '\n' && c != '\r') ?? false) || (input.Action == "return" && string.IsNullOrWhiteSpace(input.Comment))) return Results.BadRequest(new { error = "差戻し理由は必須です。コメントは500文字以内で入力してください。" });
    return await flow.Transition(row, input, Id(u), Role(u), ct) ? Results.NoContent() : Results.Conflict(new { error = "権限または状態が変更されました。再読み込みしてください。" });
});
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    await db.Database.EnsureCreatedAsync();
    if (!await db.Users.AnyAsync())
    {
        var password = app.Configuration["DemoPassword"];
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12) throw new InvalidOperationException("DemoPassword must be supplied with at least 12 characters.");
        var hasher = new PasswordHasher<UserAccount>();
        foreach (var (email, name, role) in new[] { ("requester@example.test", "山田 花子", "Requester"), ("approver@example.test", "佐藤 健", "Approver"), ("other@example.test", "鈴木 直樹", "Requester") })
        {
            var account = new UserAccount { Email = email, Name = name, Role = role };
            account.PasswordHash = hasher.HashPassword(account, password);
            db.Users.Add(account);
        }
        await db.SaveChangesAsync();
        var first = await db.Users.SingleAsync(x => x.Email == "requester@example.test");
        var seed = new Request { OwnerId = first.Id, Title = "開発用モニターの購入", Purpose = "業務効率と作業環境の改善のため、27インチモニターを購入します。", Amount = 48000, Status = "Submitted" };
        db.Requests.Add(seed);
        db.Audits.Add(new Audit { Request = seed, ActorId = first.Id, Action = "Submitted", Comment = "デモデータ" });
        await db.SaveChangesAsync();
    }
}
await app.RunAsync();
static int Id(ClaimsPrincipal u) => int.Parse(u.FindFirstValue(ClaimTypes.NameIdentifier)!);
static string Role(ClaimsPrincipal u) => u.FindFirstValue(ClaimTypes.Role)!;
static IQueryable<Request> Visible(AppDb db, ClaimsPrincipal u)
{
    var id = Id(u);
    return Role(u) == "Approver" ? db.Requests.Where(x => x.Status != "Draft") : db.Requests.Where(x => x.OwnerId == id);
}
public partial class Program { }
internal static class DummyHash { internal static readonly string Value = new PasswordHasher<UserAccount>().HashPassword(new UserAccount(), "dummy-only-password"); }
