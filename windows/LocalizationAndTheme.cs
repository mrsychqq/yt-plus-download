using System.Text.Json;

namespace YTPlus;

internal enum AppLanguage
{
    Ru,
    En
}

internal enum AppTheme
{
    System,
    Light,
    Dark
}

internal readonly record struct LocalizedText(string Ru, string En)
{
    public string Resolve(AppLanguage lang) => lang == AppLanguage.En ? En : Ru;
}

internal sealed record AppPreferences(AppTheme Theme = AppTheme.System, AppLanguage Language = AppLanguage.Ru)
{
    private static string PreferencesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "YTPlus", "preferences.json");

    private sealed class StoredPreferences
    {
        public string? Theme { get; set; }
        public string? Language { get; set; }
    }

    public static AppPreferences Load()
    {
        try
        {
            if (!File.Exists(PreferencesPath))
                return new();
            var json = File.ReadAllText(PreferencesPath);
            var stored = JsonSerializer.Deserialize<StoredPreferences>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var theme = stored?.Theme?.ToLowerInvariant() switch
            {
                "light" => AppTheme.Light,
                "dark" => AppTheme.Dark,
                _ => AppTheme.System
            };
            var lang = stored?.Language?.ToLowerInvariant() switch
            {
                "en" => AppLanguage.En,
                _ => AppLanguage.Ru
            };
            return new(theme, lang);
        }
        catch
        {
            return new();
        }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(PreferencesPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var stored = new StoredPreferences
            {
                Theme = Theme switch
                {
                    AppTheme.Light => "light",
                    AppTheme.Dark => "dark",
                    _ => "system"
                },
                Language = Language == AppLanguage.En ? "en" : "ru"
            };
            File.WriteAllText(PreferencesPath, JsonSerializer.Serialize(stored));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

internal static class AppStrings
{
    public static string Tr(AppLanguage lang, string ru, string en) => lang == AppLanguage.En ? en : ru;

    public static string ShortCode(this AppLanguage lang) => lang == AppLanguage.En ? "EN" : "RU";

    public static AppLanguage Toggled(this AppLanguage lang) => lang == AppLanguage.En ? AppLanguage.Ru : AppLanguage.En;

    public static AppTheme Next(this AppTheme theme) => theme switch
    {
        AppTheme.System => AppTheme.Light,
        AppTheme.Light => AppTheme.Dark,
        _ => AppTheme.System
    };

    public static string ShortTitle(this AppTheme theme, AppLanguage lang) => (theme, lang) switch
    {
        (AppTheme.System, AppLanguage.Ru) => "Авто",
        (AppTheme.Light, AppLanguage.Ru) => "Светлая",
        (AppTheme.Dark, AppLanguage.Ru) => "Тёмная",
        (AppTheme.System, AppLanguage.En) => "Auto",
        (AppTheme.Light, AppLanguage.En) => "Light",
        (AppTheme.Dark, AppLanguage.En) => "Dark",
        _ => "Auto"
    };

    public static string FullTitle(this AppTheme theme, AppLanguage lang) => (theme, lang) switch
    {
        (AppTheme.System, AppLanguage.Ru) => "Системная",
        (AppTheme.Light, AppLanguage.Ru) => "Светлая",
        (AppTheme.Dark, AppLanguage.Ru) => "Тёмная",
        (AppTheme.System, AppLanguage.En) => "System",
        (AppTheme.Light, AppLanguage.En) => "Light",
        (AppTheme.Dark, AppLanguage.En) => "Dark",
        _ => "System"
    };

    public static string ThemeTooltip(AppTheme theme, AppLanguage lang) => lang == AppLanguage.En
        ? $"Theme: {theme.FullTitle(lang)} (click to switch)"
        : $"Тема: {theme.FullTitle(lang)} (нажмите, чтобы сменить)";

    public static string LanguageTooltip(AppLanguage lang) => lang == AppLanguage.En
        ? "Language: English (click to switch to Русский)"
        : "Язык: Русский (нажмите, чтобы переключить на English)";

    public static string ResolutionLabel(int height, AppLanguage lang) => (height, lang) switch
    {
        (0, AppLanguage.Ru) => "Максимальное",
        (0, AppLanguage.En) => "Best available",
        (4320, AppLanguage.Ru) => "До 4320p (8K)",
        (4320, AppLanguage.En) => "Up to 4320p (8K)",
        (2160, AppLanguage.Ru) => "До 2160p (4K)",
        (2160, AppLanguage.En) => "Up to 2160p (4K)",
        (_, AppLanguage.Ru) => $"До {height}p",
        (_, AppLanguage.En) => $"Up to {height}p",
        _ => $"Up to {height}p"
    };

    public static string PreparingFormat(string formatLabel, AppLanguage lang) =>
        lang == AppLanguage.En ? $"Preparing {formatLabel}…" : $"Подготавливаем {formatLabel}…";

    public static string Remaining(string eta, AppLanguage lang) =>
        lang == AppLanguage.En ? $"{eta} left" : $"Осталось {eta}";

    public static LocalizedText MissingOutputFile(string formatLabel) => new(
        $"Не удалось найти готовый файл {formatLabel} в выбранной папке.",
        $"Could not find the finished {formatLabel} file in the selected folder.");

    public static LocalizedText SignInOrPrivate(string platformName) => new(
        $"{platformName} требует вход в аккаунт или видео скрыто настройками приватности.",
        $"{platformName} requires sign-in or this video is private.");

    public static LocalizedText RejectedRequest(string platformName) => new(
        $"{platformName} временно отклонил запрос. Попробуйте скачать ещё раз.",
        $"{platformName} temporarily rejected the request. Please try again.");

    public static LocalizedText RateLimited(string platformName) => new(
        $"{platformName} временно ограничил запросы. Подождите немного и попробуйте снова.",
        $"{platformName} temporarily rate-limited requests. Wait a moment and try again.");

    public static LocalizedText ConnectionFailed(string platformName) => new(
        $"Не удалось подключиться к {platformName}. Проверьте соединение.",
        $"Could not connect to {platformName}. Check your internet connection.");
}
