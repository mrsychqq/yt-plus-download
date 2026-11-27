import AppKit
import SwiftUI

enum AppLanguage: String, CaseIterable, Identifiable, Hashable, Sendable {
    case ru
    case en

    var id: String { rawValue }

    var shortCode: String {
        switch self {
        case .ru: "RU"
        case .en: "EN"
        }
    }

    var nativeName: String {
        switch self {
        case .ru: "Русский"
        case .en: "English"
        }
    }

    var toggled: AppLanguage {
        self == .ru ? .en : .ru
    }
}

struct LocalizedText: Hashable, Sendable {
    let ru: String
    let en: String

    func text(_ lang: AppLanguage) -> String {
        lang == .ru ? ru : en
    }
}

enum AppTheme: String, CaseIterable, Identifiable, Hashable, Sendable {
    case system
    case light
    case dark

    var id: String { rawValue }

    var nsAppearance: NSAppearance? {
        switch self {
        case .system: nil
        case .light: NSAppearance(named: .aqua)
        case .dark: NSAppearance(named: .darkAqua)
        }
    }

    var iconName: String {
        switch self {
        case .system: "circle.lefthalf.filled"
        case .light: "sun.max.fill"
        case .dark: "moon.fill"
        }
    }

    func title(_ lang: AppLanguage) -> String {
        switch (self, lang) {
        case (.system, .ru): "Системная"
        case (.light, .ru): "Светлая"
        case (.dark, .ru): "Тёмная"
        case (.system, .en): "System"
        case (.light, .en): "Light"
        case (.dark, .en): "Dark"
        }
    }

    func shortTitle(_ lang: AppLanguage) -> String {
        switch (self, lang) {
        case (.system, .ru): "Авто"
        case (.light, .ru): "Светлая"
        case (.dark, .ru): "Тёмная"
        case (.system, .en): "Auto"
        case (.light, .en): "Light"
        case (.dark, .en): "Dark"
        }
    }
}

enum L10n {
    static func tr(_ lang: AppLanguage, ru: String, en: String) -> String {
        lang == .ru ? ru : en
    }

    static func headerTitle(platform: VideoPlatform, lang: AppLanguage) -> String {
        lang == .ru ? "Скачать с \(platform.title)" : "Download from \(platform.title)"
    }

    static func headerSubtitle(platform: VideoPlatform, lang: AppLanguage) -> String {
        switch (platform, lang) {
        case (.pinterest, .ru): "Видео, фото и звук"
        case (.pinterest, .en): "Video, photo & audio"
        case (_, .ru): "Видео и звук"
        case (_, .en): "Video & audio"
        }
    }

    static func placeholder(platform: VideoPlatform, lang: AppLanguage) -> String {
        switch (platform, lang) {
        case (.youtube, _): "youtube.com/watch?v=…"
        case (.tiktok, .ru): "tiktok.com/@user/video/… или vm.tiktok.com/…"
        case (.tiktok, .en): "tiktok.com/@user/video/… or vm.tiktok.com/…"
        case (.pinterest, .ru): "pinterest.com/pin/… или pin.it/…"
        case (.pinterest, .en): "pinterest.com/pin/… or pin.it/…"
        }
    }

    static func invalidLink(platform: VideoPlatform) -> LocalizedText {
        switch platform {
        case .youtube:
            LocalizedText(
                ru: "Вставьте ссылку на отдельное видео YouTube.",
                en: "Paste a link to a single YouTube video."
            )
        case .tiktok:
            LocalizedText(
                ru: "Вставьте ссылку на отдельное видео TikTok.",
                en: "Paste a link to a single TikTok video."
            )
        case .pinterest:
            LocalizedText(
                ru: "Вставьте ссылку на пин Pinterest (видео или фото).",
                en: "Paste a link to a Pinterest pin (video or photo)."
            )
        }
    }

    static func qualityLabel(_ rawValue: Int, lang: AppLanguage) -> String {
        switch (rawValue, lang) {
        case (0, .ru): "Максимальное"
        case (0, .en): "Best available"
        case (4320, .ru): "До 4320p (8K)"
        case (4320, .en): "Up to 4320p (8K)"
        case (2160, .ru): "До 2160p (4K)"
        case (2160, .en): "Up to 2160p (4K)"
        case (_, .ru): "До \(rawValue)p"
        case (_, .en): "Up to \(rawValue)p"
        }
    }

    static func missingToolsMessage(_ missing: [String], lang: AppLanguage) -> String {
        let joined = missing.joined(separator: ", ")
        return lang == .ru ? "Нужно установить \(joined)" : "Please install \(joined)"
    }

    static func preparingFormat(_ formatLabel: String, lang: AppLanguage) -> String {
        lang == .ru ? "Подготавливаем \(formatLabel)…" : "Preparing \(formatLabel)…"
    }

    static func remaining(_ eta: String, lang: AppLanguage) -> String {
        lang == .ru ? "Осталось \(eta)" : "\(eta) left"
    }

    static func tabAccessibility(_ title: String, lang: AppLanguage) -> String {
        lang == .ru ? "Вкладка \(title)" : "\(title) tab"
    }

    static func themeHelp(_ theme: AppTheme, lang: AppLanguage) -> String {
        lang == .ru ? "Тема: \(theme.title(lang))" : "Theme: \(theme.title(lang))"
    }

    static func languageHelp(_ lang: AppLanguage) -> String {
        lang == .ru
            ? "Язык: Русский (нажмите, чтобы переключить на English)"
            : "Language: English (click to switch to Русский)"
    }

    static func missingOutputFile(_ formatLabel: String) -> LocalizedText {
        LocalizedText(
            ru: "Не удалось найти готовый файл \(formatLabel) в выбранной папке.",
            en: "Could not find the finished \(formatLabel) file in the selected folder."
        )
    }

    static func signInOrPrivate(platform: VideoPlatform) -> LocalizedText {
        LocalizedText(
            ru: "\(platform.title) требует вход в аккаунт или видео скрыто настройками приватности.",
            en: "\(platform.title) requires sign-in or this video is private."
        )
    }

    static func rejectedRequest(platform: VideoPlatform) -> LocalizedText {
        LocalizedText(
            ru: "\(platform.title) временно отклонил запрос. Попробуйте скачать ещё раз.",
            en: "\(platform.title) temporarily rejected the request. Please try again."
        )
    }

    static func rateLimited(platform: VideoPlatform) -> LocalizedText {
        LocalizedText(
            ru: "\(platform.title) временно ограничил запросы. Подождите немного и попробуйте снова.",
            en: "\(platform.title) temporarily rate-limited requests. Wait a moment and try again."
        )
    }

    static func connectionFailed(platform: VideoPlatform) -> LocalizedText {
        LocalizedText(
            ru: "Не удалось подключиться к \(platform.title). Проверьте соединение.",
            en: "Could not connect to \(platform.title). Check your internet connection."
        )
    }
}
