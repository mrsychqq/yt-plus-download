import AppKit
import SwiftUI

@main
struct YTPlusApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate

    var body: some Scene {
        Window("YT+", id: "download") {
            DownloadView(model: delegate.model)
        }
        .windowResizability(.contentSize)
        .defaultPosition(.center)
        .commands {
            CommandGroup(replacing: .newItem) {}
        }
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    let model = DownloadModel()

    func applicationDidFinishLaunching(_ notification: Notification) {
        model.applyAppearance()
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        true
    }

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        guard model.isBusy else { return .terminateNow }
        model.onDownloadFinished = { sender.reply(toApplicationShouldTerminate: true) }
        model.cancel()
        return .terminateLater
    }
}

struct DownloadView: View {
    private static let appIcon = Bundle.main.url(forResource: "AppIcon", withExtension: "icns")
        .flatMap { NSImage(contentsOf: $0) }
    private static let platformIcons: [VideoPlatform: NSImage] = {
        let names: [(VideoPlatform, String)] = [
            (.youtube, "IconYouTube"),
            (.tiktok, "IconTikTok"),
            (.pinterest, "IconPinterest")
        ]
        var result: [VideoPlatform: NSImage] = [:]
        for (platform, name) in names {
            if let url = Bundle.main.url(forResource: name, withExtension: "png"),
               let image = NSImage(contentsOf: url) {
                result[platform] = image
            }
        }
        return result
    }()
    private static let appVersion = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "1.3.0"

    @ObservedObject var model: DownloadModel
    @Environment(\.colorScheme) private var colorScheme
    @FocusState private var linkFocused: Bool
    @Namespace private var tabAnimation

    private var lang: AppLanguage { model.language }
    private var isDark: Bool { colorScheme == .dark }

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            header
            platformTabs
                .disabled(model.isBusy)
            linkField
                .disabled(model.isBusy)
            options
                .disabled(model.isBusy)
            statusPanel
            actions
        }
        .padding(22)
        .frame(width: 580)
        .background(Color(nsColor: .windowBackgroundColor))
        .onAppear {
            model.applyAppearance()
            linkFocused = true
        }
        .sheet(isPresented: $model.showFailureDetails) {
            if case .failed(_, let detail) = model.status, let detail {
                VStack(alignment: .leading, spacing: 14) {
                    Text(L10n.tr(lang, ru: "Сообщение загрузчика", en: "Downloader output"))
                        .font(.headline)
                    ScrollView {
                        Text(detail)
                            .font(.system(.body, design: .monospaced))
                            .textSelection(.enabled)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                    HStack {
                        Button(L10n.tr(lang, ru: "Копировать", en: "Copy")) {
                            NSPasteboard.general.clearContents()
                            NSPasteboard.general.setString(detail, forType: .string)
                        }
                        Spacer()
                        Button(L10n.tr(lang, ru: "Закрыть", en: "Close")) { model.showFailureDetails = false }
                            .keyboardShortcut(.cancelAction)
                    }
                }
                .padding(20)
                .frame(width: 480, height: 210)
            }
        }
    }

    private func platformAccent(_ platform: VideoPlatform) -> Color {
        switch (platform, isDark) {
        case (.youtube, false): Color(red: 0.08, green: 0.36, blue: 0.88)
        case (.youtube, true): Color(red: 0.25, green: 0.54, blue: 1.0)
        case (.tiktok, false): Color(red: 0.06, green: 0.49, blue: 0.52)
        case (.tiktok, true): Color(red: 0.14, green: 0.76, blue: 0.74)
        case (.pinterest, false): Color(red: 0.76, green: 0.12, blue: 0.24)
        case (.pinterest, true): Color(red: 0.94, green: 0.27, blue: 0.40)
        }
    }

    private func platformIcon(_ platform: VideoPlatform) -> String {
        switch platform {
        case .youtube: "play.rectangle.fill"
        case .tiktok: "music.note"
        case .pinterest: "pin.fill"
        }
    }

    private var headerGradientColors: [Color] {
        switch model.platform {
        case .youtube:
            [Color(red: 0.05, green: 0.11, blue: 0.28), Color(red: 0.09, green: 0.29, blue: 0.72)]
        case .tiktok:
            [Color(red: 0.06, green: 0.10, blue: 0.16), Color(red: 0.07, green: 0.33, blue: 0.42)]
        case .pinterest:
            [Color(red: 0.27, green: 0.04, blue: 0.10), Color(red: 0.65, green: 0.07, blue: 0.21)]
        }
    }

    private var headerGlowColor: Color {
        switch model.platform {
        case .youtube: Color(red: 0.0, green: 0.78, blue: 1.0)
        case .tiktok: Color(red: 0.15, green: 0.94, blue: 0.90)
        case .pinterest: Color(red: 1.0, green: 0.36, blue: 0.48)
        }
    }

    private var headerSubtitleColor: Color {
        switch model.platform {
        case .youtube: Color(red: 0.78, green: 0.89, blue: 1.0)
        case .tiktok: Color(red: 0.70, green: 0.95, blue: 0.96)
        case .pinterest: Color(red: 1.0, green: 0.82, blue: 0.86)
        }
    }

    private var header: some View {
        HStack(spacing: 14) {
            Group {
                if let icon = Self.platformIcons[model.platform] ?? Self.appIcon {
                    Image(nsImage: icon)
                        .resizable()
                        .interpolation(.high)
                        .scaledToFit()
                        .id(model.platform)
                        .transition(.opacity)
                } else {
                    Image(systemName: "arrow.down.to.line")
                        .font(.system(size: 27, weight: .semibold))
                        .foregroundStyle(.white)
                }
            }
            .frame(width: 60, height: 60)
            .shadow(color: .black.opacity(0.28), radius: 8, y: 4)
            .accessibilityHidden(true)

            VStack(alignment: .leading, spacing: 4) {
                Text(L10n.headerTitle(platform: model.platform, lang: lang))
                    .font(.system(size: 23, weight: .bold))
                    .lineLimit(1)
                    .minimumScaleFactor(0.82)
                    .foregroundStyle(.white)
                    .accessibilityAddTraits(.isHeader)
                Text(L10n.headerSubtitle(platform: model.platform, lang: lang))
                    .font(.system(size: 13.5, weight: .medium))
                    .foregroundStyle(headerSubtitleColor)
            }
            Spacer(minLength: 8)
            VStack(alignment: .trailing, spacing: 8) {
                HStack(spacing: 6) {
                    Menu {
                        ForEach(AppTheme.allCases) { option in
                            Button {
                                model.theme = option
                            } label: {
                                if model.theme == option {
                                    Label(option.title(lang), systemImage: "checkmark")
                                } else {
                                    Text(option.title(lang))
                                }
                            }
                        }
                    } label: {
                        HStack(spacing: 4) {
                            Image(systemName: model.theme.iconName)
                                .font(.system(size: 11, weight: .semibold))
                            Text(model.theme.shortTitle(lang))
                                .font(.system(size: 11, weight: .semibold))
                        }
                        .foregroundStyle(.white.opacity(0.95))
                        .padding(.horizontal, 9)
                        .padding(.vertical, 4)
                        .background(.white.opacity(0.16), in: Capsule())
                        .overlay(Capsule().strokeBorder(.white.opacity(0.24), lineWidth: 0.75))
                    }
                    .buttonStyle(.plain)
                    .menuIndicator(.hidden)
                    .environment(\.colorScheme, .dark)
                    .fixedSize()
                    .help(L10n.themeHelp(model.theme, lang: lang))
                    .accessibilityLabel(L10n.themeHelp(model.theme, lang: lang))

                    Button {
                        model.language = model.language.toggled
                    } label: {
                        Text(lang.shortCode)
                            .font(.system(size: 11, weight: .bold, design: .rounded))
                            .foregroundStyle(.white.opacity(0.95))
                            .padding(.horizontal, 8)
                            .padding(.vertical, 4)
                            .background(.white.opacity(0.16), in: Capsule())
                            .overlay(Capsule().strokeBorder(.white.opacity(0.24), lineWidth: 0.75))
                    }
                    .buttonStyle(.plain)
                    .help(L10n.languageHelp(lang))
                    .accessibilityLabel(L10n.languageHelp(lang))
                }
                Text("v\(Self.appVersion)")
                    .font(.system(size: 10.5, weight: .semibold, design: .rounded))
                    .foregroundStyle(.white.opacity(0.72))
                    .padding(.trailing, 3)
            }
        }
        .padding(.horizontal, 18)
        .padding(.vertical, 16)
        .background {
            ZStack(alignment: .topTrailing) {
                RoundedRectangle(cornerRadius: 16, style: .continuous)
                    .fill(
                        LinearGradient(
                            colors: headerGradientColors,
                            startPoint: .bottomLeading,
                            endPoint: .topTrailing
                        )
                    )
                RadialGradient(
                    colors: [headerGlowColor.opacity(0.32), .clear],
                    center: .topTrailing,
                    startRadius: 8,
                    endRadius: 210
                )
                .clipShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
                RoundedRectangle(cornerRadius: 16, style: .continuous)
                    .strokeBorder(.white.opacity(0.14), lineWidth: 1)
            }
        }
        .shadow(color: headerGradientColors[0].opacity(isDark ? 0.42 : 0.22), radius: 12, y: 6)
        .animation(.easeInOut(duration: 0.2), value: model.platform)
    }

    private var platformTabs: some View {
        HStack(spacing: 4) {
            ForEach(VideoPlatform.allCases) { item in
                let isSelected = model.platform == item
                Button {
                    withAnimation(.spring(response: 0.26, dampingFraction: 0.85)) {
                        model.platform = item
                    }
                } label: {
                    HStack(spacing: 6) {
                        Image(systemName: platformIcon(item))
                            .font(.system(size: 12.5, weight: .semibold))
                        Text(item.title)
                            .font(.system(size: 13, weight: isSelected ? .semibold : .medium))
                    }
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 8)
                    .foregroundStyle(isSelected ? .white : .primary.opacity(0.82))
                    .background {
                        if isSelected {
                            RoundedRectangle(cornerRadius: 9, style: .continuous)
                                .fill(platformAccent(item))
                                .shadow(color: platformAccent(item).opacity(0.30), radius: 5, y: 2)
                                .matchedGeometryEffect(id: "activeTab", in: tabAnimation)
                        }
                    }
                    .contentShape(RoundedRectangle(cornerRadius: 9, style: .continuous))
                }
                .buttonStyle(.plain)
                .accessibilityLabel(L10n.tabAccessibility(item.title, lang: lang))
                .accessibilityAddTraits(isSelected ? .isSelected : [])
            }
        }
        .padding(4)
        .background(Color.primary.opacity(isDark ? 0.08 : 0.06), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
        .overlay {
            RoundedRectangle(cornerRadius: 12, style: .continuous)
                .strokeBorder(Color.primary.opacity(isDark ? 0.10 : 0.07), lineWidth: 1)
        }
    }

    private var linkField: some View {
        VStack(alignment: .leading, spacing: 7) {
            Text(L10n.tr(lang, ru: "Ссылка", en: "Link"))
                .font(.system(size: 12.5, weight: .semibold))
                .foregroundStyle(.secondary)
            HStack(spacing: 8) {
                HStack(spacing: 6) {
                    Image(systemName: "link")
                        .font(.system(size: 12.5, weight: .medium))
                        .foregroundStyle(.secondary)
                    TextField(L10n.placeholder(platform: model.platform, lang: lang), text: $model.link)
                        .textFieldStyle(.plain)
                        .focused($linkFocused)
                        .accessibilityLabel(L10n.tr(lang, ru: "Ссылка на видео", en: "Video link"))
                        .onSubmit { startDownload() }
                    if !model.link.isEmpty && !model.isBusy {
                        Button {
                            model.link = ""
                            linkFocused = true
                        } label: {
                            Image(systemName: "xmark.circle.fill")
                                .font(.system(size: 13))
                                .foregroundStyle(.tertiary)
                        }
                        .buttonStyle(.plain)
                        .help(L10n.tr(lang, ru: "Очистить поле", en: "Clear"))
                    }
                }
                .padding(.horizontal, 10)
                .padding(.vertical, 8)
                .background(Color(nsColor: .controlBackgroundColor), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                .overlay {
                    RoundedRectangle(cornerRadius: 10, style: .continuous)
                        .strokeBorder(linkFocused ? platformAccent(model.platform).opacity(0.75) : Color.primary.opacity(isDark ? 0.16 : 0.12), lineWidth: linkFocused ? 1.5 : 1)
                }

                Button {
                    model.pasteLink()
                    linkFocused = true
                } label: {
                    Label(L10n.tr(lang, ru: "Вставить", en: "Paste"), systemImage: "clipboard")
                }
                .controlSize(.large)
                .help(L10n.tr(lang, ru: "Вставить ссылку из буфера обмена", en: "Paste link from clipboard"))
            }
        }
    }

    private var options: some View {
        VStack(spacing: 0) {
            HStack {
                Label(L10n.tr(lang, ru: "Формат файла", en: "File format"), systemImage: "doc")
                    .fontWeight(.medium)
                Spacer()
                Picker(L10n.tr(lang, ru: "Формат файла", en: "File format"), selection: $model.format) {
                    Section(L10n.tr(lang, ru: "Видео", en: "Video")) {
                        ForEach(DownloadFormat.allCases.filter { !$0.isAudio }) { format in
                            Text(format.label).tag(format)
                        }
                    }
                    Section(L10n.tr(lang, ru: "Только звук", en: "Audio only")) {
                        ForEach(DownloadFormat.allCases.filter(\.isAudio)) { format in
                            Text(format.label).tag(format)
                        }
                    }
                }
                .labelsHidden()
                .fixedSize()
            }
            .padding(.vertical, 10)
            if !model.format.isAudio {
                Divider()
                HStack {
                    Label(L10n.tr(lang, ru: "Разрешение", en: "Resolution"), systemImage: "slider.horizontal.3")
                        .fontWeight(.medium)
                    Spacer()
                    Picker(L10n.tr(lang, ru: "Разрешение", en: "Resolution"), selection: $model.quality) {
                        ForEach(VideoQuality.allCases) { quality in
                            Text(L10n.qualityLabel(quality.rawValue, lang: lang)).tag(quality)
                        }
                    }
                    .labelsHidden()
                    .fixedSize()
                }
                .padding(.vertical, 10)
            }
            Divider()
            HStack(spacing: 10) {
                Image(systemName: "folder")
                    .frame(width: 16)
                    .foregroundStyle(.secondary)
                    .accessibilityHidden(true)
                VStack(alignment: .leading, spacing: 2) {
                    Text(L10n.tr(lang, ru: "Папка", en: "Folder"))
                        .fontWeight(.medium)
                    Text((model.folder.path as NSString).abbreviatingWithTildeInPath)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                        .truncationMode(.middle)
                        .help(model.folder.path)
                }
                Spacer(minLength: 5)
                Button(L10n.tr(lang, ru: "Выбрать…", en: "Choose…")) { model.chooseFolder() }
                    .accessibilityLabel(L10n.tr(lang, ru: "Выбрать папку", en: "Choose folder"))
            }
            .padding(.vertical, 10)
        }
        .padding(.horizontal, 14)
        .background(Color(nsColor: .controlBackgroundColor), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
        .overlay {
            RoundedRectangle(cornerRadius: 12, style: .continuous)
                .strokeBorder(Color.primary.opacity(isDark ? 0.12 : 0.09), lineWidth: 1)
        }
    }

    private var statusPanel: some View {
        let missing = model.tools.missingNames(lang: lang)
        return VStack(alignment: .leading, spacing: 9) {
            if !missing.isEmpty {
                statusLabel(L10n.missingToolsMessage(missing, lang: lang),
                            icon: "exclamationmark.circle", color: .orange)
                HStack {
                    Link(L10n.tr(lang, ru: "Инструкция по установке", en: "Installation guide"),
                         destination: URL(string: "https://github.com/yt-dlp/yt-dlp/wiki/Installation")!)
                    Spacer()
                    Button(L10n.tr(lang, ru: "Проверить снова", en: "Check again")) { model.refreshTools() }
                }
            } else {
                switch model.status {
                case .ready:
                    statusLabel(L10n.tr(lang, ru: "Готово к скачиванию", en: "Ready to download"),
                                icon: "arrow.down.circle", color: .secondary)
                case .preparing:
                    statusLabel(L10n.tr(lang, ru: "Получаем данные…", en: "Fetching info…"),
                                icon: "arrow.down.circle", color: platformAccent(model.platform))
                    ProgressView().controlSize(.small)
                case .downloading(let progress):
                    transferStatus(progress)
                case .downloadingPhoto:
                    statusLabel(L10n.tr(lang, ru: "Скачиваем фото в оригинале…", en: "Downloading original photo…"),
                                icon: "photo", color: platformAccent(model.platform))
                    ProgressView().controlSize(.small)
                case .waitingForNetwork:
                    statusLabel(L10n.tr(lang,
                        ru: "Соединение потеряно. Ждём сеть и продолжим…",
                        en: "Connection lost. Waiting for network to resume…"),
                                icon: "wifi.slash", color: .orange)
                    ProgressView().controlSize(.small)
                case .resuming:
                    statusLabel(L10n.tr(lang,
                        ru: "Соединение восстановлено. Продолжаем загрузку…",
                        en: "Connection restored. Resuming download…"),
                                icon: "wifi", color: platformAccent(model.platform))
                    ProgressView().controlSize(.small)
                case .finalizing:
                    statusLabel(L10n.preparingFormat(model.format.label, lang: lang),
                                icon: model.format.isAudio ? "waveform" : "film", color: platformAccent(model.platform))
                    ProgressView().controlSize(.small)
                case .cancelling:
                    statusLabel(L10n.tr(lang, ru: "Останавливаем загрузку…", en: "Stopping download…"),
                                icon: "stop.circle", color: .secondary)
                case .cancelled:
                    statusLabel(L10n.tr(lang, ru: "Загрузка отменена", en: "Download cancelled"),
                                icon: "stop.circle", color: .secondary)
                case .saved(let file, let isPhoto):
                    statusLabel(
                        isPhoto
                            ? L10n.tr(lang, ru: "Фото сохранено", en: "Photo saved")
                            : L10n.tr(lang, ru: "Файл сохранён", en: "File saved"),
                        icon: "checkmark.circle.fill", color: .green
                    )
                    Text(file.lastPathComponent)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                        .truncationMode(.middle)
                        .help(file.lastPathComponent)
                case .failed(let message, let detail):
                    statusLabel(message.text(lang), icon: "exclamationmark.circle.fill", color: .red)
                    if detail != nil {
                        Button(L10n.tr(lang, ru: "Подробности…", en: "Details…")) { model.showFailureDetails = true }
                            .buttonStyle(.link)
                    }
                }
            }
        }
        .frame(maxWidth: .infinity, minHeight: 58, alignment: .leading)
        .padding(14)
        .background(Color.primary.opacity(isDark ? 0.06 : 0.04), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
        .overlay {
            RoundedRectangle(cornerRadius: 12, style: .continuous)
                .strokeBorder(Color.primary.opacity(isDark ? 0.10 : 0.06), lineWidth: 1)
        }
    }

    private func statusLabel(_ text: String, icon: String, color: Color) -> some View {
        HStack(spacing: 8) {
            Image(systemName: icon)
                .foregroundStyle(color)
                .accessibilityHidden(true)
            Text(text).fontWeight(.medium)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private func transferStatus(_ progress: TransferProgress) -> some View {
        VStack(alignment: .leading, spacing: 7) {
            HStack {
                statusLabel(
                    progress.isAudio || model.format.isAudio
                        ? L10n.tr(lang, ru: "Скачиваем звук", en: "Downloading audio")
                        : L10n.tr(lang, ru: "Скачиваем видео", en: "Downloading video"),
                    icon: "arrow.down.circle", color: platformAccent(model.platform)
                )
                Spacer()
                if let fraction = progress.fraction {
                    Text(fraction, format: .percent.precision(.fractionLength(0)))
                        .fontWeight(.semibold)
                        .monospacedDigit()
                }
            }
            if let fraction = progress.fraction {
                ProgressView(value: fraction)
                    .tint(platformAccent(model.platform))
            } else {
                ProgressView()
            }
            HStack {
                if let title = model.title {
                    Text(title).lineLimit(1)
                }
                Spacer()
                if let speed = progress.speed { Text(speed) }
                if let remaining = progress.remaining { Text(L10n.remaining(remaining, lang: lang)) }
            }
            .font(.caption)
            .foregroundStyle(.secondary)
            .monospacedDigit()
        }
    }

    private var actions: some View {
        HStack {
            if case .saved(let file, _) = model.status {
                Button {
                    NSWorkspace.shared.activateFileViewerSelecting([file])
                } label: {
                    Label(L10n.tr(lang, ru: "Показать в Finder", en: "Show in Finder"), systemImage: "folder")
                }
            }
            Spacer()
            if case .cancelling = model.status {
                Button(L10n.tr(lang, ru: "Отмена", en: "Cancelling")) {}.disabled(true)
            } else if model.isBusy {
                Button(L10n.tr(lang, ru: "Отменить", en: "Cancel"), action: model.cancel)
                    .keyboardShortcut(.cancelAction)
            } else {
                Button(action: startDownload) {
                    Label(L10n.tr(lang, ru: "Скачать", en: "Download"), systemImage: "arrow.down.to.line")
                        .frame(minWidth: 120)
                }
                .buttonStyle(.borderedProminent)
                .tint(platformAccent(model.platform))
                .keyboardShortcut(.defaultAction)
                .disabled(!model.canStart)
            }
        }
        .controlSize(.large)
    }

    private func startDownload() {
        guard model.canStart else { return }
        model.start()
        if model.isBusy { linkFocused = false }
    }
}
