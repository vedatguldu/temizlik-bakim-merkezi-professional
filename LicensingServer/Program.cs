using System.Text.Json;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<LicenseServerOptions>(builder.Configuration.GetSection("LicenseServer"));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<LicenseRepository>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new { ok = true, service = "LicensingServer" }));

app.MapPost("/api/v1/license/activate", async (
    LicenseActivationRequest request,
    LicenseRepository repository) =>
{
    if (string.IsNullOrWhiteSpace(request.LicenseKey) || string.IsNullOrWhiteSpace(request.MachineFingerprint))
    {
        return Results.BadRequest(new LicenseActivationResponse
        {
            IsValid = false,
            Message = "Lisans anahtarı ve cihaz bilgisi zorunludur.",
        });
    }

    var license = await repository.FindByKeyAsync(request.LicenseKey.Trim().ToUpperInvariant());
    if (license == null)
    {
        return Results.Json(new LicenseActivationResponse
        {
            IsValid = false,
            Message = "Lisans anahtarı bulunamadı.",
        }, statusCode: StatusCodes.Status401Unauthorized);
    }

    if (!license.IsActive)
    {
        return Results.Json(new LicenseActivationResponse
        {
            IsValid = false,
            Message = "Lisans pasif durumda.",
            LicenseId = license.Id,
            PlanName = license.PlanName,
        }, statusCode: StatusCodes.Status401Unauthorized);
    }

    if (license.ExpiresAtUtc.HasValue && license.ExpiresAtUtc.Value <= DateTime.UtcNow)
    {
        return Results.Json(new LicenseActivationResponse
        {
            IsValid = false,
            Message = "Lisans süresi dolmuş.",
            LicenseId = license.Id,
            PlanName = license.PlanName,
        }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var machine = request.MachineFingerprint.Trim();
    var knownMachine = license.Machines.Any(x => string.Equals(x, machine, StringComparison.Ordinal));
    if (!knownMachine && license.Machines.Count >= license.MaxMachines)
    {
        return Results.Json(new LicenseActivationResponse
        {
            IsValid = false,
            Message = "Bu lisans için cihaz limiti aşıldı.",
            LicenseId = license.Id,
            PlanName = license.PlanName,
            Features = license.Features.ToArray(),
        }, statusCode: StatusCodes.Status401Unauthorized);
    }

    if (!knownMachine)
    {
        license.Machines.Add(machine);
    }

    license.LastActivatedAtUtc = DateTime.UtcNow;
    license.LastActivatedAppVersion = request.AppVersion;
    await repository.UpsertAsync(license);

    return Results.Ok(new LicenseActivationResponse
    {
        IsValid = true,
        Message = "Lisans doğrulandı.",
        LicenseId = license.Id,
        PlanName = license.PlanName,
        Features = license.Features.ToArray(),
    });
});

app.MapGet("/api/v1/admin/licenses", async (HttpContext context, LicenseRepository repository, IOptions<LicenseServerOptions> options) =>
{
    if (!IsAdminAuthorized(context, options.Value))
    {
        return Results.Unauthorized();
    }

    var items = await repository.GetAllAsync();
    return Results.Ok(items.OrderByDescending(x => x.CreatedAtUtc));
});

app.MapPost("/api/v1/admin/licenses", async (
    HttpContext context,
    CreateLicenseRequest request,
    LicenseRepository repository,
    IOptions<LicenseServerOptions> options) =>
{
    if (!IsAdminAuthorized(context, options.Value))
    {
        return Results.Unauthorized();
    }

    var key = string.IsNullOrWhiteSpace(request.Key)
        ? GenerateLifetimeKey()
        : request.Key.Trim().ToUpperInvariant();

    var existing = await repository.FindByKeyAsync(key);
    if (existing != null)
    {
        return Results.Conflict(new { message = "Bu lisans anahtarı zaten var." });
    }

    var license = new LicenseRecord
    {
        Id = Guid.NewGuid().ToString("N"),
        Key = key,
        PlanName = string.IsNullOrWhiteSpace(request.PlanName) ? "PRO_LIFETIME" : request.PlanName.Trim(),
        IsActive = true,
        MaxMachines = request.MaxMachines <= 0 ? 2 : request.MaxMachines,
        CreatedAtUtc = DateTime.UtcNow,
        ExpiresAtUtc = request.ExpiresAtUtc,
        Features = request.Features?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            ?? new List<string> { "pro.smart-plan", "pro.risk-forecast", "pro.analytics", "pro.backup" },
        Machines = new List<string>(),
    };

    await repository.UpsertAsync(license);
    return Results.Ok(license);
});

app.MapPost("/api/v1/admin/licenses/{key}/deactivate", async (string key, HttpContext context, LicenseRepository repository, IOptions<LicenseServerOptions> options) =>
{
    if (!IsAdminAuthorized(context, options.Value))
    {
        return Results.Unauthorized();
    }

    var item = await repository.FindByKeyAsync(key.Trim().ToUpperInvariant());
    if (item == null)
    {
        return Results.NotFound();
    }

    item.IsActive = false;
    await repository.UpsertAsync(item);
    return Results.Ok(item);
});

app.MapPost("/api/v1/admin/licenses/{key}/activate", async (string key, HttpContext context, LicenseRepository repository, IOptions<LicenseServerOptions> options) =>
{
    if (!IsAdminAuthorized(context, options.Value))
    {
        return Results.Unauthorized();
    }

    var item = await repository.FindByKeyAsync(key.Trim().ToUpperInvariant());
    if (item == null)
    {
        return Results.NotFound();
    }

    item.IsActive = true;
    await repository.UpsertAsync(item);
    return Results.Ok(item);
});

app.MapPost("/api/v1/admin/licenses/{key}/reset-machines", async (string key, HttpContext context, LicenseRepository repository, IOptions<LicenseServerOptions> options) =>
{
    if (!IsAdminAuthorized(context, options.Value))
    {
        return Results.Unauthorized();
    }

    var item = await repository.FindByKeyAsync(key.Trim().ToUpperInvariant());
    if (item == null)
    {
        return Results.NotFound();
    }

    item.Machines.Clear();
    await repository.UpsertAsync(item);
    return Results.Ok(item);
});

app.Run();

static bool IsAdminAuthorized(HttpContext context, LicenseServerOptions options)
{
    if (!context.Request.Headers.TryGetValue("X-Admin-Token", out var headerToken))
    {
        return false;
    }

    var configuredToken = Environment.GetEnvironmentVariable("TBM_ADMIN_TOKEN");
    if (string.IsNullOrWhiteSpace(configuredToken))
    {
        configuredToken = options.AdminToken;
    }

    return !string.IsNullOrWhiteSpace(configuredToken) &&
           string.Equals(headerToken.ToString(), configuredToken, StringComparison.Ordinal);
}

static string GenerateLifetimeKey()
{
    var a = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
    var b = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
    return $"TBM-PRO-LIFETIME-{a}-{b}";
}

public sealed class LicenseServerOptions
{
    public string AdminToken { get; set; } = "CHANGE_ME_ADMIN_TOKEN";

    public string StoragePath { get; set; } = "App_Data/licenses.json";
}

public sealed class LicenseRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly string _storagePath;

    public LicenseRepository(IOptions<LicenseServerOptions> options)
    {
        _storagePath = Path.GetFullPath(options.Value.StoragePath);
        var directory = Path.GetDirectoryName(_storagePath)!;
        Directory.CreateDirectory(directory);
        if (!File.Exists(_storagePath))
        {
            File.WriteAllText(_storagePath, "[]");
        }
    }

    public async Task<List<LicenseRecord>> GetAllAsync()
    {
        await _mutex.WaitAsync();
        try
        {
            return await ReadAllInternalAsync();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<LicenseRecord?> FindByKeyAsync(string key)
    {
        await _mutex.WaitAsync();
        try
        {
            var all = await ReadAllInternalAsync();
            return all.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task UpsertAsync(LicenseRecord record)
    {
        await _mutex.WaitAsync();
        try
        {
            var all = await ReadAllInternalAsync();
            var index = all.FindIndex(x => string.Equals(x.Key, record.Key, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                all[index] = record;
            }
            else
            {
                all.Add(record);
            }

            var json = JsonSerializer.Serialize(all, JsonOptions);
            await File.WriteAllTextAsync(_storagePath, json);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<List<LicenseRecord>> ReadAllInternalAsync()
    {
        var json = await File.ReadAllTextAsync(_storagePath);
        var records = JsonSerializer.Deserialize<List<LicenseRecord>>(json, JsonOptions);
        return records ?? new List<LicenseRecord>();
    }
}

public sealed class LicenseRecord
{
    public string Id { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public string PlanName { get; set; } = "PRO_LIFETIME";

    public bool IsActive { get; set; } = true;

    public int MaxMachines { get; set; } = 2;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ExpiresAtUtc { get; set; }

    public DateTime? LastActivatedAtUtc { get; set; }

    public string LastActivatedAppVersion { get; set; } = string.Empty;

    public List<string> Features { get; set; } = new();

    public List<string> Machines { get; set; } = new();
}

public sealed class CreateLicenseRequest
{
    public string Key { get; set; } = string.Empty;

    public string PlanName { get; set; } = "PRO_LIFETIME";

    public int MaxMachines { get; set; } = 2;

    public DateTime? ExpiresAtUtc { get; set; }

    public string[]? Features { get; set; }
}

public sealed class LicenseActivationRequest
{
    public string LicenseKey { get; set; } = string.Empty;

    public string MachineFingerprint { get; set; } = string.Empty;

    public string AppVersion { get; set; } = string.Empty;
}

public sealed class LicenseActivationResponse
{
    public bool IsValid { get; set; }

    public string Message { get; set; } = string.Empty;

    public string LicenseId { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public string[] Features { get; set; } = Array.Empty<string>();
}
