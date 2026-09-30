// In-memory stand-ins for the handful of MAUI statics the linked app code uses.
// Only the members the app actually calls are implemented.
namespace Microsoft.Maui.Storage
{
    public class Preferences
    {
        public static Preferences Default { get; } = new();
        readonly Dictionary<string, object> _values = new();

        public T Get<T>(string key, T defaultValue) =>
            _values.TryGetValue(key, out var v) ? (T)v : defaultValue;

        public void Set<T>(string key, T value) => _values[key] = value!;
        public void Clear() => _values.Clear();
    }

    public class SecureStorage
    {
        public static SecureStorage Default { get; } = new();
        readonly Dictionary<string, string> _values = new();

        public Task<string?> GetAsync(string key) =>
            Task.FromResult(_values.TryGetValue(key, out var v) ? v : null);

        public Task SetAsync(string key, string value) { _values[key] = value; return Task.CompletedTask; }
        public bool Remove(string key) => _values.Remove(key);
        public void RemoveAll() => _values.Clear();
    }
}

namespace Microsoft.Maui.Authentication
{
    public class WebAuthenticatorResult
    {
        public Dictionary<string, string> Properties { get; } = new();
    }

    public class WebAuthenticator
    {
        public static WebAuthenticator Default { get; } = new();

        /// <summary>Tests set this to play the part of the browser + redirect.</summary>
        public Func<Uri, Uri, WebAuthenticatorResult>? Handler { get; set; }

        public Task<WebAuthenticatorResult> AuthenticateAsync(Uri url, Uri callbackUrl) =>
            Task.FromResult(Handler?.Invoke(url, callbackUrl)
                ?? throw new InvalidOperationException("No WebAuthenticator.Handler set by the test."));
    }
}
