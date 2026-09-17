using KidsWearStore.Data;
using KidsWearStore.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ----------------------------------------------------
// DATABASE CONFIGURATION
// ----------------------------------------------------

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine(
        "CRITICAL: DefaultConnection is missing from configuration.");
}
else
{
    Console.WriteLine("DefaultConnection detected.");
}

// ----------------------------------------------------
// DATABASE PROVIDER SELECTION
//
// Local development:
//   SQL Server LocalDB
//
// Production:
//   Neon PostgreSQL
// ----------------------------------------------------

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (connectionString != null &&
    (connectionString.Contains(
        "Host=",
        StringComparison.OrdinalIgnoreCase)
        
     ||
        connectionString.StartsWith(
            "postgresql://",
            StringComparison.OrdinalIgnoreCase)
     ||
     connectionString.StartsWith(
        "postgres://",
        StringComparison.OrdinalIgnoreCase)))
    {
        // ------------------------------------------------
        // POSTGRESQL / NEON
        // ------------------------------------------------

        Console.WriteLine(
            "Database provider selected: PostgreSQL / Neon.");

        var postgresConnectionString = connectionString;

if (connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) ||
    connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
{
    var uri = new Uri(connectionString);

    var userInfo = uri.UserInfo.Split(':', 2);

    var builder = new Npgsql.NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.Port > 0 ? uri.Port : 5432,
        Database = uri.AbsolutePath.Trim('/'),
        Username = Uri.UnescapeDataString(userInfo[0]),
        Password = userInfo.Length > 1
            ? Uri.UnescapeDataString(userInfo[1])
            : ""
    };

    builder.SslMode = Npgsql.SslMode.Require;

    postgresConnectionString = builder.ConnectionString;
}

Console.WriteLine("Database provider selected: PostgreSQL / Neon.");

options.UseNpgsql(
    postgresConnectionString,
    npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorCodesToAdd: null);
    });
    }
    else
    {
        // ------------------------------------------------
        // SQL SERVER / LOCALDB
        // ------------------------------------------------

        Console.WriteLine(
            "Database provider selected: SQL Server / LocalDB.");

        options.UseSqlServer(
            connectionString,
            sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null);
            });
    }
});

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

        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan =
            TimeSpan.FromMinutes(10);
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
    app.UseExceptionHandler("/Home/Error");

    app.UseHsts();
}
else
{
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
// ----------------------------------------------------

try
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.Error.WriteLine(
            "DATABASE INITIALIZATION SKIPPED: DefaultConnection is not configured.");
    }
    else
    {
        using var scope = app.Services.CreateScope();

        var services = scope.ServiceProvider;

        var db =
            services.GetRequiredService<ApplicationDbContext>();

        Console.WriteLine(
            "Testing configured database connection...");

        // ------------------------------------------------
        // TEST DATABASE CONNECTION
        // ------------------------------------------------

        var canConnect =
            await db.Database.CanConnectAsync();

        if (!canConnect)
        {
            Console.Error.WriteLine(
                "DATABASE ERROR: Cannot connect to the configured database.");
        }
        else
        {
            Console.WriteLine(
                "DATABASE CONNECTION SUCCESSFUL.");

            // ------------------------------------------------
            // DEFAULT CATEGORIES
            // ------------------------------------------------

            if (!await db.Categories.AnyAsync())
            {
                db.Categories.AddRange(
                    new Category
                    {
                        Name = "Boys",
                        Description =
                            "Clothing and fashion for boys",
                        IsActive = true
                    },

                    new Category
                    {
                        Name = "Girls",
                        Description =
                            "Clothing and fashion for girls",
                        IsActive = true
                    },

                    new Category
                    {
                        Name = "Baby",
                        Description =
                            "Cute clothing for babies",
                        IsActive = true
                    },

                    new Category
                    {
                        Name = "T-Shirts",
                        Description =
                            "Kids T-Shirts",
                        IsActive = true
                    },

                    new Category
                    {
                        Name = "Dresses",
                        Description =
                            "Dresses and frocks for kids",
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

                Console.WriteLine(
                    "DEFAULT CATEGORIES CREATED.");
            }

            // ------------------------------------------------
            // ROLES
            // ------------------------------------------------

            var roleManager =
                services.GetRequiredService<
                    RoleManager<IdentityRole>>();

            var userManager =
                services.GetRequiredService<
                    UserManager<ApplicationUser>>();

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
                            Console.Error.WriteLine(
                                $"Role creation error: {error.Description}");
                        }
                    }
                    else
                    {
                        Console.WriteLine(
                            $"Role created: {role}");
                    }
                }
            }

            // ------------------------------------------------
            // PRODUCTION ADMIN
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
                    await userManager.FindByEmailAsync(
                        adminEmail);

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
                            Console.Error.WriteLine(
                                $"Admin creation error: {error.Description}");
                        }
                    }
                    else
                    {
                        Console.WriteLine(
                            "Production admin user created.");
                    }
                }

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
                            Console.Error.WriteLine(
                                $"Admin role error: {error.Description}");
                        }
                    }
                    else
                    {
                        Console.WriteLine(
                            "Production admin role assigned.");
                    }
                }
            }
            else
            {
                Console.WriteLine(
                    "Production admin environment variables are not configured.");
            }
        }
    }
}
catch (Exception ex)
{
    // ----------------------------------------------------
    // DO NOT CRASH THE PROCESS DURING DATABASE DIAGNOSIS
    // ----------------------------------------------------

    Console.Error.WriteLine(
        "==================================================");

    Console.Error.WriteLine(
        "DATABASE STARTUP ERROR");

    Console.Error.WriteLine(
        "==================================================");

    Console.Error.WriteLine(
        ex.ToString());

    Console.Error.WriteLine(
        "==================================================");
}

// ----------------------------------------------------
// START APPLICATION
// ----------------------------------------------------

app.Run();