internal static class ComCallGuard
{
    public static T Call<T>(Func<T> action, string comInterface, Dictionary<string, object?>? context = null)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is not WorkerException)
        {
            throw new WorkerException(SwErrorDecoder.FromException(
                ex,
                comInterface,
                context ?? new Dictionary<string, object?>()));
        }
    }

    public static void CallVoid(Action action, string comInterface, Dictionary<string, object?>? context = null)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is not WorkerException)
        {
            throw new WorkerException(SwErrorDecoder.FromException(
                ex,
                comInterface,
                context ?? new Dictionary<string, object?>()));
        }
    }

    public static (T? Value, WorkerError? Warning) Try<T>(
        Func<T> action,
        string comInterface,
        Dictionary<string, object?>? context = null)
    {
        try
        {
            return (action(), null);
        }
        catch (Exception ex)
        {
            return (default, SwErrorDecoder.FromException(ex, comInterface, context ?? new Dictionary<string, object?>()));
        }
    }
}
