using System.Text.Json;

namespace CRM.Data.Services;

public sealed record QuickReplyTemplate(
    string Title,
    string Message,
    string Category,
    bool Enabled);

public sealed class QuickReplyTemplatesFile
{
    public List<QuickReplyTemplate>? Templates { get; set; }
}

public sealed class QuickReplyTemplatesService
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private List<QuickReplyTemplate> _templates;

    public QuickReplyTemplatesService(IWebHostEnvironment environment)
    {
        var dataPath = Path.Combine(environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataPath);
        _filePath = Path.Combine(dataPath, "quick-replies.json");
        _templates = LoadTemplates();
    }

    public IReadOnlyList<QuickReplyTemplate> GetAll() => _templates;

    public IReadOnlyList<QuickReplyTemplate> GetEnabled() =>
        _templates.Where(template => template.Enabled).ToList();

    public IReadOnlyList<QuickReplyTemplate> Update(IEnumerable<QuickReplyTemplate>? templates)
    {
        var normalized = Normalize(templates).ToList();
        if (normalized.Count == 0)
        {
            normalized = CreateDefaultTemplates();
        }

        lock (_lock)
        {
            _templates = normalized;
            Save();
            return _templates.ToList();
        }
    }

    private List<QuickReplyTemplate> LoadTemplates()
    {
        if (!File.Exists(_filePath))
        {
            return CreateDefaultTemplates();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var file = JsonSerializer.Deserialize<QuickReplyTemplatesFile>(json, JsonOptions());
            var templates = Normalize(file?.Templates).ToList();
            return templates.Count > 0 ? templates : CreateDefaultTemplates();
        }
        catch
        {
            return CreateDefaultTemplates();
        }
    }

    private void Save()
    {
        var file = new QuickReplyTemplatesFile
        {
            Templates = _templates
        };

        File.WriteAllText(_filePath, JsonSerializer.Serialize(file, JsonOptions()));
    }

    private static IEnumerable<QuickReplyTemplate> Normalize(IEnumerable<QuickReplyTemplate>? templates)
    {
        if (templates == null)
        {
            yield break;
        }

        foreach (var template in templates)
        {
            var title = (template.Title ?? string.Empty).Trim();
            var message = (template.Message ?? string.Empty).Trim();
            var category = (template.Category ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
            {
                continue;
            }

            yield return new QuickReplyTemplate(
                title.Length > 80 ? title[..80] : title,
                message.Length > 1000 ? message[..1000] : message,
                string.IsNullOrWhiteSpace(category) ? "General" : (category.Length > 40 ? category[..40] : category),
                template.Enabled);
        }
    }

    private static List<QuickReplyTemplate> CreateDefaultTemplates() =>
    [
        new("Saludo", "Hola, gracias por escribirnos. Soy tu asesor, cuéntame en qué puedo ayudarte.", "Atención", true),
        new("Catalogo", "Claro, te comparto la información de productos disponibles. Si buscas algo especifico, dime modelo, medida o cantidad.", "Ventas", true),
        new("Datos para cotizar", "Para prepararte una cotización, por favor envíame producto, cantidad, ciudad de entrega y datos de contacto.", "Cotización", true),
        new("Seguimiento", "Hola, te escribo para dar seguimiento a tu solicitud. Quedo atento por si deseas avanzar con la cotización.", "Seguimiento", true),
        new("Cierre", "Perfecto, quedo atento a tu confirmacion para continuar con el pedido.", "Cierre", true)
    ];

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
