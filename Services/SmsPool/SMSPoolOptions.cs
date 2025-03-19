public class SMSPoolOptions
{
    public string ApiKey { get; set; }
    public bool UseSandbox { get; set; } = false;
    public int DefaultTimeoutSeconds { get; set; } = 30;
}