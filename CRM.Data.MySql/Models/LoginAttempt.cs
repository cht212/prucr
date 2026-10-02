namespace CRM.Data.Models;

public sealed class LoginAttempt
{
    public string Key { get; set; } = string.Empty;
    public int Failures { get; set; }
    public DateTime? BlockedUntilUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}
