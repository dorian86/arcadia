namespace Arcadia.Services;

/// <summary>Runs work on a dedicated STA thread (Shell COM objects such as IShellLink require it).</summary>
public static class Sta
{
    public static Task<T> Run<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        })
        { IsBackground = true, Name = "Arcadia STA worker" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    public static Task Run(Action work) => Run(() => { work(); return true; });
}

public static class LaunchHelper
{
    public static bool IsUri(string? target) =>
        !string.IsNullOrWhiteSpace(target)
        && (target.Contains("://", StringComparison.Ordinal) || target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase));
}
