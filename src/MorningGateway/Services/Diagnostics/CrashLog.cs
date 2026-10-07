namespace MorningGateway.Services.Diagnostics;

/// <summary>
/// Appends unexpected exceptions to a small text file so crashes on a wall-mounted tablet can be
/// diagnosed after the fact (release builds have no debugger and no Debug output). Never throws.
/// </summary>
public static class CrashLog
{
    const long MaxBytes = 256 * 1024;
    static readonly object Gate = new();

    /// <summary>Where the log lives; set once at startup. Null disables file logging.</summary>
    public static string? FilePath { get; set; }

    public static void Write(string context, Exception? exception)
    {
        try
        {
            var entry = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {context}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}";
#if ANDROID
            Android.Util.Log.Error("MorningGateway", entry);
#endif
            var path = FilePath;
            if (path is null)
            {
                return;
            }

            lock (Gate)
            {
                // Keep the previous file as one generation of history rather than growing forever.
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                {
                    File.Move(path, path + ".old", overwrite: true);
                }

                File.AppendAllText(path, entry);
            }
        }
        catch (Exception)
        {
            // Logging is best effort.
        }
    }
}
