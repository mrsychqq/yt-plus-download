import AppKit
import CoreGraphics
import Foundation

let cwd = URL(fileURLWithPath: FileManager.default.currentDirectoryPath)
let iconURL = cwd.appendingPathComponent("Resources/YTPlusIcon.png")
guard let iconImage = NSImage(contentsOf: iconURL) else {
    fputs("Failed to load Resources/YTPlusIcon.png\n", stderr)
    exit(1)
}

let version = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "1.3.0"

func makeBitmap(width: Int, height: Int, draw: (CGContext, CGSize) -> Void) -> NSBitmapImageRep {
    let rep = NSBitmapImageRep(
        bitmapDataPlanes: nil,
        pixelsWide: width,
        pixelsHigh: height,
        bitsPerSample: 8,
        samplesPerPixel: 4,
        hasAlpha: true,
        isPlanar: false,
        colorSpaceName: .deviceRGB,
        bytesPerRow: width * 4,
        bitsPerPixel: 32
    )!
    rep.size = NSSize(width: width, height: height)
    NSGraphicsContext.saveGraphicsState()
    let ctx = NSGraphicsContext(bitmapImageRep: rep)!
    NSGraphicsContext.current = ctx
    ctx.imageInterpolation = .high
    ctx.shouldAntialias = true
    draw(ctx.cgContext, CGSize(width: width, height: height))
    NSGraphicsContext.restoreGraphicsState()
    return rep
}

func savePNG(_ rep: NSBitmapImageRep, to path: String, dpi: CGFloat = 72) {
    rep.size = NSSize(width: CGFloat(rep.pixelsWide) * 72.0 / dpi, height: CGFloat(rep.pixelsHigh) * 72.0 / dpi)
    let data = rep.representation(using: .png, properties: [:])!
    try! data.write(to: cwd.appendingPathComponent(path))
}

func saveBMP24(_ rep: NSBitmapImageRep, to path: String) {
    let w = rep.pixelsWide
    let h = rep.pixelsHigh
    let strideLen = ((w * 3 + 3) / 4) * 4
    let imageSize = strideLen * h
    let fileSize = 54 + imageSize
    var data = Data(capacity: fileSize)

    func appendU16(_ v: UInt16) { var x = v.littleEndian; withUnsafeBytes(of: &x) { data.append(contentsOf: $0) } }
    func appendU32(_ v: UInt32) { var x = v.littleEndian; withUnsafeBytes(of: &x) { data.append(contentsOf: $0) } }
    func appendI32(_ v: Int32) { var x = v.littleEndian; withUnsafeBytes(of: &x) { data.append(contentsOf: $0) } }

    data.append(contentsOf: [0x42, 0x4D])
    appendU32(UInt32(fileSize))
    appendU16(0); appendU16(0)
    appendU32(54)

    appendU32(40)
    appendI32(Int32(w))
    appendI32(Int32(h))
    appendU16(1)
    appendU16(24)
    appendU32(0)
    appendU32(UInt32(imageSize))
    appendI32(2835); appendI32(2835)
    appendU32(0); appendU32(0)

    let raw = rep.bitmapData!
    let bpr = rep.bytesPerRow
    let pad = strideLen - w * 3
    for y in stride(from: h - 1, through: 0, by: -1) {
        for x in 0..<w {
            let p = raw.advanced(by: y * bpr + x * 4)
            let r = p[0], g = p[1], b = p[2]
            data.append(b); data.append(g); data.append(r)
        }
        if pad > 0 { data.append(contentsOf: repeatElement(UInt8(0), count: pad)) }
    }
    try! data.write(to: cwd.appendingPathComponent(path))
}

func drawCenteredText(_ text: String, in rect: CGRect, font: NSFont, color: NSColor) {
    let style = NSMutableParagraphStyle()
    style.alignment = .center
    style.lineSpacing = 3
    let attrs: [NSAttributedString.Key: Any] = [
        .font: font,
        .foregroundColor: color,
        .paragraphStyle: style
    ]
    let str = NSAttributedString(string: text, attributes: attrs)
    let size = str.boundingRect(with: rect.size, options: [.usesLineFragmentOrigin, .usesFontLeading]).size
    let drawRect = CGRect(x: rect.minX, y: rect.midY - size.height / 2, width: rect.width, height: ceil(size.height))
    str.draw(in: drawRect)
}

// 1. macOS DMG Background (1240x800 @144dpi = 620x400pt)
let dmg = makeBitmap(width: 1240, height: 800) { _, size in
    let bgGrad = NSGradient(colors: [
        NSColor(calibratedRed: 0.96, green: 0.975, blue: 1.0, alpha: 1.0),
        NSColor(calibratedRed: 0.91, green: 0.945, blue: 0.995, alpha: 1.0)
    ])!
    bgGrad.draw(in: CGRect(origin: .zero, size: size), angle: -90)

    let topBand = CGRect(x: 0, y: size.height - 156, width: size.width, height: 156)
    let headerGrad = NSGradient(colors: [
        NSColor(calibratedRed: 0.05, green: 0.11, blue: 0.28, alpha: 1.0),
        NSColor(calibratedRed: 0.09, green: 0.26, blue: 0.68, alpha: 1.0)
    ])!
    headerGrad.draw(in: topBand, angle: 12)

    if let glow = NSGradient(colors: [
        NSColor(calibratedRed: 0.0, green: 0.80, blue: 1.0, alpha: 0.24),
        NSColor(calibratedRed: 0.0, green: 0.80, blue: 1.0, alpha: 0.0)
    ]) {
        NSGraphicsContext.saveGraphicsState()
        NSBezierPath.clip(topBand)
        glow.draw(fromCenter: NSPoint(x: size.width - 140, y: size.height - 40), radius: 0,
                  toCenter: NSPoint(x: size.width - 140, y: size.height - 40), radius: 280, options: [])
        NSGraphicsContext.restoreGraphicsState()
    }

    drawCenteredText(
        "Установка YT+",
        in: CGRect(x: 0, y: size.height - 102, width: size.width, height: 52),
        font: NSFont.systemFont(ofSize: 42, weight: .bold),
        color: .white
    )
    drawCenteredText(
        "Перетащите приложение в папку «Программы»",
        in: CGRect(x: 0, y: size.height - 142, width: size.width, height: 34),
        font: NSFont.systemFont(ofSize: 22, weight: .medium),
        color: NSColor(calibratedRed: 0.80, green: 0.90, blue: 1.0, alpha: 1.0)
    )

    let leftCard = NSBezierPath(roundedRect: CGRect(x: 200, y: 170, width: 300, height: 340), xRadius: 36, yRadius: 36)
    let rightCard = NSBezierPath(roundedRect: CGRect(x: 740, y: 170, width: 300, height: 340), xRadius: 36, yRadius: 36)
    NSColor.white.withAlphaComponent(0.78).setFill()
    leftCard.fill()
    rightCard.fill()
    NSColor(calibratedRed: 0.12, green: 0.34, blue: 0.82, alpha: 0.14).setStroke()
    leftCard.lineWidth = 2.5
    rightCard.lineWidth = 2.5
    leftCard.stroke()
    rightCard.stroke()

    let arrowCircle = NSBezierPath(ovalIn: CGRect(x: 564, y: 299, width: 112, height: 112))
    let circleGrad = NSGradient(colors: [
        NSColor(calibratedRed: 0.0, green: 0.62, blue: 1.0, alpha: 1.0),
        NSColor(calibratedRed: 0.10, green: 0.28, blue: 0.85, alpha: 1.0)
    ])!
    circleGrad.draw(in: arrowCircle, angle: -90)

    let arrow = NSBezierPath()
    arrow.lineCapStyle = .round
    arrow.lineJoinStyle = .round
    arrow.lineWidth = 10
    arrow.move(to: NSPoint(x: 594, y: 355))
    arrow.line(to: NSPoint(x: 644, y: 355))
    arrow.move(to: NSPoint(x: 624, y: 377))
    arrow.line(to: NSPoint(x: 646, y: 355))
    arrow.line(to: NSPoint(x: 624, y: 333))
    NSColor.white.setStroke()
    arrow.stroke()

    drawCenteredText(
        "YouTube  ·  TikTok  ·  Pinterest",
        in: CGRect(x: 0, y: 54, width: size.width, height: 34),
        font: NSFont.systemFont(ofSize: 20, weight: .semibold),
        color: NSColor(calibratedRed: 0.25, green: 0.36, blue: 0.56, alpha: 0.85)
    )
}
savePNG(dmg, to: "Resources/dmg-background.png", dpi: 144)

// 2. Windows NSIS Wizard Sidebar (164x314, 24-bit BMP)
let wizard = makeBitmap(width: 164, height: 314) { _, size in
    let grad = NSGradient(colors: [
        NSColor(calibratedRed: 0.05, green: 0.10, blue: 0.25, alpha: 1.0),
        NSColor(calibratedRed: 0.08, green: 0.23, blue: 0.58, alpha: 1.0),
        NSColor(calibratedRed: 0.05, green: 0.42, blue: 0.82, alpha: 1.0)
    ])!
    grad.draw(in: CGRect(origin: .zero, size: size), angle: 75)

    if let glow = NSGradient(colors: [
        NSColor(calibratedRed: 0.0, green: 0.85, blue: 1.0, alpha: 0.34),
        NSColor(calibratedRed: 0.0, green: 0.85, blue: 1.0, alpha: 0.0)
    ]) {
        glow.draw(fromCenter: NSPoint(x: 82, y: 214), radius: 0,
                  toCenter: NSPoint(x: 82, y: 214), radius: 76, options: [])
    }

    NSColor.white.withAlphaComponent(0.08).setStroke()
    for r in [68.0, 96.0] {
        let ring = NSBezierPath(ovalIn: CGRect(x: 82 - r, y: 214 - r, width: r * 2, height: r * 2))
        ring.lineWidth = 1.0
        ring.stroke()
    }

    iconImage.draw(in: CGRect(x: 38, y: 170, width: 88, height: 88),
                   from: .zero, operation: .sourceOver, fraction: 1.0)

    drawCenteredText(
        "YT+",
        in: CGRect(x: 8, y: 124, width: 148, height: 32),
        font: NSFont.systemFont(ofSize: 26, weight: .bold),
        color: .white
    )

    let pill = NSBezierPath(roundedRect: CGRect(x: 56, y: 112, width: 52, height: 3), xRadius: 1.5, yRadius: 1.5)
    NSColor(calibratedRed: 0.45, green: 0.86, blue: 1.0, alpha: 0.85).setFill()
    pill.fill()

    drawCenteredText(
        "YouTube\nTikTok\nPinterest",
        in: CGRect(x: 8, y: 40, width: 148, height: 64),
        font: NSFont.systemFont(ofSize: 12.5, weight: .medium),
        color: NSColor(calibratedRed: 0.84, green: 0.92, blue: 1.0, alpha: 0.96)
    )

    drawCenteredText(
        "v\(version)",
        in: CGRect(x: 8, y: 14, width: 148, height: 18),
        font: NSFont.systemFont(ofSize: 10.5, weight: .regular),
        color: NSColor(calibratedRed: 0.72, green: 0.84, blue: 1.0, alpha: 0.75)
    )
}
saveBMP24(wizard, to: "windows/wizard.bmp")

// 3. Windows NSIS Header Banner (150x57, 24-bit BMP)
let header = makeBitmap(width: 150, height: 57) { _, size in
    NSColor.white.setFill()
    NSBezierPath.fill(CGRect(origin: .zero, size: size))

    let accent = NSBezierPath()
    accent.move(to: NSPoint(x: 62, y: 0))
    accent.line(to: NSPoint(x: 150, y: 0))
    accent.line(to: NSPoint(x: 150, y: 57))
    accent.line(to: NSPoint(x: 84, y: 57))
    accent.close()
    let grad = NSGradient(colors: [
        NSColor(calibratedRed: 0.08, green: 0.24, blue: 0.62, alpha: 1.0),
        NSColor(calibratedRed: 0.03, green: 0.48, blue: 0.88, alpha: 1.0)
    ])!
    grad.draw(in: accent, angle: 25)

    iconImage.draw(in: CGRect(x: 98, y: 8, width: 41, height: 41),
                   from: .zero, operation: .sourceOver, fraction: 1.0)
}
saveBMP24(header, to: "windows/header.bmp")

print("Generated Resources/dmg-background.png, windows/wizard.bmp, windows/header.bmp")
