using System.Diagnostics;

namespace EVSiteScoring.Api.Utils;

public static class BrowserLauncher
{
    public static void Launch(string url, ILogger logger)
    {
        try
        {
            ProcessStartInfo psi;
            if (OperatingSystem.IsWindows())
            {
                psi = new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true };
            }
            else if (OperatingSystem.IsMacOS())
            {
                psi = new ProcessStartInfo("open", url);
            }
            else if (OperatingSystem.IsLinux())
            {
                psi = new ProcessStartInfo("xdg-open", url);
            }
            else
            {
                logger.LogInformation("Automatic browser launch not supported on this platform.");
                return;
            }

            psi.UseShellExecute = false;
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to launch browser automatically. Navigate manually to {Url}.", url);
        }
    }
}
