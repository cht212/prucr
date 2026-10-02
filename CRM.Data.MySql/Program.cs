using CRM.Data.Extensions;
using CRM.Data.Services;
using CRM.Data.Startup;
using Microsoft.AspNetCore.ResponseCompression;
using MySql.Data.MySqlClient;
using System.IO.Compression;

var builder = WebApplication.CreateBuilder(args);

LoadLocalEnvironmentFile(builder);

// El proveedor EventLog que ASP.NET Core agrega por defecto en Windows puede
// impedir el arranque desde Visual Studio cuando la cuenta actual no tiene
// permisos sobre la fuente ".NET Runtime". Consola y Debug cubren tanto la
// ventana de salida de Visual Studio como los logs del proceso desplegado.
builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddCrmDatabase(builder.Configuration);
builder.Services.AddCrmApplicationServices(builder.Environment);
builder.Services.AddCrmCookieAuthentication(builder.Environment);
builder.Services.AddCrmCors(builder.Configuration);
builder.Services.AddCrmRateLimiting();
builder.Services.AddCrmForwardedHeaders(builder.Configuration);
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        ["application/json", "application/javascript", "text/css", "image/svg+xml"]);
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
    options.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(options =>
    options.Level = CompressionLevel.Fastest);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services, builder.Configuration, app.Logger);

app.UseForwardedHeaders();
app.UseCrmExceptionHandler();
app.UseAuthentication();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseCrmSecurityHeaders();
app.UseResponseCompression();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/uploads"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await next();
});
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/" &&
        (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)) &&
        context.User.Identity?.IsAuthenticated != true)
    {
        context.Response.Redirect("/login.html");
        return;
    }

    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        if (context.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            context.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            context.Context.Response.Headers["Pragma"] = "no-cache";
            context.Context.Response.Headers["Expires"] = "0";
            return;
        }

        var cacheDuration = TimeSpan.FromDays(7);
        context.Context.Response.Headers["Cache-Control"] = $"public, max-age={(int)cacheDuration.TotalSeconds}";
        context.Context.Response.Headers["Vary"] = "Accept-Encoding";
    }
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("CRM");
app.UseCrmRequestSecurity();

// En local/ngrok no redirigimos a HTTPS porque el tunel ya entra por HTTPS.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Valida accesos y acciones contra los permisos efectivos de cada usuario.
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true &&
        (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)))
    {
        var required = CrmPermissionService.ResolveReadPermissions(context.Request.Path);
        if (required.Length > 0)
        {
            var idValue = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var role = context.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var effective = int.TryParse(idValue, out var userId)
                ? await context.RequestServices.GetRequiredService<CrmPermissionService>().GetEffectiveAsync(userId, role)
                : [];
            if (!required.Any(effective.Contains))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { message = "No tienes acceso a este apartado o información." });
                return;
            }
        }
    }
    if (context.User.Identity?.IsAuthenticated == true &&
        !HttpMethods.IsGet(context.Request.Method) &&
        !HttpMethods.IsHead(context.Request.Method) &&
        !HttpMethods.IsOptions(context.Request.Method))
    {
        var requiredPermission = CrmPermissionService.ResolveMutationPermission(context.Request.Path);
        if (requiredPermission != null)
        {
            var idValue = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var role = context.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var allowed = false;
            if (int.TryParse(idValue, out var userId))
            {
                var effective = await context.RequestServices.GetRequiredService<CrmPermissionService>()
                    .GetEffectiveAsync(userId, role);
                allowed = effective.Contains(requiredPermission) ||
                    (HttpMethods.IsPut(context.Request.Method) &&
                     requiredPermission == CrmPermissionService.EditContacts &&
                     CrmPermissionService.IsContactUpdatePath(context.Request.Path) &&
                     effective.Contains(CrmPermissionService.EditConversationContact));
            }
            if (!allowed)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "No tienes el permiso necesario para realizar esta acción.",
                    permiso = requiredPermission
                });
                return;
            }
        }
    }

    await next();
});
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

app.Run();

static void LoadLocalEnvironmentFile(WebApplicationBuilder builder)
{
    var path = Path.Combine(builder.Environment.ContentRootPath, "configuracion-local.env");
    if (!File.Exists(path))
    {
        return;
    }

    var values = File.ReadLines(path)
        .Select(line => line.Trim())
        .Where(line => line.Length > 0 && !line.StartsWith('#'))
        .Select(line => new { Line = line, Separator = line.IndexOf('=') })
        .Where(item => item.Separator > 0)
        .ToDictionary(
            item => item.Line[..item.Separator].Trim(),
            item => item.Line[(item.Separator + 1)..].Trim().Trim('"', '\''),
            StringComparer.OrdinalIgnoreCase);

    string? Value(string key) => values.TryGetValue(key, out var value) ? value : null;

    var host = Value("MYSQL_HOST");
    var database = Value("MYSQL_DATABASE");
    var user = Value("MYSQL_USER");
    var password = Value("MYSQL_PASSWORD");
    if (!string.IsNullOrWhiteSpace(host) &&
        !string.IsNullOrWhiteSpace(database) &&
        !string.IsNullOrWhiteSpace(user) &&
        !string.IsNullOrWhiteSpace(password))
    {
        var connection = new MySqlConnectionStringBuilder
        {
            Server = host,
            Port = uint.TryParse(Value("MYSQL_PORT"), out var port) ? port : 3306,
            Database = database,
            UserID = user,
            Password = password,
            SslMode = MySqlSslMode.Disabled,
            AllowPublicKeyRetrieval = true
        };
        builder.Configuration["ConnectionStrings:DefaultConnection"] = connection.ConnectionString;
    }

    if (!string.IsNullOrWhiteSpace(Value("CRM_BOOTSTRAP_USERNAME")))
    {
        builder.Configuration["Authentication:BootstrapUsername"] = Value("CRM_BOOTSTRAP_USERNAME");
    }
    if (!string.IsNullOrWhiteSpace(Value("CRM_BOOTSTRAP_PASSWORD")))
    {
        builder.Configuration["Authentication:BootstrapPassword"] = Value("CRM_BOOTSTRAP_PASSWORD");
    }

    var r2Mappings = new Dictionary<string, string>
    {
        ["R2_ACCOUNT_ID"] = "R2:AccountId",
        ["R2_ACCESS_KEY_ID"] = "R2:AccessKeyId",
        ["R2_SECRET_ACCESS_KEY"] = "R2:SecretAccessKey",
        ["R2_BUCKET_NAME"] = "R2:BucketName"
    };
    foreach (var mapping in r2Mappings)
    {
        if (!string.IsNullOrWhiteSpace(Value(mapping.Key)))
        {
            builder.Configuration[mapping.Value] = Value(mapping.Key);
        }
    }
}
