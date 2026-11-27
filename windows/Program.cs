namespace YTPlus;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowFatalError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                ShowFatalError(ex);
        };

        try
        {
            ApplicationConfiguration.Initialize();
            var lang = AppPreferences.Load().Language;
            using var instance = new Mutex(true, @"Local\YTPlus.Desktop", out var firstInstance);
            if (!firstInstance)
            {
                MessageBox.Show(
                    AppStrings.Tr(lang, "YT+ уже открыт.", "YT+ is already running."),
                    "YT+",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            Application.Run(new DownloadForm());
        }
        catch (Exception ex)
        {
            ShowFatalError(ex);
        }
    }

    private static void ShowFatalError(Exception ex)
    {
        try
        {
            var lang = AppPreferences.Load().Language;
            MessageBox.Show(
                AppStrings.Tr(lang,
                    $"Произошла непредвиденная ошибка при работе YT+:\n\n{ex.Message}",
                    $"An unexpected error occurred in YT+:\n\n{ex.Message}"),
                AppStrings.Tr(lang, "YT+ — Ошибка", "YT+ — Error"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            // Ignore secondary UI errors when shutting down.
        }
    }
}
