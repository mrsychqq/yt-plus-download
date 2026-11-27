using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace YTPlus;

internal sealed class DownloadForm : Form
{
    private readonly record struct UiPalette(
        bool IsDark,
        Color Ink,
        Color Muted,
        Color Surface,
        Color CardBg,
        Color CardBorder,
        Color Divider,
        Color TabBarBg,
        Color TabBarBorder,
        Color TabHover,
        Color SoftButtonBg,
        Color SoftButtonHover,
        Color DisabledAccent,
        Color ProgressTrack,
        Color Green,
        Color Amber,
        Color Red,
        Color LinkColor);

    private static readonly UiPalette LightPalette = new(
        IsDark: false,
        Ink: Color.FromArgb(20, 30, 48),
        Muted: Color.FromArgb(92, 107, 132),
        Surface: Color.FromArgb(244, 247, 252),
        CardBg: Color.White,
        CardBorder: Color.FromArgb(219, 227, 240),
        Divider: Color.FromArgb(234, 239, 247),
        TabBarBg: Color.FromArgb(228, 234, 245),
        TabBarBorder: Color.FromArgb(212, 221, 236),
        TabHover: Color.FromArgb(145, Color.White),
        SoftButtonBg: Color.FromArgb(241, 245, 251),
        SoftButtonHover: Color.FromArgb(228, 235, 247),
        DisabledAccent: Color.FromArgb(164, 179, 206),
        ProgressTrack: Color.FromArgb(226, 233, 245),
        Green: Color.FromArgb(21, 128, 61),
        Amber: Color.FromArgb(180, 83, 9),
        Red: Color.FromArgb(190, 24, 39),
        LinkColor: Color.FromArgb(22, 92, 224));

    private static readonly UiPalette DarkPalette = new(
        IsDark: true,
        Ink: Color.FromArgb(237, 242, 250),
        Muted: Color.FromArgb(148, 163, 189),
        Surface: Color.FromArgb(17, 22, 34),
        CardBg: Color.FromArgb(26, 34, 51),
        CardBorder: Color.FromArgb(42, 54, 79),
        Divider: Color.FromArgb(36, 47, 70),
        TabBarBg: Color.FromArgb(24, 32, 48),
        TabBarBorder: Color.FromArgb(40, 52, 76),
        TabHover: Color.FromArgb(38, Color.White),
        SoftButtonBg: Color.FromArgb(35, 45, 66),
        SoftButtonHover: Color.FromArgb(45, 58, 84),
        DisabledAccent: Color.FromArgb(58, 72, 99),
        ProgressTrack: Color.FromArgb(37, 49, 71),
        Green: Color.FromArgb(74, 222, 128),
        Amber: Color.FromArgb(251, 191, 36),
        Red: Color.FromArgb(251, 113, 133),
        LinkColor: Color.FromArgb(96, 165, 250));

    private enum StatusKind
    {
        Ready,
        MissingTools,
        Fetching,
        Downloading,
        DownloadingPhoto,
        WaitingForNetwork,
        Resuming,
        Finalizing,
        Cancelling,
        Cancelled,
        Saved,
        FileMissing,
        ExplorerFailed,
        Failed
    }

    private readonly TextBox link = new() { BorderStyle = BorderStyle.None };
    private readonly ComboBox formats = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
    private readonly ComboBox resolutions = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
    private readonly PillButton paste = new();
    private readonly PillButton clearLink = new() { Text = "×", Visible = false };
    private readonly PillButton choose = new();
    private readonly PillButton download = new() { Enabled = false };
    private readonly PillButton cancel = new() { Visible = false };
    private readonly PillButton reveal = new() { Visible = false };
    private readonly LinkLabel details = new() { AutoSize = true, Visible = false, Margin = new Padding(0, 4, 0, 0) };
    private readonly Label folderText = new() { AutoEllipsis = true, AutoSize = false };
    private readonly Label statusText = new() { AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
    private readonly Label progressText = new() { AutoEllipsis = true, AutoSize = false, Margin = new Padding(0, 2, 0, 2) };
    private readonly SmoothProgressBar progressBar = new() { Dock = DockStyle.Fill, Visible = false, Margin = new Padding(0, 6, 0, 6) };
    private readonly ToolTip tips = new();

    private readonly Font baseFont = new("Segoe UI", 10F);
    private readonly Font clearButtonFont = new("Segoe UI", 11F, FontStyle.Regular);
    private readonly Font headerTitleFont = new("Segoe UI", 17.5F, FontStyle.Bold);
    private readonly Font headerSubtitleFont = new("Segoe UI", 9.5F, FontStyle.Regular);
    private readonly Font headerPillFont = new("Segoe UI", 8.5F, FontStyle.Bold);
    private readonly Font badgeFont = new("Segoe UI", 8F, FontStyle.Bold);
    private readonly Font sectionLabelFont = new("Segoe UI", 9.5F, FontStyle.Bold);
    private readonly Font rowLabelFont = new("Segoe UI", 10F, FontStyle.Regular);
    private readonly Font tabFont = new("Segoe UI", 9.5F, FontStyle.Regular);
    private readonly Font tabActiveFont = new("Segoe UI", 9.5F, FontStyle.Bold);
    private readonly Font primaryButtonFont = new("Segoe UI", 10F, FontStyle.Bold);
    private readonly Font statusBoldFont = new("Segoe UI", 10F, FontStyle.Bold);
    private readonly Font smallMutedFont = new("Segoe UI", 8.75F, FontStyle.Regular);

    private readonly TableLayoutPanel layout;
    private readonly GradientHeaderPanel header;
    private readonly PlatformTabBar tabBar;
    private readonly TableLayoutPanel addressSection;
    private readonly Label linkLabel;
    private readonly RoundedPanel linkBox;
    private readonly RoundedPanel optionsCard;
    private readonly TableLayoutPanel optionsStack;
    private readonly TableLayoutPanel formatRow;
    private readonly Label formatLabel;
    private readonly TableLayoutPanel resolutionRow;
    private readonly Label resolutionLabel;
    private readonly Control resolutionDivider;
    private readonly Control folderDivider;
    private readonly TableLayoutPanel folderRow;
    private readonly TableLayoutPanel folderInfo;
    private readonly Label folderLabel;
    private readonly RoundedPanel statusCard;
    private readonly TableLayoutPanel statusLayout;
    private readonly TableLayoutPanel actionsPanel;
    private readonly FlowLayoutPanel rightButtons;
    private readonly Dictionary<VideoPlatform, Bitmap> headerArtworks = new();
    private readonly DownloadTools tools = DownloadTools.Bundled();
    private readonly string settingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "YTPlus", "folder.txt");

    private AppPreferences preferences;
    private UiPalette theme;
    private VideoPlatform selectedPlatform = VideoPlatform.YouTube;
    private string folder;
    private string? savedFile;
    private bool savedIsPhoto;
    private string? errorDetail;
    private string? videoTitle;
    private bool showingError;
    private StatusKind statusKind = StatusKind.Ready;
    private LocalizedText? statusErrorMessage;
    private bool statusIsAudio;
    private double? statusFraction;
    private string? statusSpeed;
    private string? statusRemaining;
    private string? statusFormatLabel;
    private CancellationTokenSource? active;
    private bool closeWhenFinished;

    private AppLanguage Lang => preferences.Language;

    public DownloadForm()
    {
        preferences = AppPreferences.Load();
        theme = ResolvePalette(preferences.Theme);

        SuspendLayout();

        Text = "YT+";
        Font = baseFont;
        ForeColor = theme.Ink;
        BackColor = theme.Surface;
        DoubleBuffered = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 590);

        LoadWindowIcon();
        LoadHeaderBitmaps();

        foreach (var format in DownloadProfiles.Shared.Formats)
            formats.Items.Add(new Choice<MediaFormat>(format.Label, format));
        PopulateResolutions(defaultHeight: 1080);
        formats.SelectedIndex = Math.Max(0, Array.FindIndex(DownloadProfiles.Shared.Formats, item => item.Extension == "mp4"));

        folder = LoadFolder();
        folderText.Text = folder;
        folderText.Font = smallMutedFont;
        tips.SetToolTip(folderText, folder);

        layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(22, 18, 22, 20)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Header
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1: Platform tabs
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 2: Link
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 3: Options card
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // 4: Status card
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 5: Actions

        // 0: Single-surface custom-painted Gradient Hero Header with Theme & Language pills
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = version is null ? "v1.3.0" : $"v{version.Major}.{version.Minor}.{version.Build}";
        header = new GradientHeaderPanel
        {
            Dock = DockStyle.Fill,
            Height = 94,
            CornerRadius = 16,
            TitleFont = headerTitleFont,
            SubtitleFont = headerSubtitleFont,
            PillFont = headerPillFont,
            BadgeFont = badgeFont,
            BadgeText = versionText,
            ToolTipControl = tips,
            Margin = new Padding(0, 0, 0, 14)
        };
        header.ThemeClicked += (_, _) => CycleTheme();
        header.LanguageClicked += (_, _) => ToggleLanguage();
        layout.Controls.Add(header, 0, 0);

        // 1: Single-surface segmented tab bar (YouTube | TikTok | Pinterest)
        tabBar = new PlatformTabBar
        {
            Dock = DockStyle.Fill,
            Height = 42,
            NormalFont = tabFont,
            ActiveFont = tabActiveFont,
            Margin = new Padding(0, 0, 0, 14)
        };
        tabBar.PlatformSelected += (_, platform) => SelectPlatform(platform, clearError: true);
        layout.Controls.Add(tabBar, 0, 1);

        // 2: Link input card + Paste button
        addressSection = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 14)
        };
        addressSection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        addressSection.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        addressSection.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        addressSection.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        linkLabel = new Label
        {
            Font = sectionLabelFont,
            AutoSize = true,
            Margin = new Padding(2, 0, 0, 6)
        };
        addressSection.Controls.Add(linkLabel, 0, 0);

        linkBox = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Height = 38,
            CornerRadius = 10,
            Margin = new Padding(0, 0, 8, 0)
        };
        link.Font = Font;

        clearLink.Size = new Size(24, 24);
        clearLink.CornerRadius = 6;
        clearLink.ShowBorder = false;
        clearLink.Font = clearButtonFont;

        linkBox.Controls.Add(link);
        linkBox.Controls.Add(clearLink);
        linkBox.Layout += (_, _) => LayoutLinkBox();
        linkBox.SizeChanged += (_, _) => LayoutLinkBox();

        paste.Size = new Size(106, 38);
        paste.CornerRadius = 9;
        paste.ShowBorder = true;
        paste.Margin = new Padding(0);
        addressSection.Controls.Add(linkBox, 0, 1);
        addressSection.Controls.Add(paste, 1, 1);
        layout.Controls.Add(addressSection, 0, 2);

        // 3: Unified rounded options card (Format / Resolution / Folder)
        optionsCard = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            CornerRadius = 12,
            Padding = new Padding(16, 6, 16, 6),
            Margin = new Padding(0, 0, 0, 14)
        };
        optionsStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 5,
            Margin = new Padding(0)
        };
        optionsStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (var i = 0; i < 5; i++)
            optionsStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        formats.Width = 210;
        resolutions.Width = 210;
        choose.Size = new Size(104, 32);
        choose.CornerRadius = 9;
        choose.ShowBorder = true;
        choose.Margin = new Padding(12, 0, 0, 0);

        (formatRow, formatLabel) = ComboOptionRow(formats);
        optionsStack.Controls.Add(formatRow, 0, 0);
        resolutionDivider = HorizontalDivider();
        optionsStack.Controls.Add(resolutionDivider, 0, 1);
        (resolutionRow, resolutionLabel) = ComboOptionRow(resolutions);
        optionsStack.Controls.Add(resolutionRow, 0, 2);
        folderDivider = HorizontalDivider();
        optionsStack.Controls.Add(folderDivider, 0, 3);
        (folderRow, folderInfo, folderLabel) = FolderOptionRow();
        optionsStack.Controls.Add(folderRow, 0, 4);
        optionsCard.Controls.Add(optionsStack);
        layout.Controls.Add(optionsCard, 0, 3);

        // 4: Status card
        statusCard = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            CornerRadius = 12,
            Padding = new Padding(16, 12, 16, 12),
            Margin = new Padding(0, 0, 0, 14)
        };
        statusLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4
        };
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        statusText.Font = statusBoldFont;
        progressBar.Height = 8;
        progressText.Dock = DockStyle.Fill;
        progressText.Font = smallMutedFont;
        progressText.Height = SmallTextRowHeight();

        statusLayout.Controls.Add(statusText, 0, 0);
        statusLayout.Controls.Add(progressBar, 0, 1);
        statusLayout.Controls.Add(progressText, 0, 2);
        statusLayout.Controls.Add(details, 0, 3);
        statusCard.Controls.Add(statusLayout);
        statusCard.SizeChanged += (_, _) => UpdateStatusWrapWidth();
        layout.Controls.Add(statusCard, 0, 4);

        // 5: Bottom action bar
        actionsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0)
        };
        actionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        actionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        reveal.Size = new Size(164, 38);
        reveal.CornerRadius = 9;
        reveal.ShowBorder = true;
        reveal.Margin = new Padding(0);
        actionsPanel.Controls.Add(reveal, 0, 0);

        download.Size = new Size(134, 38);
        download.CornerRadius = 10;
        download.ShowBorder = false;
        download.Font = primaryButtonFont;
        download.ForeColor = Color.White;
        download.Margin = new Padding(0);

        cancel.Size = new Size(134, 38);
        cancel.CornerRadius = 9;
        cancel.ShowBorder = true;
        cancel.Margin = new Padding(0);

        rightButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0)
        };
        rightButtons.Controls.Add(download);
        rightButtons.Controls.Add(cancel);
        actionsPanel.Controls.Add(rightButtons, 2, 0);
        layout.Controls.Add(actionsPanel, 0, 5);

        Controls.Add(layout);

        Load += (_, _) => MinimumSize = LogicalToDeviceUnits(new Size(560, 560));
        HandleCreated += (_, _) =>
        {
            ApplyWindowChrome();
            ApplyNativeControlThemes();
        };
        formats.HandleCreated += (_, _) => ApplyNativeControlTheme(formats, "DarkMode_CFD");
        resolutions.HandleCreated += (_, _) => ApplyNativeControlTheme(resolutions, "DarkMode_CFD");
        Shown += (_, _) =>
        {
            var smallHeight = SmallTextRowHeight();
            folderText.Height = smallHeight;
            progressText.Height = smallHeight;
            LayoutLinkBox();
            UpdateStatusWrapWidth();
            link.Focus();
        };
        link.GotFocus += (_, _) => UpdateLinkFocusBorder(true);
        link.LostFocus += (_, _) => UpdateLinkFocusBorder(false);
        link.TextChanged += (_, _) =>
        {
            ClearErrorIfIdle();
            if (active is null && DownloadEngine.ParseMediaLink(link.Text) is { } detected && detected.Platform != selectedPlatform)
                SelectPlatform(detected.Platform, clearError: false);
            UpdateAvailability();
        };
        clearLink.Click += (_, _) =>
        {
            link.Text = "";
            link.Focus();
        };
        formats.SelectedIndexChanged += (_, _) => UpdateFormat();
        paste.Click += (_, _) => PasteFromClipboard();
        choose.Click += (_, _) => ChooseFolder();
        download.Click += async (_, _) => await StartDownloadAsync();
        cancel.Click += (_, _) => CancelDownload();
        reveal.Click += (_, _) => RevealFile();
        details.LinkClicked += (_, _) => ShowErrorDetails();

        if (OperatingSystem.IsWindows())
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        statusKind = tools.Installed ? StatusKind.Ready : StatusKind.MissingTools;
        showingError = !tools.Installed;

        ApplyVisualTheme();
        ApplyLanguage();
        UpdateFormat();

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
        PerformLayout();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (OperatingSystem.IsWindows())
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            foreach (var bitmap in headerArtworks.Values)
                bitmap.Dispose();
            headerArtworks.Clear();
            baseFont.Dispose();
            clearButtonFont.Dispose();
            headerTitleFont.Dispose();
            headerSubtitleFont.Dispose();
            headerPillFont.Dispose();
            badgeFont.Dispose();
            sectionLabelFont.Dispose();
            rowLabelFont.Dispose();
            tabFont.Dispose();
            tabActiveFont.Dispose();
            primaryButtonFont.Dispose();
            statusBoldFont.Dispose();
            smallMutedFont.Dispose();
            tips.Dispose();
        }
        base.Dispose(disposing);
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (preferences.Theme != AppTheme.System || IsDisposed || !IsHandleCreated) return;
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle))
            return;
        try
        {
            BeginInvoke(() =>
            {
                if (IsDisposed || preferences.Theme != AppTheme.System) return;
                var next = ResolvePalette(preferences.Theme);
                if (next.IsDark == theme.IsDark) return;
                theme = next;
                ApplyVisualTheme();
            });
        }
        catch (InvalidOperationException) { }
    }

    private static UiPalette ResolvePalette(AppTheme mode) => mode switch
    {
        AppTheme.Light => LightPalette,
        AppTheme.Dark => DarkPalette,
        _ => IsSystemDarkMode() ? DarkPalette : LightPalette
    };

    private static bool IsSystemDarkMode()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int value)
                return value == 0;
        }
        catch
        {
            // Fall back to light mode if registry key is unavailable.
        }
        return false;
    }

    private void CycleTheme()
    {
        preferences = preferences with { Theme = preferences.Theme.Next() };
        preferences.Save();
        theme = ResolvePalette(preferences.Theme);
        ApplyVisualTheme();
        UpdateHeaderControls();
    }

    private void ToggleLanguage()
    {
        preferences = preferences with { Language = preferences.Language.Toggled() };
        preferences.Save();
        ApplyLanguage();
    }

    private void PopulateResolutions(int? defaultHeight = null)
    {
        var selectedHeight = defaultHeight ?? (resolutions.SelectedItem as Choice<int>)?.Value ?? 1080;
        resolutions.BeginUpdate();
        resolutions.Items.Clear();
        foreach (var height in DownloadProfiles.Shared.Resolutions)
            resolutions.Items.Add(new Choice<int>(AppStrings.ResolutionLabel(height, Lang), height));
        var idx = Array.IndexOf(DownloadProfiles.Shared.Resolutions, selectedHeight);
        resolutions.SelectedIndex = Math.Max(0, idx);
        resolutions.EndUpdate();
    }

    private void ApplyVisualTheme()
    {
        ForeColor = theme.Ink;
        BackColor = theme.Surface;
        layout.BackColor = theme.Surface;
        addressSection.BackColor = theme.Surface;
        actionsPanel.BackColor = theme.Surface;
        rightButtons.BackColor = theme.Surface;

        header.BackdropColor = theme.Surface;
        header.Invalidate();

        tabBar.BackdropColor = theme.Surface;
        tabBar.TrackColor = theme.TabBarBg;
        tabBar.BorderColor = theme.TabBarBorder;
        tabBar.HoverColor = theme.TabHover;
        tabBar.InactiveTextColor = theme.Ink;
        tabBar.MutedTextColor = theme.Muted;
        tabBar.DisabledColor = theme.DisabledAccent;
        tabBar.Invalidate();

        linkLabel.ForeColor = theme.Muted;
        linkLabel.BackColor = theme.Surface;

        linkBox.FillColor = theme.CardBg;
        linkBox.BackdropColor = theme.Surface;
        linkBox.Invalidate();

        link.BackColor = theme.CardBg;
        link.ForeColor = theme.Ink;

        clearLink.BackdropColor = theme.CardBg;
        clearLink.NormalBackColor = theme.CardBg;
        clearLink.HoverBackColor = theme.SoftButtonBg;
        clearLink.ForeColor = theme.Muted;
        clearLink.Invalidate();

        StyleSecondaryButton(paste, backdrop: theme.Surface);
        StyleSecondaryButton(choose, backdrop: theme.CardBg);
        StyleSecondaryButton(reveal, backdrop: theme.Surface);
        StyleSecondaryButton(cancel, backdrop: theme.Surface);
        download.BackdropColor = theme.Surface;

        optionsCard.FillColor = theme.CardBg;
        optionsCard.BackdropColor = theme.Surface;
        optionsCard.BorderColor = theme.CardBorder;
        optionsCard.Invalidate();
        optionsStack.BackColor = theme.CardBg;
        formatRow.BackColor = theme.CardBg;
        formatLabel.BackColor = theme.CardBg;
        formatLabel.ForeColor = theme.Ink;
        resolutionRow.BackColor = theme.CardBg;
        resolutionLabel.BackColor = theme.CardBg;
        resolutionLabel.ForeColor = theme.Ink;
        resolutionDivider.BackColor = theme.Divider;
        folderDivider.BackColor = theme.Divider;
        folderRow.BackColor = theme.CardBg;
        folderInfo.BackColor = theme.CardBg;
        folderLabel.BackColor = theme.CardBg;
        folderLabel.ForeColor = theme.Ink;
        folderText.BackColor = theme.CardBg;
        folderText.ForeColor = theme.Muted;

        formats.BackColor = theme.CardBg;
        formats.ForeColor = theme.Ink;
        resolutions.BackColor = theme.CardBg;
        resolutions.ForeColor = theme.Ink;

        statusCard.FillColor = theme.CardBg;
        statusCard.BackdropColor = theme.Surface;
        statusCard.BorderColor = theme.CardBorder;
        statusCard.Invalidate();
        statusLayout.BackColor = theme.CardBg;
        statusText.BackColor = theme.CardBg;
        progressText.BackColor = theme.CardBg;
        progressText.ForeColor = theme.Muted;
        progressBar.BackColor = theme.CardBg;
        progressBar.TrackColor = theme.ProgressTrack;
        details.BackColor = theme.CardBg;
        details.LinkColor = theme.LinkColor;
        details.ActiveLinkColor = theme.LinkColor;
        details.VisitedLinkColor = theme.LinkColor;

        ApplyPlatformTheme();
        RenderCurrentStatus();
        UpdateAvailability();
        ApplyWindowChrome();
        ApplyNativeControlThemes();
    }

    private void ApplyLanguage()
    {
        link.AccessibleName = AppStrings.Tr(Lang, "Ссылка на видео", "Video link");
        formats.AccessibleName = AppStrings.Tr(Lang, "Формат файла", "File format");
        resolutions.AccessibleName = AppStrings.Tr(Lang, "Разрешение", "Resolution");
        tabBar.AccessibleName = AppStrings.Tr(Lang, "Выбор платформы", "Platform selector");
        clearLink.AccessibleName = AppStrings.Tr(Lang, "Очистить поле", "Clear");
        tips.SetToolTip(clearLink, AppStrings.Tr(Lang, "Очистить поле", "Clear"));

        linkLabel.Text = AppStrings.Tr(Lang, "Ссылка", "Link");
        paste.Text = AppStrings.Tr(Lang, "Вставить", "Paste");
        tips.SetToolTip(paste, AppStrings.Tr(Lang, "Вставить ссылку из буфера обмена", "Paste link from clipboard"));
        formatLabel.Text = AppStrings.Tr(Lang, "Формат файла", "File format");
        resolutionLabel.Text = AppStrings.Tr(Lang, "Разрешение", "Resolution");
        folderLabel.Text = AppStrings.Tr(Lang, "Папка", "Folder");
        choose.Text = AppStrings.Tr(Lang, "Выбрать…", "Choose…");
        download.Text = AppStrings.Tr(Lang, "Скачать", "Download");
        cancel.Text = statusKind == StatusKind.Cancelling
            ? AppStrings.Tr(Lang, "Отмена", "Cancelling")
            : AppStrings.Tr(Lang, "Отменить", "Cancel");
        reveal.Text = AppStrings.Tr(Lang, "Показать в папке", "Show in Folder");
        details.Text = AppStrings.Tr(Lang, "Подробности…", "Details…");

        PopulateResolutions();
        UpdateHeaderControls();
        ApplyPlatformTheme();
        RenderCurrentStatus();
    }

    private void UpdateHeaderControls()
    {
        header.ThemeMode = preferences.Theme;
        header.ThemeLabel = preferences.Theme.ShortTitle(Lang);
        header.LanguageLabel = Lang.ShortCode();
        header.ThemeTooltip = AppStrings.ThemeTooltip(preferences.Theme, Lang);
        header.LanguageTooltip = AppStrings.LanguageTooltip(Lang);
        header.RefreshHoveredToolTip();
        header.Invalidate();
    }

    private void LayoutLinkBox()
    {
        var padX = LogicalToDeviceUnits(12);
        var padRight = LogicalToDeviceUnits(6);
        var btnSize = LogicalToDeviceUnits(24);
        var clearVisible = clearLink.Visible;

        clearLink.Bounds = new Rectangle(
            Math.Max(padX, linkBox.ClientSize.Width - padRight - btnSize),
            Math.Max(0, (linkBox.ClientSize.Height - btnSize) / 2),
            btnSize,
            btnSize);

        var rightLimit = clearVisible ? clearLink.Left - LogicalToDeviceUnits(4) : linkBox.ClientSize.Width - padX;
        var textWidth = Math.Max(40, rightLimit - padX);
        var textHeight = link.PreferredHeight;
        link.Bounds = new Rectangle(
            padX,
            Math.Max(2, (linkBox.ClientSize.Height - textHeight) / 2),
            textWidth,
            textHeight);
    }

    private int SmallTextRowHeight() =>
        Math.Max(LogicalToDeviceUnits(19), TextRenderer.MeasureText("Ag", smallMutedFont).Height + LogicalToDeviceUnits(3));

    private void ApplyWindowChrome() => ApplyWindowChrome(Handle, theme);

    private static void ApplyWindowChrome(IntPtr hwnd, UiPalette palette)
    {
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero) return;
        try
        {
            int dark = palette.IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)) != 0)
                _ = DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));

            int captionColor = palette.Surface.R | (palette.Surface.G << 8) | (palette.Surface.B << 16);
            var captionResult = DwmSetWindowAttribute(hwnd, 35, ref captionColor, sizeof(int));
            int textColor = palette.Ink.R | (palette.Ink.G << 8) | (palette.Ink.B << 16);
            _ = DwmSetWindowAttribute(hwnd, 36, ref textColor, sizeof(int));

            // On Windows 10 (where DWMWA_CAPTION_COLOR is unsupported), force an immediate
            // non-client title bar repaint when switching light/dark mode dynamically.
            if (captionResult != 0 && IsWindowVisible(hwnd))
            {
                const int wmNcActivate = 0x0086;
                const uint swpFlags = 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020; // NOSIZE | NOMOVE | NOZORDER | NOACTIVATE | FRAMECHANGED
                var active = GetActiveWindow() == hwnd;
                _ = SendMessage(hwnd, wmNcActivate, active ? IntPtr.Zero : (IntPtr)1, IntPtr.Zero);
                _ = SendMessage(hwnd, wmNcActivate, active ? (IntPtr)1 : IntPtr.Zero, IntPtr.Zero);
                _ = SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, swpFlags);
            }
        }
        catch
        {
            // Ignored on older Windows builds where these DWM attributes are unavailable.
        }
    }

    private void ApplyNativeControlThemes()
    {
        ApplyNativeControlTheme(formats, "DarkMode_CFD");
        ApplyNativeControlTheme(resolutions, "DarkMode_CFD");
    }

    private void ApplyNativeControlTheme(Control control, string darkThemeName, bool? darkOverride = null)
    {
        if (!OperatingSystem.IsWindows() || !control.IsHandleCreated) return;
        var isDark = darkOverride ?? theme.IsDark;
        try
        {
            _ = SetWindowTheme(control.Handle, isDark ? darkThemeName : "Explorer", null);
        }
        catch
        {
            // Safe fallback if uxtheme dark sub-app theme is unavailable on older Windows 10 builds.
        }
    }

    private void StyleSecondaryButton(PillButton button, Color backdrop)
    {
        button.BackdropColor = backdrop;
        button.BorderColor = theme.CardBorder;
        button.NormalBackColor = theme.SoftButtonBg;
        button.HoverBackColor = theme.SoftButtonHover;
        button.ForeColor = theme.Ink;
        button.Invalidate();
    }

    private static Control HorizontalDivider() => new Panel
    {
        Dock = DockStyle.Fill,
        Height = 1,
        Margin = new Padding(0)
    };

    private (TableLayoutPanel Row, Label Label) ComboOptionRow(ComboBox combo)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0, 9, 0, 9)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var label = new Label
        {
            Font = rowLabelFont,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 12, 0)
        };
        row.Controls.Add(label, 0, 0);

        combo.Anchor = AnchorStyles.Right;
        combo.Margin = new Padding(0);
        row.Controls.Add(combo, 1, 0);
        return (row, label);
    }

    private (TableLayoutPanel Row, TableLayoutPanel Info, Label Label) FolderOptionRow()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0, 9, 0, 9)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var info = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0)
        };
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        info.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        info.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var label = new Label
        {
            Font = rowLabelFont,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 2)
        };
        info.Controls.Add(label, 0, 0);

        folderText.Dock = DockStyle.Fill;
        folderText.Height = SmallTextRowHeight();
        folderText.Margin = new Padding(0);
        info.Controls.Add(folderText, 0, 1);

        choose.Anchor = AnchorStyles.Right;
        row.Controls.Add(info, 0, 0);
        row.Controls.Add(choose, 1, 0);
        return (row, info, label);
    }

    private (Color Start, Color End, Color Glow, Color Subtitle, Color Accent, Color AccentHover, Color AccentText) PlatformPalette(VideoPlatform platform)
    {
        var dark = theme.IsDark;
        return platform switch
        {
            VideoPlatform.YouTube => (
                Color.FromArgb(13, 28, 71),
                Color.FromArgb(24, 76, 188),
                Color.FromArgb(0, 198, 255),
                Color.FromArgb(204, 226, 255),
                dark ? Color.FromArgb(47, 118, 246) : Color.FromArgb(22, 92, 224),
                dark ? Color.FromArgb(35, 100, 224) : Color.FromArgb(16, 76, 194),
                dark ? Color.FromArgb(96, 165, 250) : Color.FromArgb(22, 92, 224)
            ),
            VideoPlatform.TikTok => (
                Color.FromArgb(15, 25, 40),
                Color.FromArgb(17, 84, 107),
                Color.FromArgb(37, 240, 232),
                Color.FromArgb(182, 243, 246),
                dark ? Color.FromArgb(18, 152, 160) : Color.FromArgb(14, 124, 134),
                dark ? Color.FromArgb(14, 128, 136) : Color.FromArgb(11, 102, 111),
                dark ? Color.FromArgb(45, 212, 191) : Color.FromArgb(14, 124, 134)
            ),
            VideoPlatform.Pinterest => (
                Color.FromArgb(70, 10, 26),
                Color.FromArgb(166, 18, 53),
                Color.FromArgb(255, 92, 122),
                Color.FromArgb(255, 208, 218),
                dark ? Color.FromArgb(220, 42, 76) : Color.FromArgb(190, 28, 60),
                dark ? Color.FromArgb(192, 32, 62) : Color.FromArgb(158, 20, 48),
                dark ? Color.FromArgb(251, 113, 133) : Color.FromArgb(190, 28, 60)
            ),
            _ => (
                Color.FromArgb(13, 28, 71),
                Color.FromArgb(24, 76, 188),
                Color.FromArgb(0, 198, 255),
                Color.FromArgb(204, 226, 255),
                dark ? Color.FromArgb(47, 118, 246) : Color.FromArgb(22, 92, 224),
                dark ? Color.FromArgb(35, 100, 224) : Color.FromArgb(16, 76, 194),
                dark ? Color.FromArgb(96, 165, 250) : Color.FromArgb(22, 92, 224)
            )
        };
    }

    private void SelectPlatform(VideoPlatform platform, bool clearError)
    {
        if (active is not null) return;
        selectedPlatform = platform;
        ApplyPlatformTheme();
        if (clearError)
            ClearErrorIfIdle();
        UpdateAvailability();
    }

    private void UpdateLinkFocusBorder(bool focused)
    {
        var palette = PlatformPalette(selectedPlatform);
        linkBox.BorderColor = focused ? palette.Accent : theme.CardBorder;
        linkBox.Invalidate();
    }

    private void ApplyPlatformTheme()
    {
        var palette = PlatformPalette(selectedPlatform);
        header.StartColor = palette.Start;
        header.EndColor = palette.End;
        header.GlowColor = palette.Glow;
        header.SubtitleColor = palette.Subtitle;
        header.TitleText = selectedPlatform.HeaderTitle(Lang);
        header.SubtitleText = selectedPlatform.HeaderSubtitle(Lang);
        header.IconBitmap = headerArtworks.GetValueOrDefault(selectedPlatform);
        header.Invalidate();

        tabBar.SelectedPlatform = selectedPlatform;
        tabBar.ActiveColor = palette.Accent;
        tabBar.Invalidate();

        link.PlaceholderText = selectedPlatform.Placeholder(Lang);
        progressBar.BarColor = statusKind == StatusKind.WaitingForNetwork ? theme.Amber : palette.Accent;
        progressBar.Invalidate();
        UpdateLinkFocusBorder(link.Focused);
    }

    private void LoadWindowIcon()
    {
        try
        {
            using var stream = typeof(DownloadForm).Assembly.GetManifestResourceStream("YTPlus.AppIcon.ico");
            if (stream is not null)
                Icon = new Icon(stream);
        }
        catch
        {
            // Keep default form icon if resource decoding fails.
        }
    }

    private void LoadHeaderBitmaps()
    {
        var map = new (VideoPlatform Platform, string Resource)[]
        {
            (VideoPlatform.YouTube, "YTPlus.HeaderIcon.YouTube.png"),
            (VideoPlatform.TikTok, "YTPlus.HeaderIcon.TikTok.png"),
            (VideoPlatform.Pinterest, "YTPlus.HeaderIcon.Pinterest.png")
        };
        var assembly = typeof(DownloadForm).Assembly;
        foreach (var (platform, resource) in map)
        {
            try
            {
                using var stream = assembly.GetManifestResourceStream(resource);
                if (stream is null) continue;
                using var decoded = new Bitmap(stream);
                headerArtworks[platform] = new Bitmap(decoded);
            }
            catch
            {
                // Skip icon if decoding fails.
            }
        }
    }

    private void UpdateStatusWrapWidth()
    {
        var width = Math.Max(200, statusCard.ClientSize.Width - statusCard.Padding.Horizontal - 4);
        statusText.MaximumSize = new Size(width, 0);
    }

    private void SetStatusState(StatusKind kind, LocalizedText? errorMessage = null)
    {
        statusKind = kind;
        statusErrorMessage = errorMessage;
        showingError = kind is StatusKind.MissingTools or StatusKind.FileMissing or StatusKind.ExplorerFailed or StatusKind.Failed;
        RenderCurrentStatus();
    }

    private void RenderCurrentStatus()
    {
        var palette = PlatformPalette(selectedPlatform);
        switch (statusKind)
        {
            case StatusKind.Ready:
                statusText.Text = AppStrings.Tr(Lang, "Готово к скачиванию", "Ready to download");
                statusText.ForeColor = theme.Ink;
                break;
            case StatusKind.MissingTools:
                statusText.Text = AppStrings.Tr(Lang,
                    "Файлы загрузчика отсутствуют. Запустите установщик YT+.",
                    "Downloader files are missing. Please run the YT+ installer.");
                statusText.ForeColor = theme.Red;
                break;
            case StatusKind.Fetching:
                statusText.Text = AppStrings.Tr(Lang, "Получаем данные…", "Fetching info…");
                statusText.ForeColor = palette.AccentText;
                break;
            case StatusKind.Downloading:
                var phase = statusIsAudio
                    ? AppStrings.Tr(Lang, "Скачиваем звук", "Downloading audio")
                    : AppStrings.Tr(Lang, "Скачиваем видео", "Downloading video");
                statusText.Text = statusFraction is double fraction ? $"{phase} · {fraction:P0}" : phase;
                statusText.ForeColor = palette.AccentText;
                var remaining = statusRemaining is null ? null : AppStrings.Remaining(statusRemaining, Lang);
                progressText.Text = string.Join(" · ", new[] { videoTitle, statusSpeed, remaining }.OfType<string>());
                break;
            case StatusKind.DownloadingPhoto:
                statusText.Text = AppStrings.Tr(Lang, "Скачиваем фото в оригинале…", "Downloading original photo…");
                statusText.ForeColor = palette.AccentText;
                progressText.Text = videoTitle ?? "";
                break;
            case StatusKind.WaitingForNetwork:
                statusText.Text = AppStrings.Tr(Lang,
                    "Соединение потеряно. Ждём сеть и продолжим…",
                    "Connection lost. Waiting for network to resume…");
                statusText.ForeColor = theme.Amber;
                progressText.Text = videoTitle ?? "";
                break;
            case StatusKind.Resuming:
                statusText.Text = AppStrings.Tr(Lang,
                    "Соединение восстановлено. Продолжаем загрузку…",
                    "Connection restored. Resuming download…");
                statusText.ForeColor = palette.AccentText;
                progressText.Text = videoTitle ?? "";
                break;
            case StatusKind.Finalizing:
                statusText.Text = AppStrings.PreparingFormat(statusFormatLabel ?? "", Lang);
                statusText.ForeColor = palette.AccentText;
                progressText.Text = videoTitle ?? "";
                break;
            case StatusKind.Cancelling:
                statusText.Text = AppStrings.Tr(Lang, "Останавливаем загрузку…", "Stopping download…");
                statusText.ForeColor = theme.Muted;
                progressText.Text = videoTitle ?? "";
                break;
            case StatusKind.Cancelled:
                statusText.Text = AppStrings.Tr(Lang, "Загрузка отменена", "Download cancelled");
                statusText.ForeColor = theme.Muted;
                break;
            case StatusKind.Saved:
                statusText.Text = savedIsPhoto
                    ? AppStrings.Tr(Lang, "Фото сохранено", "Photo saved")
                    : AppStrings.Tr(Lang, "Файл сохранён", "File saved");
                statusText.ForeColor = theme.Green;
                break;
            case StatusKind.FileMissing:
                statusText.Text = AppStrings.Tr(Lang,
                    "Готовый файл перемещён или удалён.",
                    "The finished file was moved or deleted.");
                statusText.ForeColor = theme.Red;
                break;
            case StatusKind.ExplorerFailed:
                statusText.Text = AppStrings.Tr(Lang,
                    "Не удалось открыть файл в Проводнике.",
                    "Could not open the file in Explorer.");
                statusText.ForeColor = theme.Red;
                break;
            case StatusKind.Failed:
                statusText.Text = statusErrorMessage?.Resolve(Lang)
                    ?? AppStrings.Tr(Lang, "Произошла ошибка при скачивании.", "An error occurred while downloading.");
                statusText.ForeColor = theme.Red;
                progressText.Text = "";
                tips.SetToolTip(progressText, "");
                break;
        }
    }

    private void ClearErrorIfIdle()
    {
        if (active is not null || !showingError || !tools.Installed) return;
        errorDetail = null;
        details.Visible = false;
        SetStatusState(StatusKind.Ready);
    }

    private void PasteFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsText())
                link.Text = Clipboard.GetText().Trim();
        }
        catch (ExternalException)
        {
            // Clipboard temporarily locked by another application.
        }
        link.Focus();
    }

    private void UpdateFormat()
    {
        var audio = (formats.SelectedItem as Choice<MediaFormat>)?.Value.IsAudio == true;
        resolutionRow.Visible = !audio;
        resolutionDivider.Visible = !audio;
        UpdateAvailability();
    }

    private void UpdateAvailability()
    {
        var busy = active is not null;
        tabBar.Enabled = !busy;
        link.Enabled = paste.Enabled = choose.Enabled = formats.Enabled = resolutions.Enabled = !busy;
        var shouldShowClear = !busy && link.Text.Length > 0;
        if (clearLink.Visible != shouldShowClear)
        {
            clearLink.Visible = shouldShowClear;
            LayoutLinkBox();
        }
        download.Visible = !busy;
        var canDownload = !busy && tools.Installed && !string.IsNullOrWhiteSpace(link.Text);
        download.Enabled = canDownload;
        var palette = PlatformPalette(selectedPlatform);
        download.NormalBackColor = canDownload ? palette.Accent : theme.DisabledAccent;
        download.HoverBackColor = canDownload ? palette.AccentHover : theme.DisabledAccent;
        download.Invalidate();
        cancel.Visible = busy;
        reveal.Visible = !busy && savedFile is not null;
        AcceptButton = busy ? null : download;
        CancelButton = busy ? cancel : null;
    }

    private async Task StartDownloadAsync()
    {
        if (active is not null || !tools.Installed) return;
        savedFile = null;
        errorDetail = null;
        details.Visible = false;
        progressText.Text = "";
        tips.SetToolTip(progressText, "");
        UpdateAvailability();

        var video = DownloadEngine.ParseMediaLink(link.Text, selectedPlatform);
        if (video is null)
        {
            if (DownloadEngine.ParseMediaLink(link.Text) is { } detected)
            {
                SelectPlatform(detected.Platform, clearError: false);
                video = detected;
            }
            else
            {
                SetStatusState(StatusKind.Failed, selectedPlatform.InvalidLinkText());
                return;
            }
        }

        var format = ((Choice<MediaFormat>)formats.SelectedItem!).Value;
        var height = ((Choice<int>)resolutions.SelectedItem!).Value;
        using var cancellation = new CancellationTokenSource();
        active = cancellation;
        videoTitle = null;
        statusFraction = null;
        statusSpeed = null;
        statusRemaining = null;
        statusFormatLabel = format.Label;
        var palette = PlatformPalette(video.Platform);
        SetStatusState(StatusKind.Fetching);
        progressBar.BarColor = palette.Accent;
        progressBar.SetIndeterminate(true);
        progressBar.Visible = true;
        cancel.Text = AppStrings.Tr(Lang, "Отменить", "Cancel");
        cancel.Enabled = true;
        UpdateAvailability();

        try
        {
            var progress = new Progress<DownloadProgress>(update => ApplyProgress(update, video.Platform, format, cancellation));
            var result = await DownloadEngine.DownloadMediaAsync(video, format, height, folder, tools, progress, cancellation.Token);
            savedFile = result.FilePath;
            savedIsPhoto = result.IsPhoto;
            SetStatusState(StatusKind.Saved);
            var fileName = Path.GetFileName(savedFile);
            progressText.Text = fileName;
            tips.SetToolTip(progressText, savedFile);
        }
        catch (OperationCanceledException)
        {
            SetStatusState(StatusKind.Cancelled);
            progressText.Text = "";
        }
        catch (DownloadFailure error)
        {
            SetStatusState(StatusKind.Failed, error.LocalizedMessage);
            errorDetail = error.Detail;
            details.Visible = !string.IsNullOrEmpty(errorDetail);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            SetStatusState(StatusKind.Failed, new LocalizedText(
                "Не удалось запустить загрузку или записать файл.",
                "Could not start the download or write the file."));
            errorDetail = error.Message;
            details.Visible = true;
        }
        catch (Exception error)
        {
            SetStatusState(StatusKind.Failed, new LocalizedText(
                "Произошла ошибка при скачивании.",
                "An error occurred while downloading."));
            errorDetail = error.Message;
            details.Visible = !string.IsNullOrEmpty(errorDetail);
        }
        finally
        {
            active = null;
            progressBar.SetIndeterminate(false);
            progressBar.Visible = false;
            cancel.Text = AppStrings.Tr(Lang, "Отменить", "Cancel");
            UpdateAvailability();
            if (closeWhenFinished && !IsDisposed) Close();
        }
    }

    private void ApplyProgress(DownloadProgress update, VideoPlatform platform, MediaFormat format, CancellationTokenSource cancellation)
    {
        if (IsDisposed || active != cancellation || cancellation.IsCancellationRequested) return;
        var palette = PlatformPalette(platform);
        if (update.Title is not null)
        {
            videoTitle = update.Title;
            if (statusKind == StatusKind.Downloading)
                RenderCurrentStatus();
            else
                progressText.Text = videoTitle;
            return;
        }
        if (update.DownloadingPhoto)
        {
            progressBar.BarColor = palette.Accent;
            SetStatusState(StatusKind.DownloadingPhoto);
            progressBar.SetIndeterminate(true);
            return;
        }
        if (update.WaitingForNetwork)
        {
            progressBar.BarColor = theme.Amber;
            SetStatusState(StatusKind.WaitingForNetwork);
            progressBar.SetIndeterminate(true);
            return;
        }
        if (update.Resuming)
        {
            progressBar.BarColor = palette.Accent;
            SetStatusState(StatusKind.Resuming);
            progressBar.SetIndeterminate(true);
            return;
        }
        if (update.Finalizing)
        {
            statusFormatLabel = format.Label;
            progressBar.BarColor = palette.Accent;
            SetStatusState(StatusKind.Finalizing);
            progressBar.SetIndeterminate(true);
            return;
        }

        statusIsAudio = update.IsAudio || format.IsAudio;
        statusFraction = update.Fraction;
        statusSpeed = update.Speed;
        statusRemaining = update.Remaining;
        progressBar.BarColor = palette.Accent;
        SetStatusState(StatusKind.Downloading);
        if (update.Fraction is double value)
        {
            progressBar.SetIndeterminate(false);
            progressBar.ProgressValue = value;
        }
        else
        {
            progressBar.SetIndeterminate(true);
        }
    }

    private void CancelDownload()
    {
        if (active is null) return;
        cancel.Enabled = false;
        cancel.Text = AppStrings.Tr(Lang, "Отмена", "Cancelling");
        SetStatusState(StatusKind.Cancelling);
        active.Cancel();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (active is not null)
        {
            e.Cancel = true;
            closeWhenFinished = true;
            CancelDownload();
        }
        base.OnFormClosing(e);
    }

    private void ChooseFolder()
    {
        using var picker = new FolderBrowserDialog
        {
            Description = AppStrings.Tr(Lang, "Папка для скачивания", "Download folder"),
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(folder) ? folder : ""
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        folder = picker.SelectedPath;
        folderText.Text = folder;
        tips.SetToolTip(folderText, folder);
        ClearErrorIfIdle();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);
            File.WriteAllText(settingsFile, folder);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void ShowErrorDetails()
    {
        if (string.IsNullOrEmpty(errorDetail)) return;
        using var monoFont = new Font("Consolas", 9.5F);
        using var dialog = new Form
        {
            Text = AppStrings.Tr(Lang, "Сообщение загрузчика", "Downloader output"),
            Font = Font,
            BackColor = theme.Surface,
            ForeColor = theme.Ink,
            StartPosition = FormStartPosition.CenterParent,
            AutoScaleDimensions = new SizeF(96F, 96F),
            AutoScaleMode = AutoScaleMode.Dpi,
            ClientSize = new Size(520, 230),
            MinimumSize = new Size(420, 180),
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            Icon = Icon
        };
        var currentTheme = theme;
        dialog.HandleCreated += (_, _) => ApplyWindowChrome(dialog.Handle, currentTheme);

        var box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Text = errorDetail,
            BackColor = theme.CardBg,
            ForeColor = theme.Ink,
            BorderStyle = BorderStyle.FixedSingle,
            Font = monoFont
        };
        box.HandleCreated += (_, _) => ApplyNativeControlTheme(box, "DarkMode_Explorer", currentTheme.IsDark);
        var copyButton = new PillButton
        {
            Text = AppStrings.Tr(Lang, "Копировать", "Copy"),
            Size = new Size(108, 32),
            CornerRadius = 9,
            ShowBorder = true
        };
        var closeButton = new PillButton
        {
            Text = AppStrings.Tr(Lang, "Закрыть", "Close"),
            DialogResult = DialogResult.OK,
            Size = new Size(96, 32),
            CornerRadius = 9,
            ShowBorder = true
        };
        StyleSecondaryButton(copyButton, backdrop: theme.Surface);
        StyleSecondaryButton(closeButton, backdrop: theme.Surface);
        copyButton.Click += (_, _) =>
        {
            try { Clipboard.SetText(errorDetail); }
            catch (ExternalException) { }
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            BackColor = theme.Surface,
            Padding = new Padding(12, 8, 12, 12)
        };
        buttons.Controls.Add(closeButton);
        buttons.Controls.Add(copyButton);
        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 12, 12, 0), BackColor = theme.Surface };
        host.Controls.Add(box);
        dialog.Controls.Add(host);
        dialog.Controls.Add(buttons);
        dialog.AcceptButton = closeButton;
        dialog.CancelButton = closeButton;
        dialog.ShowDialog(this);
    }

    private void RevealFile()
    {
        if (savedFile is null) return;
        if (!File.Exists(savedFile))
        {
            SetStatusState(StatusKind.FileMissing);
            progressText.Text = "";
            tips.SetToolTip(progressText, "");
            savedFile = null;
            UpdateAvailability();
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                $"/select,\"{savedFile}\"")
            { UseShellExecute = false });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            SetStatusState(StatusKind.ExplorerFailed);
        }
    }

    private string LoadFolder()
    {
        try
        {
            if (File.Exists(settingsFile) && File.ReadAllText(settingsFile).Trim() is { Length: > 0 } saved && Directory.Exists(saved))
                return saved;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        var knownDownloads = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        var result = SHGetKnownFolderPath(in knownDownloads, 0, IntPtr.Zero, out var path);
        try
        {
            if (result == 0 && Marshal.PtrToStringUni(path) is { Length: > 0 } downloads && Directory.Exists(downloads))
                return downloads;
        }
        finally
        {
            if (path != IntPtr.Zero)
                Marshal.FreeCoTaskMem(path);
        }

        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return Directory.Exists(fallback)
            ? fallback
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(in Guid folderId, uint flags, IntPtr token, out IntPtr path);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? pszSubAppName, string? pszSubIdList);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private static Color Blend(Color foreground, Color background, float amount)
    {
        var inv = 1f - amount;
        return Color.FromArgb(
            255,
            Math.Clamp((int)MathF.Round(foreground.R * amount + background.R * inv), 0, 255),
            Math.Clamp((int)MathF.Round(foreground.G * amount + background.G * inv), 0, 255),
            Math.Clamp((int)MathF.Round(foreground.B * amount + background.B * inv), 0, 255));
    }

    private sealed record Choice<T>(string Label, T Value)
    {
        public override string ToString() => Label;
    }

    private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(1f, Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height)));
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class RoundedPanel : Panel
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Color FillColor { get; set; } = Color.White;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Color BackdropColor { get; set; } = Color.FromArgb(244, 247, 252);
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Color BorderColor { get; set; } = Color.FromArgb(219, 227, 240);
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal int CornerRadius { get; set; } = 12;

        public RoundedPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var brush = new SolidBrush(BackdropColor);
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new RectangleF(0.5f, 0.5f, Math.Max(1, Width - 1.5f), Math.Max(1, Height - 1.5f));
            using var path = RoundedRect(rect, LogicalToDeviceUnits(CornerRadius));
            using var fill = new SolidBrush(FillColor);
            using var pen = new Pen(BorderColor, 1f);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(pen, path);
        }
    }

    private sealed class GradientHeaderPanel : Control
    {
        private enum PillTarget { None, Theme, Language }

        private PillTarget hovered = PillTarget.None;
        private PillTarget focusedPill = PillTarget.None;
        private RectangleF themePillRect;
        private RectangleF langPillRect;

        public event EventHandler? ThemeClicked;
        public event EventHandler? LanguageClicked;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Color StartColor { get; set; } = Color.FromArgb(13, 28, 71);
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Color EndColor { get; set; } = Color.FromArgb(24, 76, 188);
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Color GlowColor { get; set; } = Color.FromArgb(0, 198, 255);
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Color SubtitleColor { get; set; } = Color.FromArgb(204, 226, 255);
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Color BackdropColor { get; set; } = Color.FromArgb(244, 247, 252);
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal int CornerRadius { get; set; } = 16;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal string TitleText { get; set; } = "Скачать с YouTube";
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal string SubtitleText { get; set; } = "Видео и звук";
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal string BadgeText { get; set; } = "v1.3.0";
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal AppTheme ThemeMode { get; set; } = AppTheme.System;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal string ThemeLabel { get; set; } = "Авто";
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal string LanguageLabel { get; set; } = "RU";
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal string ThemeTooltip { get; set; } = "";
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal string LanguageTooltip { get; set; } = "";
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal ToolTip? ToolTipControl { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Bitmap? IconBitmap { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Font? TitleFont { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Font? SubtitleFont { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Font? PillFont { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Font? BadgeFont { get; set; }

        public GradientHeaderPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            TabStop = true;
        }

        internal void RefreshHoveredToolTip()
        {
            UpdateAccessibleName();
            if (ToolTipControl is null) return;
            var tip = hovered switch
            {
                PillTarget.Theme => ThemeTooltip,
                PillTarget.Language => LanguageTooltip,
                _ => ""
            };
            ToolTipControl.SetToolTip(this, tip);
        }

        private void UpdateAccessibleName()
        {
            AccessibleName = focusedPill switch
            {
                PillTarget.Theme => ThemeTooltip,
                PillTarget.Language => LanguageTooltip,
                _ => $"{ThemeTooltip}; {LanguageTooltip}"
            };
        }

        private PillTarget HitTest(Point p)
        {
            if (themePillRect.Contains(p)) return PillTarget.Theme;
            if (langPillRect.Contains(p)) return PillTarget.Language;
            return PillTarget.None;
        }

        protected override void OnGotFocus(EventArgs e)
        {
            if (focusedPill == PillTarget.None)
                focusedPill = PillTarget.Theme;
            UpdateAccessibleName();
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            focusedPill = PillTarget.None;
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData is Keys.Left or Keys.Right or Keys.Enter or Keys.Space || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Left or Keys.Right)
            {
                focusedPill = focusedPill == PillTarget.Language ? PillTarget.Theme : PillTarget.Language;
                UpdateAccessibleName();
                Invalidate();
                e.Handled = true;
            }
            else if (e.KeyCode is Keys.Enter or Keys.Space)
            {
                if (focusedPill == PillTarget.Language)
                    LanguageClicked?.Invoke(this, EventArgs.Empty);
                else
                    ThemeClicked?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var hit = HitTest(e.Location);
            if (hit != hovered)
            {
                hovered = hit;
                Cursor = hit == PillTarget.None ? Cursors.Default : Cursors.Hand;
                RefreshHoveredToolTip();
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hovered != PillTarget.None)
            {
                hovered = PillTarget.None;
                Cursor = Cursors.Default;
                ToolTipControl?.SetToolTip(this, "");
                Invalidate();
            }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                switch (HitTest(e.Location))
                {
                    case PillTarget.Theme:
                        ThemeClicked?.Invoke(this, EventArgs.Empty);
                        break;
                    case PillTarget.Language:
                        LanguageClicked?.Invoke(this, EventArgs.Empty);
                        break;
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var bg = new SolidBrush(BackdropColor);
            e.Graphics.FillRectangle(bg, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            var rect = new RectangleF(0.5f, 0.5f, Math.Max(1, Width - 1.5f), Math.Max(1, Height - 1.5f));
            using var cardPath = RoundedRect(rect, LogicalToDeviceUnits(CornerRadius));

            using (var gradient = new LinearGradientBrush(rect, StartColor, EndColor, 340f))
                g.FillPath(gradient, cardPath);

            var savedState = g.Save();
            g.SetClip(cardPath);
            using (var glowPath = new GraphicsPath())
            {
                var glowSize = Math.Max(Width * 0.65f, 180f);
                glowPath.AddEllipse(Width - glowSize * 0.52f, -glowSize * 0.45f, glowSize, glowSize);
                using var pgb = new PathGradientBrush(glowPath)
                {
                    CenterColor = Color.FromArgb(72, GlowColor),
                    SurroundColors = [Color.FromArgb(0, GlowColor)]
                };
                g.FillPath(pgb, glowPath);
            }
            g.Restore(savedState);

            using (var border = new Pen(Color.FromArgb(42, Color.White), 1f))
                g.DrawPath(border, cardPath);

            var padX = LogicalToDeviceUnits(18);
            var iconSize = LogicalToDeviceUnits(58);
            var textLeft = padX;

            if (IconBitmap is not null)
            {
                var iconY = (Height - iconSize) / 2;
                var shadowRect = new RectangleF(padX + 1, iconY + LogicalToDeviceUnits(3), iconSize, iconSize);
                using (var shadowPath = RoundedRect(shadowRect, LogicalToDeviceUnits(14)))
                using (var shadowBrush = new SolidBrush(Color.FromArgb(55, 0, 0, 0)))
                    g.FillPath(shadowBrush, shadowPath);

                g.DrawImage(IconBitmap, new Rectangle(padX, iconY, iconSize, iconSize));
                textLeft = padX + iconSize + LogicalToDeviceUnits(14);
            }

            // Top-right frosted pills: [Theme] [RU/EN] + version label below
            var pFont = PillFont ?? Font;
            var rightEdge = Width - LogicalToDeviceUnits(16);
            var pillTop = LogicalToDeviceUnits(14);
            var pillPadX = LogicalToDeviceUnits(9);
            var pillPadY = LogicalToDeviceUnits(4);
            var pillGap = LogicalToDeviceUnits(6);

            var langTextSize = TextRenderer.MeasureText(g, LanguageLabel, pFont, Size.Empty, TextFormatFlags.NoPadding);
            var pillH = langTextSize.Height + pillPadY * 2;
            var langW = langTextSize.Width + pillPadX * 2;
            var langX = rightEdge - langW;
            langPillRect = new RectangleF(langX, pillTop, langW, pillH);

            var themeTextSize = TextRenderer.MeasureText(g, ThemeLabel, pFont, Size.Empty, TextFormatFlags.NoPadding);
            var glyphSize = LogicalToDeviceUnits(12);
            var glyphGap = LogicalToDeviceUnits(5);
            var themeW = pillPadX + glyphSize + glyphGap + themeTextSize.Width + pillPadX;
            var themeX = langX - pillGap - themeW;
            themePillRect = new RectangleF(themeX, pillTop, themeW, pillH);

            DrawFrostedPill(g, themePillRect, hovered == PillTarget.Theme, Focused && focusedPill == PillTarget.Theme);
            DrawThemeGlyph(g, ThemeMode,
                new RectangleF(themeX + pillPadX, pillTop + (pillH - glyphSize) / 2f, glyphSize, glyphSize));
            TextRenderer.DrawText(
                g, ThemeLabel, pFont,
                new Rectangle(themeX + pillPadX + glyphSize + glyphGap, pillTop, themeTextSize.Width + 2, pillH),
                Color.FromArgb(245, 250, 255),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            DrawFrostedPill(g, langPillRect, hovered == PillTarget.Language, Focused && focusedPill == PillTarget.Language);
            TextRenderer.DrawText(
                g, LanguageLabel, pFont,
                Rectangle.Round(langPillRect),
                Color.FromArgb(245, 250, 255),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            var bFont = BadgeFont ?? Font;
            var badgeTextSize = TextRenderer.MeasureText(g, BadgeText, bFont, Size.Empty, TextFormatFlags.NoPadding);
            var badgeY = pillTop + pillH + LogicalToDeviceUnits(8);
            var badgeColor = Blend(Color.FromArgb(232, 244, 255), EndColor, 0.72f);
            TextRenderer.DrawText(
                g, BadgeText, bFont,
                new Rectangle(rightEdge - badgeTextSize.Width - LogicalToDeviceUnits(3), badgeY, badgeTextSize.Width + 4, badgeTextSize.Height + 2),
                badgeColor,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            // Title & subtitle vertically centered on the left/center area
            var tFont = TitleFont ?? Font;
            var sFont = SubtitleFont ?? Font;
            var titleSize = TextRenderer.MeasureText(g, TitleText, tFont, Size.Empty, TextFormatFlags.NoPadding);
            var subSize = TextRenderer.MeasureText(g, SubtitleText, sFont, Size.Empty, TextFormatFlags.NoPadding);
            var gap = LogicalToDeviceUnits(3);
            var blockHeight = titleSize.Height + gap + subSize.Height;
            var blockTop = Math.Max(LogicalToDeviceUnits(10), (Height - blockHeight) / 2);
            var maxTextWidth = Math.Max(60, themeX - textLeft - LogicalToDeviceUnits(10));

            TextRenderer.DrawText(
                g, TitleText, tFont,
                new Rectangle(textLeft, blockTop, maxTextWidth, titleSize.Height + 2),
                Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            TextRenderer.DrawText(
                g, SubtitleText, sFont,
                new Rectangle(textLeft + LogicalToDeviceUnits(1), blockTop + titleSize.Height + gap, maxTextWidth, subSize.Height + 2),
                SubtitleColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        private static void DrawFrostedPill(Graphics g, RectangleF bounds, bool isHovered, bool isFocused)
        {
            using var path = RoundedRect(bounds, bounds.Height / 2f);
            using var fill = new SolidBrush(Color.FromArgb(isHovered || isFocused ? 66 : 42, Color.White));
            using var pen = new Pen(Color.FromArgb(isFocused ? 200 : isHovered ? 110 : 62, Color.White), isFocused ? 1.5f : 1f);
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        private static void DrawThemeGlyph(Graphics g, AppTheme mode, RectangleF box)
        {
            var white = Color.FromArgb(245, 250, 255);
            using var brush = new SolidBrush(white);
            using var pen = new Pen(white, 1.35f) { StartCap = LineCap.Round, EndCap = LineCap.Round };

            switch (mode)
            {
                case AppTheme.Light:
                    var cx = box.Left + box.Width / 2f;
                    var cy = box.Top + box.Height / 2f;
                    var r = box.Width * 0.24f;
                    g.FillEllipse(brush, cx - r, cy - r, r * 2f, r * 2f);
                    var inner = box.Width * 0.36f;
                    var outer = box.Width * 0.50f;
                    for (var i = 0; i < 8; i++)
                    {
                        var angle = i * Math.PI / 4.0;
                        var cos = (float)Math.Cos(angle);
                        var sin = (float)Math.Sin(angle);
                        g.DrawLine(pen, cx + cos * inner, cy + sin * inner, cx + cos * outer, cy + sin * outer);
                    }
                    break;

                case AppTheme.Dark:
                    using (var moon = new GraphicsPath())
                    {
                        var inset = new RectangleF(box.Left + 1f, box.Top + 1f, box.Width - 2f, box.Height - 2f);
                        moon.AddArc(inset, 35, 250);
                        var innerArc = new RectangleF(inset.Left + inset.Width * 0.24f, inset.Top - inset.Height * 0.08f,
                            inset.Width * 0.82f, inset.Height * 0.82f);
                        moon.AddArc(innerArc, 255, -185);
                        moon.CloseFigure();
                        g.FillPath(brush, moon);
                    }
                    break;

                default: // System (half-filled circle)
                    var circle = new RectangleF(box.Left + 1f, box.Top + 1f, box.Width - 2f, box.Height - 2f);
                    g.DrawEllipse(pen, circle);
                    g.FillPie(brush, circle.X, circle.Y, circle.Width, circle.Height, 90, 180);
                    break;
            }
        }
    }

    private sealed class PlatformTabBar : Control
    {
        private static readonly VideoPlatform[] Items = [VideoPlatform.YouTube, VideoPlatform.TikTok, VideoPlatform.Pinterest];
        private int hoveredIndex = -1;

        public event EventHandler<VideoPlatform>? PlatformSelected;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal VideoPlatform SelectedPlatform { get; set; } = VideoPlatform.YouTube;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Color BackdropColor { get; set; } = Color.FromArgb(244, 247, 252);
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Color TrackColor { get; set; } = Color.FromArgb(228, 234, 245);
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Color BorderColor { get; set; } = Color.FromArgb(212, 221, 236);
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Color HoverColor { get; set; } = Color.FromArgb(145, Color.White);
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Color ActiveColor { get; set; } = Color.FromArgb(22, 92, 224);
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Color DisabledColor { get; set; } = Color.FromArgb(164, 179, 206);
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Color InactiveTextColor { get; set; } = Color.FromArgb(20, 30, 48);
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Color MutedTextColor { get; set; } = Color.FromArgb(92, 107, 132);
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Font? NormalFont { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Font? ActiveFont { get; set; }

        public PlatformTabBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleName = "Выбор платформы";
        }

        private RectangleF TabRect(int index)
        {
            var pad = LogicalToDeviceUnits(4);
            var gap = LogicalToDeviceUnits(4);
            var innerW = Math.Max(3, Width - pad * 2 - gap * (Items.Length - 1));
            var tabW = innerW / (float)Items.Length;
            var x = pad + index * (tabW + gap);
            return new RectangleF(x, pad, tabW, Math.Max(1, Height - pad * 2));
        }

        private int IndexAt(Point p)
        {
            for (var i = 0; i < Items.Length; i++)
                if (TabRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var idx = Enabled ? IndexAt(e.Location) : -1;
            if (idx != hoveredIndex)
            {
                hoveredIndex = idx;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hoveredIndex != -1)
            {
                hoveredIndex = -1;
                Invalidate();
            }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (Enabled && e.Button == MouseButtons.Left)
            {
                var idx = IndexAt(e.Location);
                if (idx >= 0 && Items[idx] != SelectedPlatform)
                    PlatformSelected?.Invoke(this, Items[idx]);
            }
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Enabled)
            {
                var cur = Array.IndexOf(Items, SelectedPlatform);
                if (e.KeyCode == Keys.Right)
                {
                    PlatformSelected?.Invoke(this, Items[(cur + 1) % Items.Length]);
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Left)
                {
                    PlatformSelected?.Invoke(this, Items[(cur + Items.Length - 1) % Items.Length]);
                    e.Handled = true;
                }
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var bg = new SolidBrush(BackdropColor);
            e.Graphics.FillRectangle(bg, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var outer = new RectangleF(0.5f, 0.5f, Math.Max(1, Width - 1.5f), Math.Max(1, Height - 1.5f));
            using (var outerPath = RoundedRect(outer, LogicalToDeviceUnits(12)))
            using (var trackBrush = new SolidBrush(TrackColor))
            using (var borderPen = new Pen(BorderColor, 1f))
            {
                g.FillPath(trackBrush, outerPath);
                g.DrawPath(borderPen, outerPath);
            }

            var tabRadius = LogicalToDeviceUnits(9);
            for (var i = 0; i < Items.Length; i++)
            {
                var platform = Items[i];
                var selected = platform == SelectedPlatform;
                var rect = TabRect(i);

                if (selected)
                {
                    var fillCol = Enabled ? ActiveColor : DisabledColor;
                    using var pillPath = RoundedRect(rect, tabRadius);
                    using var fill = new SolidBrush(fillCol);
                    g.FillPath(fill, pillPath);
                }
                else if (i == hoveredIndex && Enabled)
                {
                    using var pillPath = RoundedRect(rect, tabRadius);
                    using var hoverFill = new SolidBrush(HoverColor);
                    g.FillPath(hoverFill, pillPath);
                }

                var font = selected ? (ActiveFont ?? Font) : (NormalFont ?? Font);
                var textColor = selected
                    ? Color.White
                    : (Enabled ? InactiveTextColor : MutedTextColor);
                TextRenderer.DrawText(
                    g,
                    platform.DisplayName(),
                    font,
                    Rectangle.Round(rect),
                    textColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }
    }

    private sealed class PillButton : Button
    {
        private bool hovered;
        private bool pressed;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Color BackdropColor { get; set; } = Color.FromArgb(244, 247, 252);
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Color NormalBackColor { get; set; } = Color.FromArgb(241, 245, 251);
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Color HoverBackColor { get; set; } = Color.FromArgb(228, 235, 247);
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Color BorderColor { get; set; } = Color.FromArgb(219, 227, 240);
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal bool ShowBorder { get; set; } = true;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal int CornerRadius { get; set; } = 9;

        public PillButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs mevent) { pressed = true; Invalidate(); base.OnMouseDown(mevent); }
        protected override void OnMouseUp(MouseEventArgs mevent) { pressed = false; Invalidate(); base.OnMouseUp(mevent); }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            using var brush = new SolidBrush(BackdropColor);
            pevent.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new RectangleF(0.5f, 0.5f, Math.Max(1, Width - 1.5f), Math.Max(1, Height - 1.5f));
            using var path = RoundedRect(rect, LogicalToDeviceUnits(CornerRadius));

            var fillColor = !Enabled
                ? NormalBackColor
                : pressed
                    ? ControlPaint.Dark(HoverBackColor, 0.05f)
                    : hovered ? HoverBackColor : NormalBackColor;

            using (var fill = new SolidBrush(fillColor))
                e.Graphics.FillPath(fill, path);

            if (ShowBorder)
            {
                var borderCol = !Enabled
                    ? Blend(BorderColor, BackdropColor, 0.55f)
                    : Focused ? ControlPaint.Dark(BorderColor, 0.15f) : BorderColor;
                using var pen = new Pen(borderCol, 1f);
                e.Graphics.DrawPath(pen, path);
            }

            var textColor = Enabled ? ForeColor : Blend(ForeColor, fillColor, 0.48f);
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                ClientRectangle,
                textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    private sealed class SmoothProgressBar : Control
    {
        private readonly System.Windows.Forms.Timer timer = new() { Interval = 24 };
        private double progress;
        private bool indeterminate;
        private float marqueeOffset;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Color TrackColor { get; set; } = Color.FromArgb(226, 233, 245);
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Color BarColor { get; set; } = Color.FromArgb(22, 92, 224);

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal double ProgressValue
        {
            get => progress;
            set
            {
                progress = Math.Clamp(value, 0, 1);
                Invalidate();
            }
        }

        public SmoothProgressBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            Height = 8;
            timer.Tick += (_, _) =>
            {
                marqueeOffset += 0.025f;
                if (marqueeOffset > 1.35f) marqueeOffset = -0.35f;
                Invalidate();
            };
        }

        public void SetIndeterminate(bool enabled)
        {
            indeterminate = enabled;
            if (enabled)
            {
                marqueeOffset = -0.35f;
                timer.Start();
            }
            else
            {
                timer.Stop();
            }
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                timer.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            using var bg = new SolidBrush(BackColor);
            pevent.Graphics.FillRectangle(bg, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new RectangleF(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            var radius = rect.Height / 2f;
            using var trackPath = RoundedRect(rect, radius);
            using var trackBrush = new SolidBrush(TrackColor);
            e.Graphics.FillPath(trackBrush, trackPath);

            e.Graphics.SetClip(trackPath);
            using var barBrush = new SolidBrush(BarColor);
            if (indeterminate)
            {
                var segmentWidth = Math.Max(rect.Width * 0.32f, 40f);
                var x = marqueeOffset * rect.Width;
                var seg = new RectangleF(x, 0, segmentWidth, rect.Height);
                using var segPath = RoundedRect(seg, radius);
                e.Graphics.FillPath(barBrush, segPath);
            }
            else if (progress > 0.005)
            {
                var fillW = Math.Max(rect.Height, (float)(rect.Width * progress));
                var seg = new RectangleF(0, 0, fillW, rect.Height);
                using var segPath = RoundedRect(seg, radius);
                e.Graphics.FillPath(barBrush, segPath);
            }
            e.Graphics.ResetClip();
        }
    }
}
