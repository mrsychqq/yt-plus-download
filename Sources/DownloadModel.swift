import AppKit
import Foundation
import SwiftUI

@MainActor
final class DownloadModel: ObservableObject {
    enum Status {
        case ready
        case preparing
        case downloading(TransferProgress)
        case downloadingPhoto
        case waitingForNetwork
        case resuming
        case finalizing
        case cancelling
        case cancelled
        case saved(URL, isPhoto: Bool)
        case failed(LocalizedText, detail: String? = nil)
    }

    @Published var platform: VideoPlatform = .youtube {
        didSet {
            if platform != oldValue, case .failed = status { status = .ready }
        }
    }
    @Published var link = "" {
        didSet {
            if link != oldValue {
                if case .failed = status { status = .ready }
                if let detected = MediaLink(link), detected.platform != platform {
                    platform = detected.platform
                }
            }
        }
    }
    @Published var format: DownloadFormat = .mp4
    @Published var quality: VideoQuality = .p1080
    @Published var language: AppLanguage {
        didSet {
            if language != oldValue {
                defaults.set(language.rawValue, forKey: "appLanguage")
            }
        }
    }
    @Published var theme: AppTheme {
        didSet {
            if theme != oldValue {
                defaults.set(theme.rawValue, forKey: "appTheme")
                applyAppearance()
            }
        }
    }
    @Published private(set) var folder: URL
    @Published private(set) var tools: DownloadTools
    @Published private(set) var status = Status.ready
    @Published private(set) var title: String?
    @Published var showFailureDetails = false
    var onDownloadFinished: (() -> Void)?

    private var session: DownloadSession?
    private var activeTask: Task<Void, Never>?
    private var cancelRequested = false
    private let defaults = UserDefaults.standard

    init() {
        let manager = FileManager.default
        let downloads = manager.urls(for: .downloadsDirectory, in: .userDomainMask).first
            ?? manager.homeDirectoryForCurrentUser.appendingPathComponent("Downloads", isDirectory: true)
        if let path = defaults.string(forKey: "downloadFolder") {
            var isDir: ObjCBool = false
            if manager.fileExists(atPath: path, isDirectory: &isDir), isDir.boolValue {
                folder = URL(fileURLWithPath: path, isDirectory: true)
            } else {
                folder = downloads
            }
        } else {
            folder = downloads
        }
        language = defaults.string(forKey: "appLanguage").flatMap(AppLanguage.init(rawValue:)) ?? .ru
        theme = defaults.string(forKey: "appTheme").flatMap(AppTheme.init(rawValue:)) ?? .system
        tools = .locate()
        applyAppearance()
    }

    func applyAppearance() {
        NSApp?.appearance = theme.nsAppearance
    }

    var isBusy: Bool { activeTask != nil || session != nil }
    var canStart: Bool {
        !isBusy && tools.missing.isEmpty && !link.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }

    func refreshTools() {
        tools = .locate()
    }

    func chooseFolder() {
        let panel = NSOpenPanel()
        panel.title = L10n.tr(language, ru: "Папка для скачивания", en: "Download folder")
        panel.prompt = L10n.tr(language, ru: "Выбрать", en: "Choose")
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.canCreateDirectories = true
        panel.directoryURL = folder
        guard panel.runModal() == .OK, let selected = panel.url else { return }
        folder = selected
        defaults.set(folder.path, forKey: "downloadFolder")
        if case .failed = status { status = .ready }
    }

    func pasteLink() {
        if let copied = NSPasteboard.general.string(forType: .string) {
            link = copied.trimmingCharacters(in: .whitespacesAndNewlines)
        }
    }

    func start() {
        guard !isBusy else { return }
        title = nil
        let targetPlatform = platform
        guard let video = MediaLink(link, expected: targetPlatform) else {
            if let other = MediaLink(link) {
                platform = other.platform
                startDownload(video: other)
                return
            }
            status = .failed(L10n.invalidLink(platform: targetPlatform))
            return
        }
        startDownload(video: video)
    }

    private func startDownload(video: MediaLink) {
        refreshTools()
        guard tools.missing.isEmpty else { return }

        let destination = folder
        let selectedFormat = format
        let selectedQuality = quality
        let currentTools = tools
        let manager = FileManager.default
        var isDirectory: ObjCBool = false
        guard manager.fileExists(atPath: destination.path, isDirectory: &isDirectory),
              isDirectory.boolValue, manager.isWritableFile(atPath: destination.path)
        else {
            status = .failed(LocalizedText(
                ru: "Папка недоступна для записи. Выберите другую папку.",
                en: "Folder is not writable. Choose another folder."
            ))
            return
        }

        // Keep one staging directory across network reconnect retries so yt-dlp resumes partial files.
        let stageDirectory: URL?
        if selectedFormat == .mp4 {
            stageDirectory = nil
        } else {
            let directory = destination.appendingPathComponent(".ytplus-\(UUID().uuidString)", isDirectory: true)
            do {
                try manager.createDirectory(at: directory, withIntermediateDirectories: false,
                                            attributes: [.posixPermissions: 0o700])
                stageDirectory = directory
            } catch {
                status = .failed(LocalizedText(
                    ru: "Папка недоступна для записи. Выберите другую папку.",
                    en: "Folder is not writable. Choose another folder."
                ), detail: error.localizedDescription)
                return
            }
        }

        cancelRequested = false
        status = .preparing
        activeTask = Task { [weak self] in
            guard let self else { return }
            defer {
                if let stageDirectory {
                    try? FileManager.default.removeItem(at: stageDirectory)
                }
                self.session = nil
                self.activeTask = nil
                self.cancelRequested = false
                self.finishLifecycle()
            }
            await self.runWithReconnect(
                video: video,
                format: selectedFormat,
                quality: selectedQuality,
                destination: destination,
                stageDirectory: stageDirectory,
                tools: currentTools
            )
        }
    }

    func cancel() {
        guard isBusy else { return }
        cancelRequested = true
        status = .cancelling
        if let session {
            // Keep activeTask alive while yt-dlp/ffmpeg exits so DownloadSession can escalate
            // SIGINT -> SIGTERM -> SIGKILL and stageDirectory is removed only after exit.
            session.cancel()
        } else {
            activeTask?.cancel()
        }
    }

    private func runWithReconnect(
        video: MediaLink,
        format: DownloadFormat,
        quality: VideoQuality,
        destination: URL,
        stageDirectory: URL?,
        tools: DownloadTools
    ) async {
        let maxReconnectAttempts = 60
        var reconnectAttempt = 0

        while true {
            if cancelRequested || Task.isCancelled {
                status = .cancelled
                return
            }

            let currentSession: DownloadSession
            do {
                currentSession = try DownloadSession(
                    video: video,
                    format: format,
                    quality: quality,
                    folder: destination,
                    tools: tools,
                    stageDirectory: stageDirectory,
                    ownsStageDirectory: false
                )
            } catch {
                status = .failed(LocalizedText(
                    ru: "Не удалось запустить загрузку.",
                    en: "Could not start the download."
                ), detail: error.localizedDescription)
                return
            }

            self.session = currentSession
            let outcome = await watch(currentSession, format: format, destination: destination)
            self.session = nil

            switch outcome {
            case .saved(let file):
                status = .saved(file, isPhoto: false)
                return
            case .cancelled:
                status = .cancelled
                return
            case .failed(let exitCode, let detail, let retryError):
                if cancelRequested || Task.isCancelled {
                    status = .cancelled
                    return
                }
                let lowered = detail?.lowercased() ?? ""
                let isPinterestImagePin = video.platform == .pinterest && !format.isAudio &&
                    (lowered.contains("no video formats found") || lowered.contains("unable to extract video"))
                if isPinterestImagePin {
                    await runPinterestPhotoWithReconnect(video: video, destination: destination, originalDetail: detail)
                    return
                }

                if NetworkMonitor.isNetworkFailure(detail: detail, retryError: retryError),
                   reconnectAttempt < maxReconnectAttempts, !cancelRequested, !Task.isCancelled {
                    reconnectAttempt += 1
                    status = .waitingForNetwork
                    let online = await NetworkMonitor.waitForConnection(platform: video.platform)
                    if cancelRequested || Task.isCancelled {
                        status = .cancelled
                        return
                    }
                    if online {
                        status = .resuming
                        continue
                    }
                }

                let combinedDetail = Self.combineErrorDetail(detail, retryError: retryError)
                status = .failed(
                    failureMessage(detail, retryError: retryError, exitCode: exitCode, platform: video.platform, format: format),
                    detail: combinedDetail
                )
                return
            }
        }
    }

    private func runPinterestPhotoWithReconnect(video: MediaLink, destination: URL, originalDetail: String?) async {
        let maxReconnectAttempts = 30
        var reconnectAttempt = 0

        while true {
            if cancelRequested || Task.isCancelled {
                status = .cancelled
                return
            }
            status = .downloadingPhoto
            do {
                let (file, pinTitle) = try await PinterestImageDownloader.download(from: video, to: destination)
                if cancelRequested || Task.isCancelled {
                    status = .cancelled
                } else {
                    if let pinTitle { title = pinTitle }
                    status = .saved(file, isPhoto: true)
                }
                return
            } catch is CancellationError {
                status = .cancelled
                return
            } catch let urlError as URLError where urlError.code == .cancelled {
                status = .cancelled
                return
            } catch {
                if cancelRequested || Task.isCancelled {
                    status = .cancelled
                    return
                }
                if NetworkMonitor.isRetryablePhotoError(error), reconnectAttempt < maxReconnectAttempts {
                    reconnectAttempt += 1
                    status = .waitingForNetwork
                    let online = await NetworkMonitor.waitForConnection(platform: .pinterest)
                    if cancelRequested || Task.isCancelled {
                        status = .cancelled
                        return
                    }
                    if online {
                        status = .resuming
                        continue
                    }
                    status = .failed(
                        L10n.connectionFailed(platform: .pinterest),
                        detail: (error as? PinterestPhotoError)?.technicalDetail
                            ?? (error.localizedDescription.isEmpty ? originalDetail : error.localizedDescription)
                    )
                    return
                }
                if let photoError = error as? PinterestPhotoError {
                    status = .failed(
                        photoError.localizedMessage,
                        detail: photoError.technicalDetail ?? originalDetail
                    )
                } else {
                    status = .failed(
                        LocalizedText(
                            ru: "Не удалось скачать фото из Pinterest.",
                            en: "Could not download photo from Pinterest."
                        ),
                        detail: error.localizedDescription.isEmpty ? originalDetail : error.localizedDescription
                    )
                }
                return
            }
        }
    }

    private enum AttemptOutcome {
        case saved(URL)
        case cancelled
        case failed(Int32, String?, retryError: String?)
    }

    private func watch(_ session: DownloadSession, format: DownloadFormat, destination: URL) async -> AttemptOutcome {
        let alreadyPrefix = "[download] "
        let alreadySuffix = " has already been downloaded"
        var reportedPath: String?
        var existingPath: String?
        var errorLine: String?
        var lastRetryError: String?
        var lastDiagnostic: String?

        for await event in session.events {
            switch event {
            case .title(let name):
                title = name
            case .transfer(let progress):
                if case .cancelling = status { continue }
                status = .downloading(progress)
            case .finalizing:
                if case .cancelling = status { continue }
                status = .finalizing
            case .savedPath(let path):
                reportedPath = path
            case .diagnostic(let line) where !line.isEmpty:
                if line.hasPrefix(alreadyPrefix), line.hasSuffix(alreadySuffix) {
                    let start = line.index(line.startIndex, offsetBy: alreadyPrefix.count)
                    let end = line.index(line.endIndex, offsetBy: -alreadySuffix.count)
                    let candidate = String(line[start..<end]).trimmingCharacters(in: .whitespaces)
                    if !candidate.isEmpty { existingPath = candidate }
                }
                lastDiagnostic = line
                if line.range(of: "got error:", options: .caseInsensitive) != nil {
                    lastRetryError = line
                }
                if line.hasPrefix("ERROR:") {
                    errorLine = line
                }
            case .diagnostic:
                break
            case .finished(let exitCode):
                session.didFinish()
                if exitCode == 0,
                   let file = reportedPath.flatMap({ savedFile($0, format: format, in: destination) })
                    ?? existingPath.flatMap({ savedFile($0, format: format, in: destination) }) {
                    return .saved(file)
                }
                if cancelRequested || Task.isCancelled {
                    return .cancelled
                }
                if case .cancelling = status {
                    return .cancelled
                }
                return .failed(exitCode, errorLine ?? lastDiagnostic, retryError: lastRetryError)
            }
        }

        return (cancelRequested || Task.isCancelled) ? .cancelled : .failed(-1, errorLine ?? lastDiagnostic, retryError: lastRetryError)
    }

    private static func combineErrorDetail(_ detail: String?, retryError: String?) -> String? {
        guard let detail, !detail.isEmpty else { return retryError }
        if detail.lowercased().contains("giving up after"), let retryError, !retryError.isEmpty {
            return "\(detail)\n\(retryError)"
        }
        return detail
    }

    private func finishLifecycle() {
        onDownloadFinished?()
        onDownloadFinished = nil
    }

    private func savedFile(_ path: String, format: DownloadFormat, in destination: URL) -> URL? {
        let file = URL(fileURLWithPath: path, relativeTo: destination).standardizedFileURL
        let directory = file.deletingLastPathComponent().resolvingSymlinksInPath()
        guard directory.path == destination.resolvingSymlinksInPath().path,
              file.pathExtension.lowercased() == format.rawValue,
              let values = try? file.resourceValues(forKeys: [.isRegularFileKey, .isSymbolicLinkKey]),
              values.isRegularFile == true, values.isSymbolicLink == false
        else { return nil }
        return file
    }

    private func failureMessage(_ detail: String?, retryError: String? = nil, exitCode: Int32, platform: VideoPlatform, format: DownloadFormat) -> LocalizedText {
        let message = Self.combineErrorDetail(detail, retryError: retryError)?.lowercased() ?? ""
        if exitCode == 0 { return L10n.missingOutputFile(format.label) }
        if message.contains("no video formats found") || message.contains("unable to extract video") {
            switch platform {
            case .pinterest:
                return format.isAudio
                    ? LocalizedText(
                        ru: "В этом пине только фото, без звуковой дорожки. Выберите MP4, чтобы скачать изображение.",
                        en: "This pin contains only a photo with no audio track. Choose MP4 to download the image."
                    )
                    : LocalizedText(
                        ru: "Не удалось найти видео или фото в этом пине Pinterest.",
                        en: "Could not find a video or photo in this Pinterest pin."
                    )
            case .tiktok:
                return LocalizedText(
                    ru: "В этой публикации TikTok нет видео (возможно, это фото или слайдшоу).",
                    en: "This TikTok post has no video (it may be a photo or slideshow)."
                )
            case .youtube:
                return LocalizedText(
                    ru: "Видео недоступно для скачивания.",
                    en: "Video is unavailable for download."
                )
            }
        }
        if NetworkMonitor.containsCode(message, "10204") || message.contains("ip address is blocked") {
            return LocalizedText(
                ru: "TikTok временно ограничил доступ с этого соединения.",
                en: "TikTok temporarily restricted access from this connection."
            )
        }
        if message.contains("sign in") || message.contains("private video") ||
            message.contains("log into an account") ||
            NetworkMonitor.containsCode(message, "10216") || NetworkMonitor.containsCode(message, "10222") {
            return L10n.signInOrPrivate(platform: platform)
        }
        if message.contains("requested format") {
            return format.isAudio
                ? LocalizedText(
                    ru: "Для этого видео нет доступной звуковой дорожки.",
                    en: "No audio track is available for this video."
                )
                : LocalizedText(
                    ru: "Разрешение недоступно. Попробуйте повысить предел.",
                    en: "This resolution is unavailable. Try increasing the limit."
                )
        }
        if message.contains("http error 403") {
            return L10n.rejectedRequest(platform: platform)
        }
        if message.contains("http error 429") || message.contains("too many requests") {
            return L10n.rateLimited(platform: platform)
        }
        if message.contains("video unavailable") || message.contains("not available") || message.contains("status code") {
            return LocalizedText(
                ru: "Видео недоступно для скачивания.",
                en: "Video is unavailable for download."
            )
        }
        if NetworkMonitor.isNetworkFailure(detail: detail, retryError: retryError) {
            return L10n.connectionFailed(platform: platform)
        }
        if message.contains("no space left") || message.contains("not enough space") {
            return LocalizedText(
                ru: "На диске недостаточно свободного места.",
                en: "Not enough free disk space."
            )
        }
        if message.contains("permission denied") || message.contains("operation not permitted") {
            return LocalizedText(
                ru: "Система ограничила запуск загрузчика или запись файла.",
                en: "System permissions blocked the downloader or file write."
            )
        }
        return LocalizedText(ru: "Не удалось скачать файл.", en: "Could not download file.")
    }
}
