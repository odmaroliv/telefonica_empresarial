// En carpeta Services
public interface INotificationService
{
    event Action<string, string> OnNotification;
    void ShowNotification(string message, string type = "info");
}

public class NotificationService : INotificationService
{
    public event Action<string, string> OnNotification;

    public void ShowNotification(string message, string type = "info")
    {
        OnNotification?.Invoke(message, type);
    }
}