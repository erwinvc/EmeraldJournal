using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using EmeraldJournal.Data;
using EmeraldJournal.Services;
using Microsoft.AspNetCore.DataProtection;
using MudBlazor.Services;
using MudBlazor;
using Microsoft.AspNetCore.Authentication.Cookies;
using EmeraldJournal.Models.ViewModels;
using System.Data;

var builder = WebApplication.CreateBuilder(args);

// ── 1) Blazor & Razor ─────────────────────────────────────────────────────
builder.Services.AddRazorPages().AddRazorPagesOptions(options => {
    options.RootDirectory = "/Components/Pages";
});
builder.Services.AddServerSideBlazor();

// ── 2) SQLite DB in App_Data ──────────────────────────────────────────────
var dbFile = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "emeraldjournal.db");
Directory.CreateDirectory(Path.GetDirectoryName(dbFile)!); 

builder.Services.AddDbContext<ApplicationDbContext>(opts =>
    opts.UseSqlite($"Data Source={dbFile}"));

// ── 2.5) Add Mud Services ─────────────────────────────────────────────
builder.Services.AddMudServices();

// ── 3) Identity ───────────────────────────────────────────────────────────
builder.Services.AddDefaultIdentity<IdentityUser>(options => {
    options.Password.RequiredLength = 6;
    options.Password.RequireDigit = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
    options.Password.RequiredUniqueChars = 0;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<ApplicationDbContext>();


// ── 4) Persist DataProtection keys ────────────────────────────────────────
var keysFolder = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys");
Directory.CreateDirectory(keysFolder);   // ← ensure keys folder exists

builder.Services
       .AddDataProtection()
       .PersistKeysToFileSystem(new DirectoryInfo(keysFolder))
       .SetApplicationName("EmeraldJournal");

// ── 5) Your application services ─────────────────────────────────────────
builder.Services.AddScoped<JournalService>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddHttpClient();
builder.Services.AddControllers();

builder.Services.AddMudMarkdownServices();

// ── 6) Build & Configure HTTP pipeline ──────────────────────────────────
var app = builder.Build();

if (!app.Environment.IsDevelopment()) {
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}


app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.MapPost("/api/account/login",
    async (LoginViewModel dto,
           SignInManager<IdentityUser> signInMgr) => {
               var result = await signInMgr.PasswordSignInAsync(
                                dto.UserName,
                                dto.Password,
                                dto.RememberMe,
                                lockoutOnFailure: false);

               if (result.Succeeded)
                   return Results.Ok();

               if (result.IsLockedOut) return Results.BadRequest("Account is locked.");
               if (result.IsNotAllowed) return Results.BadRequest("Account not allowed.");
               return Results.BadRequest("Invalid username or password.");
           });

app.MapPost("/api/account/register",
    async (RegisterViewModel dto,
           UserManager<IdentityUser> userMgr,
           SignInManager<IdentityUser> signInMgr,
           HttpContext http) => {
               var user = new IdentityUser { UserName = dto.UserName, Email = dto.UserName };
               var result = await userMgr.CreateAsync(user, dto.Password);

               if (!result.Succeeded)
                   return Results.BadRequest(result.Errors.Select(e => e.Description));

               var role = string.IsNullOrWhiteSpace(dto.Role) ? "User" : dto.Role;
               await userMgr.AddToRoleAsync(user, role);

               await signInMgr.SignInAsync(user, isPersistent: false);
               return Results.Ok();
           });

app.MapPost("/api/account/logout",
    async (SignInManager<IdentityUser> mgr) => {
        await mgr.SignOutAsync();
        return Results.Ok();
    });

// ── 7) Ensure DB is created ───────────────────────────────────────────────
using (var scope = app.Services.CreateScope()) {
    var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    ctx.Database.EnsureCreated();

    string[] roles = ["Admin", "User"];

    foreach (var r in roles)
        if (!await roleMgr.RoleExistsAsync(r))
            await roleMgr.CreateAsync(new IdentityRole(r));
}

app.Run();
