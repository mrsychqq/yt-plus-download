import Darwin
import Foundation

enum VideoPlatform: String, CaseIterable, Identifiable, Hashable {
    case youtube
    case tiktok
    case pinterest

    var id: String { rawValue }

    var title: String {
        switch self {
        case .youtube: "YouTube"
        case .tiktok: "TikTok"
        case .pinterest: "Pinterest"
        }
    }

    var connectivityProbeURL: URL {
        switch self {
        case .youtube: URL(string: "https://www.youtube.com/generate_204")!
        case .tiktok: URL(string: "https://www.tiktok.com/")!
        case .pinterest: URL(string: "https://www.pinterest.com/")!
        }
    }
}

enum NetworkMonitor {
    static func containsCode(_ text: String, _ code: String) -> Bool {
        text.range(of: "\\b\(code)\\b", options: .regularExpression) != nil
    }

    static func isNetworkError(_ detail: String?) -> Bool {
        let text = detail?.lowercased() ?? ""
        guard !text.isEmpty else { return false }
        if text.contains("http error 400") || text.contains("http error 401") ||
            text.contains("http error 403") || text.contains("http error 404") ||
            text.contains("http error 410") || text.contains("http error 429") ||
            text.contains("http error 451") ||
            containsCode(text, "10204") || containsCode(text, "10216") || containsCode(text, "10222") ||
            text.contains("ip address is blocked") || text.contains("log into an account") ||
            text.contains("private video") || text.contains("sign in") ||
            text.contains("requested format") || text.contains("no video formats found") ||
            text.contains("permission denied") || text.contains("no space left") {
            return false
        }
        let markers = [
            "timed out", "timeout", "resolve hostname", "getaddrinfo failed",
            "failed to resolve", "temporary failure in name resolution",
            "name or service not known", "nodename nor servname",
            "network is unreachable", "network is down", "no route to host",
            "host is down", "connection reset", "connection refused",
            "connection aborted", "connection closed", "remotedisconnected",
            "broken pipe", "eof occurred", "incomplete read", "incompleteread",
            "did not get any data blocks", "urlerror", "timeouterror",
            "connectionerror", "remote end closed",
            "unable to download webpage", "unable to download video data",
            "unable to download api", "transport error", "sslerror",
            "winerror 10051", "winerror 10053", "winerror 10054", "winerror 10060",
            "winerror 10061", "winerror 10065", "winerror 11001", "winerror 11002"
        ]
        return markers.contains { text.contains($0) }
    }

    static func isNetworkFailure(detail: String?, retryError: String?) -> Bool {
        if isNetworkError(detail) { return true }
        let text = detail?.lowercased() ?? ""
        if text.contains("giving up after") {
            return isNetworkError(retryError)
        }
        return false
    }

    static func isNetworkURLError(_ error: Error) -> Bool {
        guard let urlError = error as? URLError else { return false }
        switch urlError.code {
        case .notConnectedToInternet, .networkConnectionLost, .timedOut,
             .cannotFindHost, .cannotConnectToHost, .dnsLookupFailed,
             .internationalRoamingOff, .dataNotAllowed, .secureConnectionFailed:
            return true
        default:
            return false
        }
    }

    static func isRetryablePhotoError(_ error: Error) -> Bool {
        if isNetworkURLError(error) { return true }
        if case PinterestPhotoError.transientHTTP = error { return true }
        return false
    }

    static func waitForConnection(platform: VideoPlatform, maxWaitSeconds: TimeInterval = 1800) async -> Bool {
        let deadline = Date().addingTimeInterval(maxWaitSeconds)
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 4
        config.timeoutIntervalForResource = 5
        config.waitsForConnectivity = false
        let session = URLSession(configuration: config)
        defer { session.invalidateAndCancel() }

        var request = URLRequest(url: platform.connectivityProbeURL)
        request.httpMethod = "HEAD"
        request.cachePolicy = .reloadIgnoringLocalAndRemoteCacheData

        while Date() < deadline {
            if Task.isCancelled { return false }
            do {
                let (_, response) = try await session.data(for: request)
                if response is HTTPURLResponse {
                    return true
                }
            } catch is CancellationError {
                return false
            } catch {
                // Still offline; retry after a short delay.
            }
            do {
                try await Task.sleep(for: .milliseconds(2500))
            } catch {
                return false
            }
        }
        return false
    }
}

struct MediaLink: Hashable {
    let platform: VideoPlatform
    let url: URL

    init?(_ input: String, expected: VideoPlatform? = nil) {
        let address = input.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !address.isEmpty else { return nil }
        let link = address.contains("://") ? address : "https://" + address
        guard let components = URLComponents(string: link),
              let scheme = components.scheme?.lowercased(),
              ["https", "http"].contains(scheme),
              components.user == nil, components.password == nil,
              components.port == nil ||
                  (scheme == "https" && components.port == 443) ||
                  (scheme == "http" && components.port == 80),
              let host = components.host?.lowercased()
        else { return nil }

        let path = components.path.split(separator: "/").map(String.init)
        let parsed: (VideoPlatform, URL)? =
            Self.parseYouTube(host: host, path: path, queryItems: components.queryItems) ??
            Self.parseTikTok(host: host, path: path) ??
            Self.parsePinterest(host: host, path: path)

        guard let (platform, canonical) = parsed else { return nil }
        if let expected, expected != platform { return nil }
        self.platform = platform
        self.url = canonical
    }

    static func extractPinterestPinId(from url: URL) -> String? {
        guard let host = url.host?.lowercased(), isPinterestHost(host) else { return nil }
        let path = url.path.split(separator: "/").map(String.init)
        guard path.count >= 2, path[0] == "pin" else { return nil }
        let segment = path[1]
        if isDigits(segment) {
            return segment.count >= 5 ? segment : nil
        }
        if let range = segment.range(of: "--", options: .backwards) {
            let tail = String(segment[range.upperBound...])
            if isDigits(tail), tail.count >= 5 { return tail }
        }
        return nil
    }

    private static func isASCIIAlphaNumDashUnderscore(_ text: String) -> Bool {
        !text.isEmpty && text.utf8.allSatisfy {
            (65...90).contains($0) || (97...122).contains($0) ||
            (48...57).contains($0) || $0 == 45 || $0 == 95
        }
    }

    private static func isDigits(_ text: String) -> Bool {
        !text.isEmpty && text.utf8.allSatisfy { (48...57).contains($0) }
    }

    private static func parseYouTube(host: String, path: [String], queryItems: [URLQueryItem]?) -> (VideoPlatform, URL)? {
        let id: String?
        switch host {
        case "youtu.be":
            id = path.count == 1 ? path.first : nil
        case "youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com":
            if path == ["watch"] {
                id = queryItems?.first { $0.name == "v" }?.value
            } else if path.count == 2, ["shorts", "live", "embed"].contains(path[0]) {
                id = path[1]
            } else {
                id = nil
            }
        default:
            return nil
        }

        guard let id, id.utf8.count == 11, isASCIIAlphaNumDashUnderscore(id),
              let url = URL(string: "https://www.youtube.com/watch?v=\(id)")
        else { return nil }
        return (.youtube, url)
    }

    private static func parseTikTok(host: String, path: [String]) -> (VideoPlatform, URL)? {
        switch host {
        case "vm.tiktok.com", "vt.tiktok.com":
            guard path.count == 1,
                  (4...32).contains(path[0].utf8.count),
                  isASCIIAlphaNumDashUnderscore(path[0]),
                  let url = URL(string: "https://\(host)/\(path[0])/")
            else { return nil }
            return (.tiktok, url)

        case "tiktok.com", "www.tiktok.com", "m.tiktok.com", "www.tiktokv.com":
            if path.count >= 3, path[0].hasPrefix("@"), path[0].count > 1,
               path[1] == "video", isDigits(path[2]), path[2].count >= 8,
               let url = URL(string: "https://www.tiktok.com/\(path[0])/video/\(path[2])") {
                return (.tiktok, url)
            }
            if path.count == 3, path[0] == "share", path[1] == "video",
               isDigits(path[2]), path[2].count >= 8,
               let url = URL(string: "https://www.tiktok.com/share/video/\(path[2])") {
                return (.tiktok, url)
            }
            if path.count == 2, path[0] == "t",
               (4...32).contains(path[1].utf8.count),
               isASCIIAlphaNumDashUnderscore(path[1]),
               let url = URL(string: "https://www.tiktok.com/t/\(path[1])/") {
                return (.tiktok, url)
            }
            if path.count == 2, path[0] == "v" {
                let rawId = path[1].hasSuffix(".html") ? String(path[1].dropLast(5)) : path[1]
                if isDigits(rawId), rawId.count >= 8,
                   let url = URL(string: "https://m.tiktok.com/v/\(rawId).html") {
                    return (.tiktok, url)
                }
            }
            if path.count == 2, path[0] == "embed", isDigits(path[1]), path[1].count >= 8,
               let url = URL(string: "https://www.tiktok.com/embed/v2/\(path[1])") {
                return (.tiktok, url)
            }
            if path.count == 3, path[0] == "embed", path[1] == "v2", isDigits(path[2]), path[2].count >= 8,
               let url = URL(string: "https://www.tiktok.com/embed/v2/\(path[2])") {
                return (.tiktok, url)
            }
            return nil

        default:
            return nil
        }
    }

    private static let pinterestTLDs: Set<String> = [
        "com", "ru", "co.uk", "de", "fr", "es", "it", "ca", "jp", "at", "ch",
        "se", "dk", "ie", "nz", "pt", "cl", "ph", "kr", "nl", "pl", "tr", "in",
        "id", "com.au", "com.mx", "co.kr", "com.br"
    ]

    private static func isPinterestHost(_ host: String) -> Bool {
        let parts = host.split(separator: ".").map(String.init)
        guard let idx = parts.firstIndex(of: "pinterest") else { return false }
        if idx > 1 { return false }
        if idx == 1 {
            let sub = parts[0]
            guard (1...4).contains(sub.count),
                  sub.utf8.allSatisfy({ (97...122).contains($0) })
            else { return false }
        }
        let tld = parts[(idx + 1)...].joined(separator: ".")
        return pinterestTLDs.contains(tld)
    }

    private static func parsePinterest(host: String, path: [String]) -> (VideoPlatform, URL)? {
        if host == "pin.it" {
            guard path.count == 1,
                  (4...32).contains(path[0].utf8.count),
                  isASCIIAlphaNumDashUnderscore(path[0]),
                  let url = URL(string: "https://pin.it/\(path[0])")
            else { return nil }
            return (.pinterest, url)
        }

        guard isPinterestHost(host), path.count >= 2, path[0] == "pin" else { return nil }
        let segment = path[1]
        let pinId: String
        if isDigits(segment) {
            pinId = segment
        } else if let range = segment.range(of: "--", options: .backwards) {
            let tail = String(segment[range.upperBound...])
            guard isDigits(tail) else { return nil }
            pinId = tail
        } else {
            return nil
        }
        guard pinId.count >= 5,
              let url = URL(string: "https://www.pinterest.com/pin/\(pinId)/")
        else { return nil }
        return (.pinterest, url)
    }
}

enum PinterestPhotoError: Error, Sendable {
    case imageNotFound
    case invalidImageHost
    case transientHTTP(Int)

    var localizedMessage: LocalizedText {
        switch self {
        case .imageNotFound:
            LocalizedText(
                ru: "Не удалось найти видео или фото в этом пине Pinterest.",
                en: "Could not find a video or photo in this Pinterest pin."
            )
        case .invalidImageHost:
            LocalizedText(
                ru: "Pinterest вернул некорректный адрес изображения.",
                en: "Pinterest returned an invalid image address."
            )
        case .transientHTTP:
            LocalizedText(
                ru: "Не удалось подключиться к Pinterest. Проверьте соединение.",
                en: "Could not connect to Pinterest. Check your internet connection."
            )
        }
    }

    var technicalDetail: String? {
        switch self {
        case .imageNotFound: nil
        case .invalidImageHost: "Invalid image host (expected *.pinimg.com over HTTPS)."
        case .transientHTTP(let code): "Pinterest returned HTTP \(code)."
        }
    }
}

private final class PinimgRedirectDelegate: NSObject, URLSessionTaskDelegate, @unchecked Sendable {
    func urlSession(
        _ session: URLSession,
        task: URLSessionTask,
        willPerformHTTPRedirection response: HTTPURLResponse,
        newRequest request: URLRequest,
        completionHandler: @escaping (URLRequest?) -> Void
    ) {
        if PinterestImageDownloader.isAllowedPinimgURL(request.url) {
            completionHandler(request)
        } else {
            completionHandler(nil)
        }
    }
}

enum PinterestImageDownloader {
    private static let userAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"

    static func isAllowedPinimgURL(_ url: URL?) -> Bool {
        guard let url,
              url.scheme?.lowercased() == "https",
              let host = url.host?.lowercased()
        else { return false }
        return host == "i.pinimg.com" || host.hasSuffix(".pinimg.com")
    }

    static func download(from link: MediaLink, to folder: URL) async throws -> (file: URL, title: String?) {
        let sessionConfig = URLSessionConfiguration.ephemeral
        sessionConfig.timeoutIntervalForRequest = 20
        sessionConfig.timeoutIntervalForResource = 60
        let session = URLSession(configuration: sessionConfig)
        defer { session.invalidateAndCancel() }

        let pinId = try await resolvePinId(from: link.url, session: session)
        let (imageURL, title) = try await fetchImageMetadata(pinId: pinId, session: session)

        guard isAllowedPinimgURL(imageURL) else {
            throw PinterestPhotoError.invalidImageHost
        }

        let imageSession = URLSession(configuration: sessionConfig, delegate: PinimgRedirectDelegate(), delegateQueue: nil)
        defer { imageSession.invalidateAndCancel() }

        var imageRequest = URLRequest(url: imageURL)
        imageRequest.setValue(userAgent, forHTTPHeaderField: "User-Agent")
        imageRequest.setValue("https://www.pinterest.com/", forHTTPHeaderField: "Referer")
        let (data, response) = try await imageSession.data(for: imageRequest)
        guard let http = response as? HTTPURLResponse else {
            throw PinterestPhotoError.imageNotFound
        }
        if (300...399).contains(http.statusCode) {
            throw PinterestPhotoError.invalidImageHost
        }
        guard isAllowedPinimgURL(response.url ?? imageURL) else {
            throw PinterestPhotoError.invalidImageHost
        }
        if http.statusCode == 408 || (500...599).contains(http.statusCode) {
            throw PinterestPhotoError.transientHTTP(http.statusCode)
        }
        guard (200...299).contains(http.statusCode), !data.isEmpty else {
            throw PinterestPhotoError.imageNotFound
        }

        let ext = detectExtension(data: data, contentType: http.value(forHTTPHeaderField: "Content-Type"), url: imageURL)
        let baseName = sanitizeFileName(title) ?? "Pinterest"
        let fileName = "\(baseName) [\(pinId)].\(ext)"
        let destination = folder.appendingPathComponent(fileName, isDirectory: false)

        if FileManager.default.fileExists(atPath: destination.path) {
            let values = try? destination.resourceValues(forKeys: [.isRegularFileKey, .isSymbolicLinkKey])
            if values?.isRegularFile == true, values?.isSymbolicLink == false {
                return (destination, title)
            }
            throw CocoaError(.fileWriteFileExists)
        }

        let tempFile = folder.appendingPathComponent(".ytplus-photo-\(UUID().uuidString).tmp", isDirectory: false)
        do {
            try data.write(to: tempFile, options: .atomic)
            try FileManager.default.moveItem(at: tempFile, to: destination)
            return (destination, title)
        } catch {
            try? FileManager.default.removeItem(at: tempFile)
            throw error
        }
    }

    private static func resolvePinId(from url: URL, session: URLSession) async throws -> String {
        if let direct = MediaLink.extractPinterestPinId(from: url) {
            return direct
        }
        var request = URLRequest(url: url)
        request.setValue(userAgent, forHTTPHeaderField: "User-Agent")
        let (_, response) = try await session.data(for: request)
        if let http = response as? HTTPURLResponse, http.statusCode == 408 || (500...599).contains(http.statusCode) {
            throw PinterestPhotoError.transientHTTP(http.statusCode)
        }
        if let finalURL = response.url, let resolved = MediaLink.extractPinterestPinId(from: finalURL) {
            return resolved
        }
        throw PinterestPhotoError.imageNotFound
    }

    private static func fetchImageMetadata(pinId: String, session: URLSession) async throws -> (URL, String?) {
        let optionsPayload = "{\"options\":{\"field_set_key\":\"unauth_react_main_pin\",\"id\":\"\(pinId)\"}}"
        var components = URLComponents(string: "https://www.pinterest.com/resource/PinResource/get/")!
        components.queryItems = [URLQueryItem(name: "data", value: optionsPayload)]
        var apiRequest = URLRequest(url: components.url!)
        apiRequest.setValue(userAgent, forHTTPHeaderField: "User-Agent")
        apiRequest.setValue("www/[username].js", forHTTPHeaderField: "X-Pinterest-PWS-Handler")

        if let (data, response) = try? await session.data(for: apiRequest),
           let http = response as? HTTPURLResponse, (200...299).contains(http.statusCode),
           let parsed = parsePinResourceJSON(data) {
            return parsed
        }

        // Fallback: parse pin webpage for i.pinimg.com/originals/... URL
        let pageURL = URL(string: "https://www.pinterest.com/pin/\(pinId)/")!
        var pageRequest = URLRequest(url: pageURL)
        pageRequest.setValue(userAgent, forHTTPHeaderField: "User-Agent")
        let (htmlData, pageResponse) = try await session.data(for: pageRequest)
        if let http = pageResponse as? HTTPURLResponse {
            if http.statusCode == 408 || (500...599).contains(http.statusCode) {
                throw PinterestPhotoError.transientHTTP(http.statusCode)
            }
            if (200...299).contains(http.statusCode),
               let html = String(data: htmlData, encoding: .utf8),
               let fallback = parsePinHTML(html) {
                return fallback
            }
        }

        throw PinterestPhotoError.imageNotFound
    }

    static func parsePinResourceJSON(_ data: Data) -> (URL, String?)? {
        guard let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let resourceResponse = root["resource_response"] as? [String: Any],
              let pin = resourceResponse["data"] as? [String: Any]
        else { return nil }

        let title = [pin["title"], pin["grid_title"], pin["seo_title"]]
            .compactMap { ($0 as? String)?.trimmingCharacters(in: .whitespacesAndNewlines) }
            .first { !$0.isEmpty }

        if let images = pin["images"] as? [String: Any] {
            if let orig = images["orig"] as? [String: Any],
               let rawURL = orig["url"] as? String,
               let url = URL(string: rawURL) {
                return (url, title)
            }
            var bestURL: URL?
            var bestArea = -1
            for value in images.values {
                guard let entry = value as? [String: Any],
                      let rawURL = entry["url"] as? String,
                      let url = URL(string: rawURL)
                else { continue }
                let area = (entry["width"] as? Int ?? 0) * (entry["height"] as? Int ?? 0)
                if area > bestArea {
                    bestArea = area
                    bestURL = url
                }
            }
            if let bestURL { return (bestURL, title) }
        }

        // Idea / Story pins store images inside story_pin_data.pages[].blocks[].image.images
        if let story = pin["story_pin_data"] as? [String: Any],
           let pages = story["pages"] as? [[String: Any]] {
            for page in pages {
                if let blocks = page["blocks"] as? [[String: Any]] {
                    for block in blocks {
                        if let image = block["image"] as? [String: Any],
                           let imgs = image["images"] as? [String: Any] {
                            for key in ["originals", "orig", "1200x", "736x"] {
                                if let entry = imgs[key] as? [String: Any],
                                   let rawURL = entry["url"] as? String,
                                   let url = URL(string: rawURL) {
                                    return (url, title)
                                }
                            }
                        }
                    }
                }
                if let image = page["image"] as? [String: Any],
                   let imgs = image["images"] as? [String: Any] {
                    for key in ["originals", "orig", "1200x", "736x"] {
                        if let entry = imgs[key] as? [String: Any],
                           let rawURL = entry["url"] as? String,
                           let url = URL(string: rawURL) {
                            return (url, title)
                        }
                    }
                }
            }
        }

        return nil
    }

    private static func parsePinHTML(_ html: String) -> (URL, String?)? {
        let pattern = #"https://i\.pinimg\.com/originals/[A-Za-z0-9/_\-\.]+\.(?:jpg|jpeg|png|webp|gif)"#
        if let regex = try? NSRegularExpression(pattern: pattern, options: .caseInsensitive),
           let match = regex.firstMatch(in: html, range: NSRange(html.startIndex..., in: html)),
           let range = Range(match.range, in: html),
           let url = URL(string: String(html[range])) {
            return (url, nil)
        }
        return nil
    }

    static func detectExtension(data: Data, contentType: String?, url: URL) -> String {
        let bytes = [UInt8](data.prefix(12))
        if bytes.count >= 8, bytes[0] == 0x89, bytes[1] == 0x50, bytes[2] == 0x4E, bytes[3] == 0x47 {
            return "png"
        }
        if bytes.count >= 6, bytes[0] == 0x47, bytes[1] == 0x49, bytes[2] == 0x46, bytes[3] == 0x38 {
            return "gif"
        }
        if bytes.count >= 12,
           bytes[0] == 0x52, bytes[1] == 0x49, bytes[2] == 0x46, bytes[3] == 0x46,
           bytes[8] == 0x57, bytes[9] == 0x45, bytes[10] == 0x42, bytes[11] == 0x50 {
            return "webp"
        }
        if bytes.count >= 3, bytes[0] == 0xFF, bytes[1] == 0xD8, bytes[2] == 0xFF {
            return "jpg"
        }
        if let mime = contentType?.lowercased() {
            if mime.contains("png") { return "png" }
            if mime.contains("webp") { return "webp" }
            if mime.contains("gif") { return "gif" }
            if mime.contains("jpeg") || mime.contains("jpg") { return "jpg" }
        }
        let pathExt = url.pathExtension.lowercased()
        if ["jpg", "jpeg", "png", "webp", "gif"].contains(pathExt) {
            return pathExt == "jpeg" ? "jpg" : pathExt
        }
        return "jpg"
    }

    static func sanitizeFileName(_ title: String?) -> String? {
        guard let title else { return nil }
        let forbidden = CharacterSet(charactersIn: "/\\:*?\"<>|\r\n\t")
        let cleaned = title.components(separatedBy: forbidden).joined(separator: " ")
            .replacingOccurrences(of: #"\s+"#, with: " ", options: .regularExpression)
            .trimmingCharacters(in: CharacterSet(charactersIn: " ."))
        guard !cleaned.isEmpty else { return nil }
        return String(cleaned.prefix(140)).trimmingCharacters(in: CharacterSet(charactersIn: " ."))
    }
}

private struct DownloadProfiles: Decodable {
    static let shared = try! JSONDecoder().decode(Self.self, from: Data(embeddedDownloadProfiles.utf8))

    let resolutions: [Int]
    let commonArguments: [String]
    let formats: [DownloadFormat]
}

struct VideoQuality: Identifiable, Hashable {
    static let allCases = DownloadProfiles.shared.resolutions.map(Self.init(rawValue:))
    static let p1080 = Self(rawValue: 1080)

    let rawValue: Int
    var id: Int { rawValue }
}

struct DownloadFormat: Decodable, Identifiable, Hashable {
    static let allCases = DownloadProfiles.shared.formats
    static let mp4 = allCases.first { $0.rawValue == "mp4" }!

    let rawValue: String
    let label: String
    let isAudio: Bool
    let options: [String]
    var id: String { rawValue }

    private enum CodingKeys: String, CodingKey {
        case rawValue = "extension"
        case label, isAudio, options
    }

    var outputTemplate: String {
        let resolution = isAudio ? "" : "%(height& [{}p]|)s"
        // Keep the intermediate filenames distinct from other output formats.
        let format = rawValue == "mp4" ? "" : " [\(label)]"
        return "%(title).170B [%(id)s]\(resolution)\(format).%(ext)s"
    }

    func arguments(quality: VideoQuality, platform: VideoPlatform = .youtube) -> [String] {
        if isAudio {
            return ["--format", "ba/b", "--extract-audio", "--audio-format", rawValue] + options
        }
        // Use yt-dlp's `res:<height>` sort (evaluates the smaller side) across all platforms
        // so both horizontal videos and vertical videos (YouTube Shorts, TikTok, Pinterest)
        // pick the requested tier instead of dropping 1080x1920 down to 480p.
        if quality.rawValue == 0 {
            return ["--format", "bv*+ba/b"] + options
        }
        var adjusted = options
        if let idx = adjusted.firstIndex(of: "--format-sort"), idx + 1 < adjusted.count {
            let current = adjusted[idx + 1]
            if current.hasPrefix("res,") {
                adjusted[idx + 1] = "res:\(quality.rawValue)," + current.dropFirst(4)
            } else if current == "res" {
                adjusted[idx + 1] = "res:\(quality.rawValue)"
            } else {
                adjusted[idx + 1] = "res:\(quality.rawValue),\(current)"
            }
        } else {
            adjusted += ["--format-sort", "res:\(quality.rawValue)"]
        }
        return ["--format", "bv*+ba/b"] + adjusted
    }
}

struct DownloadTools {
    let ytDLP: URL?
    let ffmpegDirectory: URL?
    let runtimes: [String]
    let environment: [String: String]

    static func locate() -> Self {
        let manager = FileManager.default
        let home = manager.homeDirectoryForCurrentUser
        var environment = ProcessInfo.processInfo.environment
        var locations: [String] = []
        if let bundledTools = Bundle.main.resourceURL?.appendingPathComponent("tools", isDirectory: true).path {
            locations.append(bundledTools)
        }
        locations += (environment["PATH"] ?? "").split(separator: ":").map(String.init) + [
            home.appendingPathComponent(".local/bin").path,
            home.appendingPathComponent(".local/node/bin").path,
            home.appendingPathComponent(".deno/bin").path,
            home.appendingPathComponent("bin").path,
            "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/bin"
        ]
        var seen = Set<String>()
        let directories = locations.filter { $0.hasPrefix("/") && seen.insert($0).inserted }
            .map { URL(fileURLWithPath: $0, isDirectory: true) }
        func executable(_ name: String, in directory: URL) -> URL? {
            let url = directory.appendingPathComponent(name)
            var isDirectory: ObjCBool = false
            guard manager.fileExists(atPath: url.path, isDirectory: &isDirectory),
                  !isDirectory.boolValue, manager.isExecutableFile(atPath: url.path)
            else { return nil }
            return url
        }

        let ytDLP = directories.lazy.compactMap { executable("yt-dlp", in: $0) }.first
        let ffmpegDirectory = directories.first {
            executable("ffmpeg", in: $0) != nil && executable("ffprobe", in: $0) != nil
        }
        let runtimes = ["deno", "node"].compactMap { name -> String? in
            guard let url = directories.lazy.compactMap({ executable(name, in: $0) }).first
            else { return nil }
            return "\(name):\(url.path)"
        }
        environment["PATH"] = directories.map(\.path).joined(separator: ":")
        environment["PYTHONIOENCODING"] = "utf-8"
        environment["PYTHONUTF8"] = "1"
        return Self(ytDLP: ytDLP, ffmpegDirectory: ffmpegDirectory, runtimes: runtimes, environment: environment)
    }

    func missingNames(lang: AppLanguage = .ru) -> [String] {
        var names: [String] = []
        if ytDLP == nil { names.append("yt-dlp") }
        if ffmpegDirectory == nil { names.append(lang == .ru ? "FFmpeg и FFprobe" : "FFmpeg & FFprobe") }
        if runtimes.isEmpty { names.append(lang == .ru ? "Deno или Node.js" : "Deno or Node.js") }
        return names
    }

    var missing: [String] { missingNames(lang: .ru) }
}

struct TransferProgress {
    let isAudio: Bool
    let fraction: Double?
    let speed: String?
    let remaining: String?

    init?(_ fields: [String]) {
        guard fields.count == 5 else { return nil }
        isAudio = fields[1] == "none"
        let number = fields[2].trimmingCharacters(in: CharacterSet(charactersIn: " %"))
        fraction = Double(number).flatMap { $0.isFinite ? min(1, max(0, $0 / 100)) : nil }
        speed = Self.measurement(fields[3])
        remaining = Self.measurement(fields[4])
    }

    private static func measurement(_ text: String) -> String? {
        let value = text.trimmingCharacters(in: .whitespaces)
        return !value.isEmpty && value != "NA" && value.lowercased() != "unknown" ? value : nil
    }
}

enum DownloadEvent {
    case title(String)
    case transfer(TransferProgress)
    case finalizing
    case savedPath(String)
    case diagnostic(String)
    case finished(Int32)

    init(line: Data) {
        let text = String(decoding: line, as: UTF8.self)
        let fields = text.components(separatedBy: "\t")
        switch fields.first {
        case "YTPLUS_TITLE", "YTPLUS_SAVED":
            if fields.count == 2,
               let value = try? JSONDecoder().decode(String.self, from: Data(fields[1].utf8)) {
                self = fields[0] == "YTPLUS_TITLE" ? .title(value) : .savedPath(value)
                return
            }
        case "YTPLUS_TRANSFER":
            if let progress = TransferProgress(fields) {
                self = .transfer(progress)
                return
            }
        case "YTPLUS_FINALIZING":
            self = .finalizing
            return
        default:
            break
        }
        self = .diagnostic(text.trimmingCharacters(in: .whitespacesAndNewlines))
    }
}

@MainActor
final class DownloadSession {
    private struct TrackedProcess: Hashable {
        let pid: pid_t
        let startSec: UInt64
        let startUsec: UInt64
    }

    let events: AsyncStream<DownloadEvent>
    private let process: Process
    private var stopTask: Task<Void, Never>?
    private var trackedDescendants: [TrackedProcess] = []

    init(video: MediaLink, format: DownloadFormat, quality: VideoQuality, folder: URL, tools: DownloadTools,
         stageDirectory: URL? = nil, ownsStageDirectory: Bool = true) throws {
        guard let executable = tools.ytDLP, let ffmpegDirectory = tools.ffmpegDirectory,
              !tools.runtimes.isEmpty
        else { throw CocoaError(.executableNotLoadable) }

        let pipe = Pipe()
        let process = Process()
        process.executableURL = executable
        process.currentDirectoryURL = folder
        process.environment = tools.environment
        process.standardInput = FileHandle.nullDevice
        process.standardOutput = pipe
        process.standardError = pipe
        var arguments = DownloadProfiles.shared.commonArguments + [
            "--ffmpeg-location", ffmpegDirectory.path,
            "--output", format.outputTemplate
        ] + format.arguments(quality: quality, platform: video.platform)
        for runtime in tools.runtimes {
            arguments += ["--js-runtimes", runtime]
        }

        // A cancelled conversion can leave a playable-looking partial output.
        // Keep conversion output private until yt-dlp moves the finished file.
        // When stageDirectory is supplied by the caller (for network resume), caller owns cleanup.
        let temporaryDirectory: URL?
        let shouldCleanup: Bool
        if let stageDirectory {
            temporaryDirectory = stageDirectory
            shouldCleanup = ownsStageDirectory
            arguments += ["--paths", "temp:\(stageDirectory.lastPathComponent)"]
        } else if format == .mp4 {
            temporaryDirectory = nil
            shouldCleanup = false
        } else {
            let directory = folder.appendingPathComponent(".ytplus-\(UUID().uuidString)", isDirectory: true)
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: false,
                                                    attributes: [.posixPermissions: 0o700])
            temporaryDirectory = directory
            shouldCleanup = true
            arguments += ["--paths", "temp:\(directory.lastPathComponent)"]
        }
        let removeTemporaryDirectory: @Sendable () -> Void = {
            if shouldCleanup, let temporaryDirectory { try? FileManager.default.removeItem(at: temporaryDirectory) }
        }
        process.arguments = arguments + ["--", video.url.absoluteString]

        let (events, continuation) = AsyncStream<DownloadEvent>.makeStream()
        self.events = events
        self.process = process
        do {
            try process.run()
        } catch {
            removeTemporaryDirectory()
            throw error
        }
        pipe.fileHandleForWriting.closeFile()

        DispatchQueue.global(qos: .utility).async {
            let reader = pipe.fileHandleForReading
            defer { reader.closeFile() }
            var pending = Data()
            while true {
                let chunk = reader.availableData
                if chunk.isEmpty { break }
                pending.append(chunk)
                while let newline = pending.firstIndex(of: 10) {
                    continuation.yield(DownloadEvent(line: pending[..<newline]))
                    pending.removeSubrange(...newline)
                }
            }
            if !pending.isEmpty {
                continuation.yield(DownloadEvent(line: pending))
            }
            process.waitUntilExit()
            removeTemporaryDirectory()
            continuation.yield(.finished(process.terminationStatus))
            continuation.finish()
        }
    }

    func cancel() {
        guard stopTask == nil else { return }
        sendSignal(SIGINT)
        stopTask = Task { [weak self] in
            try? await Task.sleep(for: .seconds(2))
            guard !Task.isCancelled else { return }
            self?.sendSignal(SIGTERM)
            try? await Task.sleep(for: .seconds(2))
            guard !Task.isCancelled else { return }
            self?.sendSignal(SIGKILL)
        }
    }

    func didFinish() {
        stopTask?.cancel()
        trackedDescendants.removeAll()
    }

    private func sendSignal(_ signal: Int32) {
        let rootPID = process.processIdentifier
        if process.isRunning, rootPID > 0 {
            let current = Self.descendantPIDs(of: rootPID)
            var seen = Set(trackedDescendants)
            for child in current {
                if let tracked = Self.snapshotProcess(child), seen.insert(tracked).inserted {
                    trackedDescendants.append(tracked)
                }
            }
        }
        // Signal descendants bottom-up (ffmpeg, node, deno) only if their start timestamp still matches
        // to avoid signalling an unrelated reused PID.
        for child in trackedDescendants.reversed() {
            if let current = Self.snapshotProcess(child.pid), current == child {
                kill(child.pid, signal)
            }
        }
        if process.isRunning, rootPID > 0 {
            let target = getpgid(rootPID) == rootPID ? -rootPID : rootPID
            kill(target, signal)
        }
    }

    private static func snapshotProcess(_ pid: pid_t) -> TrackedProcess? {
        var info = proc_bsdinfo()
        let size = Int32(MemoryLayout<proc_bsdinfo>.stride)
        let actual = proc_pidinfo(pid, PROC_PIDTBSDINFO, 0, &info, size)
        guard actual == size else { return nil }
        return TrackedProcess(pid: pid, startSec: info.pbi_start_tvsec, startUsec: info.pbi_start_tvusec)
    }

    private static func descendantPIDs(of root: pid_t) -> [pid_t] {
        var result: [pid_t] = []
        var visited: Set<pid_t> = [root]
        var queue: [pid_t] = [root]
        while !queue.isEmpty {
            let parent = queue.removeFirst()
            let estimated = proc_listchildpids(parent, nil, 0)
            guard estimated > 0 else { continue }
            var buffer = [pid_t](repeating: 0, count: max(Int(estimated), 16) + 16)
            let count = buffer.withUnsafeMutableBytes { raw in
                proc_listchildpids(parent, raw.baseAddress, Int32(raw.count))
            }
            guard count > 0 else { continue }
            for index in 0..<min(Int(count), buffer.count) {
                let child = buffer[index]
                if child > 0, visited.insert(child).inserted {
                    result.append(child)
                    queue.append(child)
                }
            }
        }
        return result
    }
}

@_silgen_name("proc_listchildpids")
private func proc_listchildpids(_ ppid: pid_t, _ buffer: UnsafeMutableRawPointer?, _ buffersize: Int32) -> Int32
