namespace CRM.Data.Services;

using System.Text.Json;

public sealed class BotSettingsService
{
    private const string DefaultMessage =
        "Hola, gracias por escribirnos. En breve te atenderá un asesor.\n\nResponde con una opción:\n1. Hablar con un asesor\n2. Información de productos\n3. Cotización\n4. Horarios y ubicación";
    private readonly object _sync = new();
    private readonly string _settingsPath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public BotSettingsService(IConfiguration configuration, IWebHostEnvironment environment)
    {
        _settingsPath = Path.Combine(environment.ContentRootPath, "App_Data", "bot-whatsapp.json");

        WhatsAppAutoReplyEnabled =
            bool.TryParse(configuration["Bot:WhatsApp:AutoReplyEnabled"], out var enabled) &&
            enabled;

        WhatsAppAutoReplyMessage = configuration["Bot:WhatsApp:AutoReplyMessage"] ??
            DefaultMessage;

        MaxAutoRepliesPerConversation =
            int.TryParse(configuration["Bot:WhatsApp:MaxAutoRepliesPerConversation"], out var max) &&
            max > 0
                ? max
                : 2;

        Options = CrearOpcionesDefault();
        CargarDesdeArchivo();
    }

    public bool WhatsAppAutoReplyEnabled { get; private set; }

    public string WhatsAppAutoReplyMessage { get; private set; }

    public int MaxAutoRepliesPerConversation { get; private set; }

    public IReadOnlyList<BotOption> Options { get; private set; }

    public void UpdateWhatsApp(
        bool enabled,
        string? message,
        int? maxAutoReplies,
        IEnumerable<BotOption>? options = null)
    {
        lock (_sync)
        {
            WhatsAppAutoReplyEnabled = enabled;
            WhatsAppAutoReplyMessage = string.IsNullOrWhiteSpace(message)
                ? DefaultMessage
                : message.Trim();
            MaxAutoRepliesPerConversation = maxAutoReplies is > 0 and <= 5
                ? maxAutoReplies.Value
                : 2;

            var plantillas = NormalizarOpciones(options);
            Options = plantillas.Count > 0 ? plantillas : CrearOpcionesDefault();
            GuardarEnArchivo();
        }
    }

    private static List<BotOption> CrearOpcionesDefault() =>
    [
        new("1", "Hablar con un asesor", "Listo, ya derivamos tu conversación con un asesor. En breve te atenderán.", true),
        new("2", "Información de productos", "Te ayudaremos con información de productos. Un asesor te compartirá los detalles disponibles.", true),
        new("3", "Cotización", "Perfecto, un asesor te solicitará los datos necesarios para preparar tu cotización.", true),
        new("4", "Horarios y ubicación", "Nuestro equipo te confirmará horarios, ubicación y disponibilidad por este mismo chat.", true)
    ];

    private static List<BotOption> NormalizarOpciones(IEnumerable<BotOption>? options)
    {
        if (options == null) return [];

        return options
            .Select(option => new BotOption(
                option.Key?.Trim() ?? string.Empty,
                option.Title?.Trim() ?? string.Empty,
                option.Response?.Trim() ?? string.Empty,
                option.DerivesToAdvisor))
            .Where(option =>
                !string.IsNullOrWhiteSpace(option.Key) &&
                !string.IsNullOrWhiteSpace(option.Title) &&
                !string.IsNullOrWhiteSpace(option.Response))
            .GroupBy(option => option.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(10)
            .ToList();
    }

    private void CargarDesdeArchivo()
    {
        if (!File.Exists(_settingsPath)) return;

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var data = JsonSerializer.Deserialize<BotSettingsFile>(json, _jsonOptions);
            if (data == null) return;

            WhatsAppAutoReplyEnabled = data.Enabled;
            WhatsAppAutoReplyMessage = string.IsNullOrWhiteSpace(data.Message)
                ? WhatsAppAutoReplyMessage
                : data.Message.Trim();
            MaxAutoRepliesPerConversation = data.MaxAutoReplies is > 0 and <= 5
                ? data.MaxAutoReplies.Value
                : MaxAutoRepliesPerConversation;

            var plantillas = NormalizarOpciones(data.Options);
            if (plantillas.Count > 0) Options = plantillas;
        }
        catch
        {
            Options = CrearOpcionesDefault();
        }
    }

    private void GuardarEnArchivo()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var data = new BotSettingsFile
        {
            Enabled = WhatsAppAutoReplyEnabled,
            Message = WhatsAppAutoReplyMessage,
            MaxAutoReplies = MaxAutoRepliesPerConversation,
            Options = Options.ToList()
        };

        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(data, _jsonOptions));
    }
}
