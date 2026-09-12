using KidsWearStore.Data;
using KidsWearStore.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ----------------------------------------------------
// DATABASE
// ----------------------------------------------------

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure();
        }));

// ----------------------------------------------------
// IDENTITY
// ----------------------------------------------------

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 6;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireNonAlphanumeric = false;

        options.User.RequireUniqueEmail = true;

        // Production-friendly cookie settings
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// ----------------------------------------------------
// APPLICATION SERVICES
// ----------------------------------------------------

builder.Services.AddHttpClient();

builder.Services.AddControllersWithViews();

builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(20);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;

    // Session cookie should only travel over HTTPS in production.
    options.Cookie.SecurePolicy =
        CookieSecurePolicy.SameAsRequest;
});

// ----------------------------------------------------
// BUILD APPLICATION
// ----------------------------------------------------

var app = builder.Build();

// ----------------------------------------------------
// ERROR HANDLING / SECURITY
// ----------------------------------------------------

if (!app.Environment.IsDevelopment())
{
    // Never expose detailed exceptions in production.
    app.UseExceptionHandler("/Home/Error");

    // Tell browsers to use HTTPS.
    app.UseHsts();
}
else
{
    // Development environment keeps detailed errors available.
    app.UseDeveloperExceptionPage();
}

// ----------------------------------------------------
// HTTP PIPELINE
// ----------------------------------------------------

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseSession();

app.UseAuthentication();

app.UseAuthorization();

// ----------------------------------------------------
// ROUTING
// ----------------------------------------------------

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// ----------------------------------------------------
// DATABASE INITIALIZATION
// ROLES + CATEGORIES + ADMIN USER
// ----------------------------------------------------

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    // ------------------------------------------------
    // DATABASE
    // ------------------------------------------------

    var db =
        services.GetRequiredService<ApplicationDbContext>();

    // ------------------------------------------------
    // CREATE DEFAULT PRODUCT CATEGORIES
    // ------------------------------------------------

    if (!await db.Categories.AnyAsync())
    {
        db.Categories.AddRange(
            new Category
            {
                Name = "Boys",
                Description = "Clothing and fashion for boys",
                IsActive = true
            },

            new Category
            {
                Name = "Girls",
                Description = "Clothing and fashion for girls",
                IsActive = true
            },

            new Category
            {
                Name = "Baby",
                Description = "Cute clothing for babies",
                IsActive = true
            },

            new Category
            {
                Name = "T-Shirts",
                Description = "Kids T-Shirts",
                IsActive = true
            },

            new Category
            {
                Name = "Dresses",
                Description = "Dresses and frocks for kids",
                IsActive = true
            },

            new Category
            {
                Name = "Bottom Wear",
                Description =
                    "Jeans, shorts, trousers and other bottom wear",
                IsActive = true
            }
        );

        await db.SaveChangesAsync();
    }

    // ------------------------------------------------
    // ROLES
    // ------------------------------------------------

    var roleManager =
        services.GetRequiredService<RoleManager<IdentityRole>>();

    var userManager =
        services.GetRequiredService<UserManager<ApplicationUser>>();

    string[] roles =
    {
        "Admin",
        "ProductManager",
        "Customer"
    };

    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            var roleResult =
                await roleManager.CreateAsync(
                    new IdentityRole(role));

            if (!roleResult.Succeeded)
            {
                foreach (var error in roleResult.Errors)
                {
                    Console.WriteLine(
                        $"Role creation error: {error.Description}");
                }
            }
        }
    }

    // ------------------------------------------------
    // ADMIN USER
    // ------------------------------------------------

    var adminEmail =
        Environment.GetEnvironmentVariable(
            "KIDSWEAR_ADMIN_EMAIL");

    var adminPassword =
        Environment.GetEnvironmentVariable(
            "KIDSWEAR_ADMIN_PASSWORD");

    if (!string.IsNullOrWhiteSpace(adminEmail) &&
        !string.IsNullOrWhiteSpace(adminPassword))
    {
        var adminUser =
            await userManager.FindByEmailAsync(adminEmail);

        // --------------------------------------------
        // CREATE ADMIN IF IT DOES NOT EXIST
        // --------------------------------------------

        if (adminUser == null)
        {
            adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                FirstName = "Store",
                LastName = "Admin"
            };

            var createResult =
                await userManager.CreateAsync(
                    adminUser,
                    adminPassword);

            if (!createResult.Succeeded)
            {
                foreach (var error in createResult.Errors)
                {
                    Console.WriteLine(
                        $"Admin creation error: {error.Description}");
                }
            }
        }

        // --------------------------------------------
        // MAKE SURE ADMIN HAS ADMIN ROLE
        // --------------------------------------------

        if (adminUser != null &&
            !await userManager.IsInRoleAsync(
                adminUser,
                "Admin"))
        {
            var roleResult =
                await userManager.AddToRoleAsync(
                    adminUser,
                    "Admin");

            if (!roleResult.Succeeded)
            {
                foreach (var error in roleResult.Errors)
                {
                    Console.WriteLine(
                        $"Admin role error: {error.Description}");
                }
            }
        }
    }
}

app.Run();