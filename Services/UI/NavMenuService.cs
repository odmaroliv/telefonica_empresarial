public class NavMenuService
{
    private bool _isAdminMenuExpanded;

    public bool IsAdminMenuExpanded
    {
        get => _isAdminMenuExpanded;
        set
        {
            _isAdminMenuExpanded = value;
            StateChanged?.Invoke();
        }
    }

    public event Action StateChanged;
}