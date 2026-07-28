namespace LibraTray.Core.Protocol;

public sealed class YeelightRequest
{
    public YeelightRequest(
        int id,
        string method,
        IEnumerable<object?>? parameters = null)
    {
        Id = id;
        Method = method;
        Parameters = parameters?.ToArray() ?? [];
    }

    public int Id { get; }

    public string Method { get; }

    public IReadOnlyList<object?> Parameters { get; }
}
