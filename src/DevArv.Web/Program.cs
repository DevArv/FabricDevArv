using DevArv.Web.Api;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Helpers;
using DevArv.Web.Models;
using DevArv.Web.Repositories;
using DevArv.Web.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Falta ConnectionStrings:Default (PostgreSQL).");

builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));

builder.Services
    .AddDefaultIdentity<IdentityUser>(o =>
    {
        o.SignIn.RequireConfirmedAccount = false;
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.Lockout.MaxFailedAccessAttempts = 8;
    })
    .AddEntityFrameworkStores<AppDbContext>();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Identity/Account/Login";
    o.ExpireTimeSpan = TimeSpan.FromDays(14);
    o.SlidingExpiration = true;
});

builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/");
});

// Adjuntos: hasta 15 archivos de 20 MB por defecto.
builder.Services.Configure<AttachmentOptions>(builder.Configuration.GetSection("Attachments"));
var attachmentOptions = builder.Configuration.GetSection("Attachments").Get<AttachmentOptions>() ?? new AttachmentOptions();
var maxRequest = attachmentOptions.MaxFileBytes * attachmentOptions.MaxFilesPerTask + 1024 * 1024;
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxRequest);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = maxRequest);

builder.Services.Configure<GitHubOptions>(builder.Configuration.GetSection("GitHub"));
builder.Services.AddHttpClient<GitHubService>();

builder.Services.AddSingleton<Clock>();
builder.Services.AddSingleton<AttachmentStorage>();

// Repositories: toda la lógica de negocio y de acceso a datos de las páginas.
builder.Services.AddScoped<ProjectsRepository>();
builder.Services.AddScoped<StagesRepository>();
builder.Services.AddScoped<TasksRepository>();
builder.Services.AddScoped<TaskAttachmentsRepository>();
builder.Services.AddScoped<WorkersRepository>();

builder.Services.AddScoped<PromptBuilder>();
builder.Services.AddScoped<WorkflowService>();
builder.Services.AddScoped<SystemActionExecutor>();
builder.Services.AddHostedService<Orchestrator>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await DbInitializer.InitializeAsync(scope.ServiceProvider);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// El registro público está desactivado salvo que Auth:AllowRegistration=true
// o que todavía no exista ningún usuario (para crear el primero).
var allowRegistration = app.Configuration.GetValue<bool>("Auth:AllowRegistration");
app.Use(async (context, next) =>
{
    if (!allowRegistration && context.Request.Path.StartsWithSegments("/Identity/Account/Register"))
    {
        var users = context.RequestServices.GetRequiredService<UserManager<IdentityUser>>();
        if (await users.Users.AnyAsync(context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
    }
    await next();
});

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapWorkerApi();

app.Run();
