using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace CRM.Data.Services;

public sealed class R2StorageService
{
    private readonly IConfiguration _configuration;
    private readonly SocialIntegrationService _integrations;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<R2StorageService> _logger;

    public R2StorageService(
        IConfiguration configuration,
        SocialIntegrationService integrations,
        IWebHostEnvironment environment,
        ILogger<R2StorageService> logger)
    {
        _configuration = configuration;
        _integrations = integrations;
        _environment = environment;
        _logger = logger;
    }

    public async Task<R2UploadResult> UploadAsync(
        IFormFile file,
        string folder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Length <= 0)
        {
            throw new InvalidOperationException("El archivo está vacío.");
        }

        var accountId = Required("R2:AccountId");
        var accessKeyId = Required("R2:AccessKeyId");
        var secretAccessKey = Required("R2:SecretAccessKey");
        var bucketName = Required("R2:BucketName");
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var safeFolder = string.Join('/', (folder ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizePathSegment));
        var objectKey = string.Join('/', new[]
        {
            safeFolder,
            DateTime.UtcNow.ToString("yyyy/MM"),
            $"{Guid.NewGuid():N}{extension}"
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

        var config = new AmazonS3Config
        {
            ServiceURL = $"https://{accountId}.r2.cloudflarestorage.com",
            ForcePathStyle = true,
            AuthenticationRegion = "auto"
        };

        using var client = new AmazonS3Client(
            new BasicAWSCredentials(accessKeyId, secretAccessKey),
            config);
        await using var stream = file.OpenReadStream();

        try
        {
            var request = new PutObjectRequest
            {
                BucketName = bucketName,
                Key = objectKey,
                InputStream = stream,
                ContentType = string.IsNullOrWhiteSpace(file.ContentType)
                    ? "application/octet-stream"
                    : file.ContentType,
                AutoCloseStream = false,
                // R2 usa el protocolo S3, pero no necesita las validaciones
                // de checksum específicas de AWS que algunas versiones del SDK agregan.
                DisablePayloadSigning = true,
                DisableDefaultChecksumValidation = true
            };
            request.Metadata["original-filename"] = Uri.EscapeDataString(Path.GetFileName(file.FileName));

            await client.PutObjectAsync(request, cancellationToken);

            var protectedUrl = $"/api/archivos/r2?key={Uri.EscapeDataString(objectKey)}";
            _logger.LogInformation(
                "Archivo almacenado en Cloudflare R2. Bucket={Bucket}, Key={Key}, Size={Size}",
                bucketName,
                objectKey,
                file.Length);

            return new R2UploadResult(
                protectedUrl,
                objectKey,
                file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? "image" : "raw");
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "Cloudflare R2 rechazó el archivo. Status={StatusCode}", ex.StatusCode);
            throw new InvalidOperationException("Cloudflare R2 rechazó el archivo.", ex);
        }
        catch (AmazonServiceException ex)
        {
            _logger.LogError(ex, "No se pudo conectar con Cloudflare R2.");
            throw new InvalidOperationException("No se pudo conectar con Cloudflare R2.", ex);
        }
    }

    public async Task<R2DownloadResult> DownloadAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        objectKey = ValidateObjectKey(objectKey);
        var accountId = Required("R2:AccountId");
        var accessKeyId = Required("R2:AccessKeyId");
        var secretAccessKey = Required("R2:SecretAccessKey");
        var bucketName = Required("R2:BucketName");

        var config = new AmazonS3Config
        {
            ServiceURL = $"https://{accountId}.r2.cloudflarestorage.com",
            ForcePathStyle = true,
            AuthenticationRegion = "auto"
        };

        using var client = new AmazonS3Client(
            new BasicAWSCredentials(accessKeyId, secretAccessKey),
            config);

        try
        {
            using var response = await client.GetObjectAsync(bucketName, objectKey, cancellationToken);
            await using var memory = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memory, cancellationToken);
            var contentType = string.IsNullOrWhiteSpace(response.Headers.ContentType)
                ? "application/octet-stream"
                : response.Headers.ContentType;
            return new R2DownloadResult(memory.ToArray(), contentType);
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "No se pudo leer el objeto de R2. Key={Key}, Status={StatusCode}", objectKey, ex.StatusCode);
            throw new InvalidOperationException("No se pudo leer el archivo almacenado.", ex);
        }
    }

    public async Task<R2DownloadResult> DownloadLocalAsync(
        string storedName,
        string? contentType,
        CancellationToken cancellationToken = default)
    {
        var safeName = Path.GetFileName(storedName ?? string.Empty);
        var parts = safeName.Split('.', 2);
        if (parts.Length != 2 || parts[0].Length != 32 ||
            !parts[0].All(Uri.IsHexDigit) ||
            parts[1].Length is < 1 or > 10 || !parts[1].All(char.IsLetterOrDigit))
        {
            throw new InvalidOperationException("La referencia del archivo local no es válida.");
        }

        var path = Path.Combine(_environment.ContentRootPath, "App_Data", "private-uploads", safeName);
        if (!File.Exists(path)) throw new InvalidOperationException("El archivo local no existe.");

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return new R2DownloadResult(
            bytes,
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
    }

    public async Task<R2UploadResult> SaveIncomingLocalAsync(
        byte[] fileBytes,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var storedName = $"{Guid.NewGuid():N}{extension}";
        var absoluteFolder = Path.Combine(_environment.ContentRootPath, "App_Data", "private-uploads");
        Directory.CreateDirectory(absoluteFolder);
        await File.WriteAllBytesAsync(Path.Combine(absoluteFolder, storedName), fileBytes, cancellationToken);

        var relativeUrl = $"/api/archivos/local/{storedName}";
        _logger.LogWarning(
            "Archivo entrante guardado localmente porque R2 no estuvo disponible. Url={Url}",
            relativeUrl);

        return new R2UploadResult(
            relativeUrl,
            $"local/{storedName}",
            contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? "image" : "raw");
    }

    private string Required(string key)
    {
        var value = _integrations.GetConfiguredValue(key) ?? _configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Falta configurar {key}.");
        }

        return value.Trim();
    }

    private static string SanitizePathSegment(string value) =>
        string.Concat(value.Trim().Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-'));

    private static string ValidateObjectKey(string objectKey)
    {
        var value = (objectKey ?? string.Empty).Trim();
        if (value.Length is < 1 or > 1024 || value.StartsWith('/') || value.Contains('\\') ||
            value.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidOperationException("La referencia del archivo no es válida.");
        }
        return value;
    }
}

public sealed record R2UploadResult(
    string SecureUrl,
    string PublicId,
    string ResourceType);

public sealed record R2DownloadResult(byte[] Content, string ContentType);
