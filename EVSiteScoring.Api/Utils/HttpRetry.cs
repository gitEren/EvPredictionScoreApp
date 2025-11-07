using System.Net;

namespace EVSiteScoring.Api.Utils;

public static class HttpRetry
{
    /// <summary>
    /// Executes the provided asynchronous factory with exponential backoff. Intended for provider calls where
    /// transient network errors are expected (e.g., Overpass 429/5xx responses).
    /// </summary>
    public static async Task<T> WithRetryAsync<T>(Func<Task<T>> action, ILogger logger, int maxAttempts = 3, int baseDelayMs = 500)
    {
        var attempt = 0;
        Exception? lastException = null;
        while (attempt < maxAttempts)
        {
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError)
            {
                lastException = ex;
                attempt++;
                var delay = TimeSpan.FromMilliseconds(baseDelayMs * Math.Pow(2, attempt));
                logger.LogWarning(ex, "Transient HTTP error (attempt {Attempt}/{MaxAttempts}). Waiting {Delay} before retrying.", attempt, maxAttempts, delay);
                await Task.Delay(delay).ConfigureAwait(false);
            }
            catch (TaskCanceledException ex)
            {
                lastException = ex;
                attempt++;
                var delay = TimeSpan.FromMilliseconds(baseDelayMs * Math.Pow(2, attempt));
                logger.LogWarning(ex, "HTTP timeout (attempt {Attempt}/{MaxAttempts}). Waiting {Delay} before retrying.", attempt, maxAttempts, delay);
                await Task.Delay(delay).ConfigureAwait(false);
            }
        }

        throw lastException ?? new InvalidOperationException("HTTP retry exhausted but no exception captured.");
    }
}
