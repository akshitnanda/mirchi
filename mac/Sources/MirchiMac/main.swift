import AppKit

private enum CharmDesign: Int, CaseIterable {
    case nimbuMirchi, nazar, hamsa, omamori, cornicello, chineseKnot

    var label: String {
        switch self {
        case .nimbuMirchi: return "Nimbu Mirchi · India"
        case .nazar: return "Nazar · Türkiye"
        case .hamsa: return "Hamsa · North Africa / West Asia"
        case .omamori: return "Omamori · Japan"
        case .cornicello: return "Cornicello · Italy"
        case .chineseKnot: return "Lucky Knot & Coin · China"
        }
    }
}

private final class CharmView: NSView {
    private let pointCount = 12
    private let segmentLength: CGFloat = 14
    private let anchor = CGPoint(x: 270, y: 9)
    private var chain: [CGPoint] = []
    private var previous: [CGPoint] = []
    private var timer: Timer?
    private var lastFrame = ProcessInfo.processInfo.systemUptime
    private var clock: CGFloat = 0
    private var elasticScale: CGFloat = 1
    private var elasticVelocity: CGFloat = 0
    private var design: CharmDesign = .nimbuMirchi
    private var draggingCharm = false
    private var draggingAnchor = false
    private var draggedNode = 11
    private var dragTarget = CGPoint.zero
    private var anchorDragOffset = CGPoint.zero

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { false }

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        resetChain()
        timer = Timer.scheduledTimer(timeInterval: 1.0 / 60.0,
                                     target: self,
                                     selector: #selector(tick),
                                     userInfo: nil,
                                     repeats: true)
        timer?.tolerance = 0.001
    }

    required init?(coder: NSCoder) { nil }
    deinit { timer?.invalidate() }

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func hitTest(_ point: NSPoint) -> NSView? {
        // Return nil over transparent canvas pixels so the app underneath still
        // receives clicks; only the visible charm geometry is interactive.
        isInteractive(point) ? self : nil
    }

    @objc private func tick() {
        let now = ProcessInfo.processInfo.systemUptime
        let dt = min(0.04, CGFloat(now - lastFrame))
        lastFrame = now
        let count = max(1, Int(ceil(dt / 0.008)))
        let step = dt / CGFloat(count)
        for _ in 0..<count { stepPhysics(step) }
        needsDisplay = true
    }

    private func stepPhysics(_ dt: CGFloat) {
        clock += dt
        var desiredScale: CGFloat = 1
        if draggingCharm && draggedNode >= 4 {
            let dx = dragTarget.x - anchor.x
            let dy = dragTarget.y - anchor.y
            let distance = hypot(dx, dy)
            desiredScale = min(1.48, max(1, distance / (CGFloat(draggedNode) * segmentLength)))
        }

        let spring: CGFloat = draggingCharm ? 78 : 145
        let damping: CGFloat = draggingCharm ? 13 : 8.2
        elasticVelocity += (spring * (desiredScale - elasticScale) - damping * elasticVelocity) * dt
        elasticScale = min(1.55, max(0.84, elasticScale + elasticVelocity * dt))

        for i in 1..<pointCount {
            let current = chain[i]
            let depth = CGFloat(i) / CGFloat(pointCount - 1)
            let vx = (current.x - previous[i].x) * 0.994
            let vy = (current.y - previous[i].y) * 0.994
            previous[i] = current
            let breeze = sin(clock * 1.7 + depth * 2.3) * 8.5 * depth
            chain[i].x += vx + breeze * dt * dt
            chain[i].y += vy + 1050 * dt * dt
        }

        if draggingCharm {
            let pull = 1 - exp(-18 * dt)
            chain[draggedNode].x += (dragTarget.x - chain[draggedNode].x) * pull
            chain[draggedNode].y += (dragTarget.y - chain[draggedNode].y) * pull
        }

        let targetLength = segmentLength * elasticScale
        for _ in 0..<8 {
            chain[0] = anchor
            for i in 0..<(pointCount - 1) {
                var a = chain[i]
                var b = chain[i + 1]
                let dx = b.x - a.x
                let dy = b.y - a.y
                let distance = hypot(dx, dy)
                guard distance > 0.001 else { continue }
                let error = (distance - targetLength) / distance
                if i == 0 {
                    b.x -= dx * error
                    b.y -= dy * error
                } else {
                    var shareA: CGFloat = draggingCharm && i == draggedNode ? 0.08 : 0.5
                    var shareB: CGFloat = draggingCharm && i + 1 == draggedNode ? 0.08 : 0.5
                    let total = shareA + shareB
                    shareA /= total
                    shareB /= total
                    a.x += dx * error * shareA
                    a.y += dy * error * shareA
                    b.x -= dx * error * shareB
                    b.y -= dy * error * shareB
                    chain[i] = a
                }
                chain[i + 1] = b
            }
        }
        chain[0] = anchor
    }

    private func resetChain() {
        chain = (0..<pointCount).map { CGPoint(x: anchor.x, y: anchor.y + CGFloat($0) * segmentLength) }
        previous = chain
        elasticScale = 1
        elasticVelocity = 0
        draggingCharm = false
        draggingAnchor = false
    }

    private func kick() {
        var amount = CGFloat.random(in: -5...5)
        if abs(amount) < 2.4 { amount = amount < 0 ? -4.2 : 4.2 }
        for i in 1..<pointCount {
            let depth = CGFloat(i) / CGFloat(pointCount - 1)
            previous[i].x -= amount * depth * depth
            previous[i].y += abs(amount) * 0.08 * depth
        }
    }

    override func mouseDown(with event: NSEvent) {
        if event.clickCount == 2 { kick(); return }
        let p = convert(event.locationInWindow, from: nil)
        if distance(p, anchor) <= 16 {
            draggingAnchor = true
            let mouse = NSEvent.mouseLocation
            guard let frame = window?.frame else { return }
            let anchorScreen = CGPoint(x: frame.minX + anchor.x, y: frame.maxY - anchor.y)
            anchorDragOffset = CGPoint(x: mouse.x - anchorScreen.x, y: mouse.y - anchorScreen.y)
        } else {
            draggingCharm = true
            draggedNode = nearestNode(to: p)
            dragTarget = clampedTarget(p, radius: CGFloat(draggedNode) * segmentLength)
        }
    }

    override func mouseDragged(with event: NSEvent) {
        if draggingAnchor, let panel = window {
            let mouse = NSEvent.mouseLocation
            let old = panel.frame.origin
            let next = CGPoint(x: mouse.x - anchor.x - anchorDragOffset.x,
                               y: mouse.y - (bounds.height - anchor.y) - anchorDragOffset.y)
            panel.setFrameOrigin(next)
            let shiftX = (next.x - old.x) * 0.72
            let shiftY = (next.y - old.y) * 0.72
            for i in 1..<pointCount {
                chain[i].x -= shiftX
                chain[i].y += shiftY
                previous[i].x -= shiftX
                previous[i].y += shiftY
            }
        } else if draggingCharm {
            let p = convert(event.locationInWindow, from: nil)
            dragTarget = clampedTarget(p, radius: CGFloat(draggedNode) * segmentLength)
        }
    }

    override func mouseUp(with event: NSEvent) {
        draggingCharm = false
        draggingAnchor = false
    }

    override func scrollWheel(with event: NSEvent) {
        let delta = event.scrollingDeltaY < 0 ? 1 : -1
        let count = CharmDesign.allCases.count
        design = CharmDesign(rawValue: (design.rawValue + delta + count) % count) ?? .nimbuMirchi
        kick()
    }

    override func rightMouseDown(with event: NSEvent) {
        let menu = NSMenu(title: "Mirchi")
        let wiggle = NSMenuItem(title: "Give it a wiggle", action: #selector(wiggleAction), keyEquivalent: "")
        wiggle.target = self
        menu.addItem(wiggle)
        let reset = NSMenuItem(title: "Reset", action: #selector(resetAction), keyEquivalent: "")
        reset.target = self
        menu.addItem(reset)
        let designItem = NSMenuItem(title: "Design", action: nil, keyEquivalent: "")
        let submenu = NSMenu(title: "Design")
        for choice in CharmDesign.allCases {
            let item = NSMenuItem(title: choice.label, action: #selector(selectDesign(_:)), keyEquivalent: "")
            item.target = self
            item.tag = choice.rawValue
            item.state = choice == design ? .on : .off
            submenu.addItem(item)
        }
        designItem.submenu = submenu
        menu.addItem(designItem)
        menu.addItem(.separator())
        let quit = NSMenuItem(title: "Quit", action: #selector(quitAction), keyEquivalent: "")
        quit.target = self
        menu.addItem(quit)
        NSMenu.popUpContextMenu(menu, with: event, for: self)
    }

    @objc private func wiggleAction() { kick() }
    @objc private func resetAction() { resetChain() }
    @objc private func quitAction() { NSApplication.shared.terminate(nil) }
    @objc private func selectDesign(_ sender: NSMenuItem) {
        design = CharmDesign(rawValue: sender.tag) ?? .nimbuMirchi
        kick()
    }

    private func nearestNode(to p: CGPoint) -> Int {
        (1..<pointCount).min(by: { distance(p, chain[$0]) < distance(p, chain[$1]) }) ?? pointCount - 1
    }

    private func clampedTarget(_ p: CGPoint, radius: CGFloat) -> CGPoint {
        var dx = p.x - anchor.x
        var dy = p.y - anchor.y
        let d = hypot(dx, dy)
        let maximum = max(segmentLength, radius * 1.52)
        if d > maximum {
            dx *= maximum / d
            dy *= maximum / d
        }
        return CGPoint(x: anchor.x + dx, y: anchor.y + dy)
    }

    private func isInteractive(_ p: CGPoint) -> Bool {
        if distance(p, anchor) <= 15 { return true }
        for i in 0..<(pointCount - 1) {
            if segmentDistance(p, chain[i], chain[i + 1]) <= (i < 2 ? 7 : 30) { return true }
        }
        return false
    }

    private func distance(_ a: CGPoint, _ b: CGPoint) -> CGFloat { hypot(a.x - b.x, a.y - b.y) }
    private func segmentDistance(_ p: CGPoint, _ a: CGPoint, _ b: CGPoint) -> CGFloat {
        let dx = b.x - a.x, dy = b.y - a.y
        let l2 = dx * dx + dy * dy
        if l2 < 0.001 { return distance(p, a) }
        let t = max(0, min(1, ((p.x - a.x) * dx + (p.y - a.y) * dy) / l2))
        return distance(p, CGPoint(x: a.x + t * dx, y: a.y + t * dy))
    }

    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)
        guard let context = NSGraphicsContext.current?.cgContext else { return }
        context.setAllowsAntialiasing(true)
        context.setShouldAntialias(true)
        drawThread()
        switch design {
        case .nimbuMirchi: drawNimbuMirchi()
        case .nazar: drawNazar()
        case .hamsa: drawHamsa()
        case .omamori: drawOmamori()
        case .cornicello: drawCornicello()
        case .chineseKnot: drawChineseKnotAndCoin()
        }
        drawAnchor()
    }

    private var threadColor: NSColor {
        switch design {
        case .nazar: return NSColor(calibratedRed: 0.12, green: 0.37, blue: 0.72, alpha: 0.96)
        case .hamsa: return NSColor(calibratedRed: 0.14, green: 0.50, blue: 0.53, alpha: 0.96)
        case .omamori: return NSColor(calibratedRed: 0.88, green: 0.28, blue: 0.36, alpha: 0.96)
        case .cornicello: return NSColor(calibratedRed: 0.69, green: 0.49, blue: 0.17, alpha: 0.96)
        default: return NSColor(calibratedRed: 0.80, green: 0.14, blue: 0.13, alpha: 0.96)
        }
    }

    private func drawThread() {
        let shadow = NSBezierPath()
        shadow.move(to: CGPoint(x: chain[0].x + 1.2, y: chain[0].y + 1.2))
        for p in chain.dropFirst() { shadow.line(to: CGPoint(x: p.x + 1.2, y: p.y + 1.2)) }
        shadow.lineWidth = 4.2
        shadow.lineCapStyle = .round
        shadow.lineJoinStyle = .round
        NSColor(calibratedWhite: 0, alpha: 0.17).setStroke()
        shadow.stroke()

        let cord = NSBezierPath()
        cord.move(to: chain[0])
        for p in chain.dropFirst() { cord.line(to: p) }
        cord.lineWidth = 2.05
        cord.lineCapStyle = .round
        cord.lineJoinStyle = .round
        threadColor.setStroke()
        cord.stroke()
    }

    private func chainFrame(_ distance: CGFloat) -> (CGPoint, CGFloat) {
        let maxDistance = CGFloat(pointCount - 1) * segmentLength
        let d = max(0, min(maxDistance, distance))
        let segment = min(pointCount - 2, Int(d / segmentLength))
        let t = (d - CGFloat(segment) * segmentLength) / segmentLength
        let a = chain[segment], b = chain[segment + 1]
        let p = CGPoint(x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t)
        let before = max(0, segment - 1), after = min(pointCount - 1, segment + 2)
        let dx = chain[after].x - chain[before].x
        let dy = chain[after].y - chain[before].y
        return (p, -atan2(dx, dy) * 180 / .pi)
    }

    private func atChain(_ distance: CGFloat, _ body: () -> Void) {
        let (p, rotation) = chainFrame(distance)
        NSGraphicsContext.saveGraphicsState()
        let transform = NSAffineTransform()
        transform.translateX(by: p.x, yBy: p.y)
        transform.rotate(byDegrees: rotation)
        transform.concat()
        body()
        NSGraphicsContext.restoreGraphicsState()
    }

    private func gradient(_ path: NSBezierPath, _ colors: [NSColor], angle: CGFloat) {
        NSGradient(colors: colors)?.draw(in: path, angle: angle)
    }

    private func drawAnchor() {
        NSColor(calibratedWhite: 0, alpha: 0.20).setFill()
        NSBezierPath(ovalIn: NSRect(x: anchor.x - 4, y: anchor.y - 3, width: 10, height: 10)).fill()
        threadColor.setFill()
        NSBezierPath(ovalIn: NSRect(x: anchor.x - 4, y: anchor.y - 4, width: 8, height: 8)).fill()
        let loop = NSBezierPath(ovalIn: NSRect(x: anchor.x - 7, y: anchor.y - 1, width: 14, height: 9))
        loop.lineWidth = 2
        threadColor.setStroke()
        loop.stroke()
    }

    private func drawBead(_ distance: CGFloat, radius: CGFloat, light: NSColor, dark: NSColor) {
        atChain(distance) {
            NSColor(calibratedWhite: 0, alpha: 0.18).setFill()
            NSBezierPath(ovalIn: NSRect(x: -radius + 1, y: -radius + 1.3, width: radius * 2, height: radius * 2)).fill()
            let bead = NSBezierPath(ovalIn: NSRect(x: -radius, y: -radius, width: radius * 2, height: radius * 2))
            gradient(bead, [light, dark], angle: 65)
            NSColor(calibratedWhite: 1, alpha: 0.45).setFill()
            NSBezierPath(ovalIn: NSRect(x: -radius * 0.48, y: -radius * 0.58, width: radius * 0.55, height: radius * 0.42)).fill()
        }
    }

    private func drawNimbuMirchi() {
        drawBead(37, radius: 4.5, light: .systemRed, dark: .darkGray)
        drawChilli(49, scale: 0.97, flip: false, red: false)
        drawChilli(64, scale: 0.92, flip: true, red: true)
        atChain(92) { drawLemon(scale: 1.72) }
        drawChilli(121, scale: 0.96, flip: true, red: false)
        drawChilli(136, scale: 0.91, flip: false, red: true)
        drawBead(151, radius: 4, light: NSColor(calibratedWhite: 0.4, alpha: 1), dark: NSColor(calibratedWhite: 0.1, alpha: 1))
    }

    private func drawChilli(_ distance: CGFloat, scale: CGFloat, flip: Bool, red: Bool) {
        atChain(distance) {
            NSGraphicsContext.saveGraphicsState()
            let t = NSAffineTransform()
            t.scaleX(by: flip ? -scale : scale, yBy: scale)
            t.concat()
            let path = NSBezierPath()
            path.move(to: CGPoint(x: -20, y: -4))
            path.curve(to: CGPoint(x: 16, y: -5), controlPoint1: CGPoint(x: -10, y: -7), controlPoint2: CGPoint(x: 7, y: -2))
            path.curve(to: CGPoint(x: 22, y: -6), controlPoint1: CGPoint(x: 21, y: -7), controlPoint2: CGPoint(x: 23, y: -11))
            path.curve(to: CGPoint(x: 12, y: 3), controlPoint1: CGPoint(x: 21, y: -1), controlPoint2: CGPoint(x: 17, y: 2))
            path.curve(to: CGPoint(x: -20, y: 3), controlPoint1: CGPoint(x: 1, y: 7), controlPoint2: CGPoint(x: -11, y: 6))
            path.curve(to: CGPoint(x: -20, y: -4), controlPoint1: CGPoint(x: -22, y: 1), controlPoint2: CGPoint(x: -22, y: -2))
            path.close()
            let light = red ? NSColor(calibratedRed: 0.94, green: 0.23, blue: 0.18, alpha: 1) : NSColor(calibratedRed: 0.28, green: 0.66, blue: 0.29, alpha: 1)
            let dark = red ? NSColor(calibratedRed: 0.57, green: 0.08, blue: 0.10, alpha: 1) : NSColor(calibratedRed: 0.07, green: 0.38, blue: 0.18, alpha: 1)
            gradient(path, [light, dark], angle: 80)
            dark.setStroke(); path.lineWidth = 1.05; path.stroke()
            let stem = NSBezierPath()
            stem.move(to: CGPoint(x: -20, y: -1))
            stem.curve(to: CGPoint(x: -23, y: -8), controlPoint1: CGPoint(x: -24, y: -3), controlPoint2: CGPoint(x: -25, y: -6))
            stem.lineWidth = 1.8; stem.lineCapStyle = .round
            NSColor(calibratedRed: 0.26, green: 0.39, blue: 0.14, alpha: 1).setStroke(); stem.stroke()
            NSGraphicsContext.restoreGraphicsState()
        }
    }

    private func drawLemon(scale: CGFloat) {
        NSGraphicsContext.saveGraphicsState()
        let transform = NSAffineTransform(); transform.scale(by: scale); transform.concat()
        let leaf = NSBezierPath()
        leaf.move(to: CGPoint(x: 2, y: -11))
        leaf.curve(to: CGPoint(x: 18, y: -14), controlPoint1: CGPoint(x: 7, y: -18), controlPoint2: CGPoint(x: 15, y: -19))
        leaf.curve(to: CGPoint(x: 2, y: -11), controlPoint1: CGPoint(x: 14, y: -9), controlPoint2: CGPoint(x: 7, y: -8))
        leaf.close()
        gradient(leaf, [NSColor.systemGreen, NSColor(calibratedRed: 0.08, green: 0.40, blue: 0.18, alpha: 1)], angle: 30)

        let fruit = NSBezierPath()
        fruit.move(to: CGPoint(x: 0, y: -13))
        fruit.curve(to: CGPoint(x: 14, y: -2), controlPoint1: CGPoint(x: 8, y: -14), controlPoint2: CGPoint(x: 13, y: -9))
        fruit.curve(to: CGPoint(x: 2, y: 18), controlPoint1: CGPoint(x: 16, y: 6), controlPoint2: CGPoint(x: 10, y: 13))
        fruit.curve(to: CGPoint(x: -2, y: 18), controlPoint1: CGPoint(x: 1, y: 20), controlPoint2: CGPoint(x: -1, y: 20))
        fruit.curve(to: CGPoint(x: -14, y: 0), controlPoint1: CGPoint(x: -10, y: 14), controlPoint2: CGPoint(x: -15, y: 7))
        fruit.curve(to: CGPoint(x: 0, y: -13), controlPoint1: CGPoint(x: -15, y: -7), controlPoint2: CGPoint(x: -9, y: -13))
        fruit.close()
        gradient(fruit, [NSColor(calibratedRed: 1, green: 0.94, blue: 0.31, alpha: 1), NSColor(calibratedRed: 0.88, green: 0.62, blue: 0.07, alpha: 1)], angle: 72)
        NSColor(calibratedRed: 0.62, green: 0.42, blue: 0.05, alpha: 0.9).setStroke(); fruit.lineWidth = 1.05; fruit.stroke()
        let node = min(pointCount - 1, max(1, Int(round(92 / segmentLength))))
        let vx = chain[node].x - previous[node].x
        let vy = chain[node].y - previous[node].y
        var gaze = max(-1.5, min(1.5, vx * 0.55))
        if draggingCharm { gaze += max(-0.8, min(0.8, (dragTarget.x - chain[node].x) * 0.018)) }
        let energy = min(1, hypot(vx, vy) / 5)
        let blinking = !draggingCharm && clock.truncatingRemainder(dividingBy: 4.6) > 4.43
        drawCuteFace(gaze: gaze, energy: energy, blinking: blinking, surprised: elasticScale > 1.13)
        NSGraphicsContext.restoreGraphicsState()
    }

    private func drawCuteFace(gaze: CGFloat, energy: CGFloat, blinking: Bool, surprised: Bool) {
        let ink = NSColor(calibratedRed: 0.29, green: 0.19, blue: 0.12, alpha: 0.91)
        ink.setStroke()
        if blinking {
            for side: CGFloat in [-1, 1] {
                let eye = NSBezierPath()
                eye.move(to: CGPoint(x: side * 2.5, y: 0.5))
                eye.curve(to: CGPoint(x: side * 7.1, y: 0.5),
                          controlPoint1: CGPoint(x: side * 3.8, y: 1.8),
                          controlPoint2: CGPoint(x: side * 5.8, y: 1.8))
                eye.lineWidth = 0.78; eye.lineCapStyle = .round; eye.stroke()
            }
        } else {
            NSColor(calibratedRed: 1, green: 0.98, blue: 0.86, alpha: 0.86).setFill()
            for centerX: CGFloat in [-4.9, 4.9] {
                let eye = almond(centerX: centerX, centerY: 0.6, height: surprised ? 2.2 : 1.7)
                eye.fill(); ink.setStroke(); eye.lineWidth = 0.78; eye.stroke()
            }
            ink.setFill()
            let pupilHeight: CGFloat = surprised ? 2.1 : 1.8
            NSBezierPath(ovalIn: NSRect(x: -5.65 + gaze, y: surprised ? -0.05 : 0.15, width: 1.5, height: pupilHeight)).fill()
            NSBezierPath(ovalIn: NSRect(x: 4.15 + gaze, y: surprised ? -0.05 : 0.15, width: 1.5, height: pupilHeight)).fill()
        }
        NSColor(calibratedRed: 0.71, green: 0.13, blue: 0.17, alpha: 0.88).setFill()
        NSBezierPath(ovalIn: NSRect(x: -0.85, y: -5, width: 1.7, height: 1.7)).fill()
        NSColor(calibratedRed: 0.92, green: 0.36, blue: 0.40, alpha: 0.24 + energy * 0.18).setFill()
        NSBezierPath(ovalIn: NSRect(x: -8.8, y: 4, width: 4.8, height: 2.8)).fill()
        NSBezierPath(ovalIn: NSRect(x: 4, y: 4, width: 4.8, height: 2.8)).fill()
        NSColor(calibratedRed: 0.64, green: 0.21, blue: 0.22, alpha: 0.86).setStroke()
        if surprised {
            let mouth = NSBezierPath(ovalIn: NSRect(x: -1.45, y: 6.1, width: 2.9, height: 3.8))
            mouth.lineWidth = 0.8; mouth.stroke()
        } else {
            let lift = 8.0 + energy
            let smile = NSBezierPath(); smile.move(to: CGPoint(x: -2.5, y: 6.5))
            smile.curve(to: CGPoint(x: 2.6, y: 6.4), controlPoint1: CGPoint(x: -1.1, y: lift), controlPoint2: CGPoint(x: 1.2, y: lift))
            smile.lineWidth = 0.8; smile.lineCapStyle = .round; smile.stroke()
        }
    }

    private func almond(centerX: CGFloat, centerY: CGFloat, height: CGFloat) -> NSBezierPath {
        let eye = NSBezierPath()
        let halfWidth: CGFloat = 2.65
        eye.move(to: CGPoint(x: centerX - halfWidth, y: centerY))
        eye.curve(to: CGPoint(x: centerX + halfWidth, y: centerY),
                  controlPoint1: CGPoint(x: centerX - 1.3, y: centerY - height),
                  controlPoint2: CGPoint(x: centerX + 1.3, y: centerY - height))
        eye.curve(to: CGPoint(x: centerX - halfWidth, y: centerY),
                  controlPoint1: CGPoint(x: centerX + 1.3, y: centerY + height * 0.72),
                  controlPoint2: CGPoint(x: centerX - 1.3, y: centerY + height * 0.72))
        eye.close()
        return eye
    }

    private func drawNazar() {
        drawBead(40, radius: 4.5, light: .systemTeal, dark: .systemBlue)
        drawBead(54, radius: 5.5, light: .systemBlue, dark: .blue)
        atChain(89) {
            let rings: [(CGFloat, NSColor)] = [(46, .systemBlue), (34, .systemTeal), (25, .white), (17, .systemBlue), (9.5, .black)]
            for (size, color) in rings { color.setFill(); NSBezierPath(ovalIn: NSRect(x: -size/2, y: -size/2, width: size, height: size)).fill() }
            NSColor.white.withAlphaComponent(0.72).setFill(); NSBezierPath(ovalIn: NSRect(x: -3, y: -4, width: 3, height: 3)).fill()
        }
        drawBead(124, radius: 5, light: .systemTeal, dark: .systemBlue)
        drawBead(140, radius: 4, light: .cyan, dark: .blue)
    }

    private func drawHamsa() {
        drawBead(42, radius: 4.5, light: .systemTeal, dark: .systemBlue)
        atChain(96) {
            let gold = NSColor(calibratedRed: 0.91, green: 0.74, blue: 0.29, alpha: 1)
            let fingers: [(CGFloat, CGFloat)] = [(-10,-17),(-3.5,-26),(3.5,-28),(10,-19)]
            let path = NSBezierPath(); path.lineWidth = 7; path.lineCapStyle = .round; gold.setStroke()
            for (x, top) in fingers { path.move(to: CGPoint(x: x, y: 2)); path.line(to: CGPoint(x: x, y: top)) }
            path.move(to: CGPoint(x: -13, y: 4)); path.line(to: CGPoint(x: -23, y: -7)); path.stroke()
            gold.setFill(); NSBezierPath(ovalIn: NSRect(x: -16, y: -9, width: 32, height: 36)).fill(); NSBezierPath(rect: NSRect(x: -9, y: 18, width: 18, height: 14)).fill()
            NSColor.white.setFill(); NSBezierPath(ovalIn: NSRect(x: -10, y: -2, width: 20, height: 10)).fill()
            NSColor.systemTeal.setFill(); NSBezierPath(ovalIn: NSRect(x: -4.5, y: -2, width: 9, height: 10)).fill()
            NSColor.black.setFill(); NSBezierPath(ovalIn: NSRect(x: -2, y: 0.5, width: 4, height: 5)).fill()
        }
        drawBead(137, radius: 4.8, light: .systemTeal, dark: .systemBlue)
    }

    private func drawOmamori() {
        drawBead(40, radius: 4.2, light: .systemPink, dark: .systemRed)
        atChain(98) {
            let pouch = NSBezierPath(roundedRect: NSRect(x: -21, y: -29, width: 42, height: 58), xRadius: 8, yRadius: 8)
            gradient(pouch, [NSColor(calibratedRed: 0.95, green: 0.49, blue: 0.59, alpha: 1), NSColor(calibratedRed: 0.69, green: 0.16, blue: 0.31, alpha: 1)], angle: 72)
            NSColor(calibratedRed: 0.94, green: 0.76, blue: 0.36, alpha: 1).setStroke(); pouch.lineWidth = 1.5; pouch.stroke()
            let band = NSBezierPath(); band.move(to: CGPoint(x: -18, y: -18)); band.line(to: CGPoint(x: 18, y: -18)); band.lineWidth = 1.5; band.stroke()
            NSColor(calibratedRed: 1, green: 0.86, blue: 0.76, alpha: 0.72).setFill()
            for p in [CGPoint(x:-4,y:-3),CGPoint(x:4,y:-3),CGPoint(x:0,y:-7),CGPoint(x:0,y:1)] { NSBezierPath(ovalIn: NSRect(x:p.x-3,y:p.y-3,width:6,height:6)).fill() }
        }
        drawBead(140, radius: 4.5, light: .systemPink, dark: .systemRed)
    }

    private func drawCornicello() {
        drawBead(42, radius: 4.5, light: .systemYellow, dark: .brown)
        drawBead(57, radius: 5, light: .systemRed, dark: .darkGray)
        atChain(103) {
            let horn = NSBezierPath(); horn.move(to: CGPoint(x:-8,y:-34))
            horn.curve(to: CGPoint(x:12,y:-16), controlPoint1: CGPoint(x:5,y:-36), controlPoint2: CGPoint(x:13,y:-27))
            horn.curve(to: CGPoint(x:-13,y:29), controlPoint1: CGPoint(x:11,y:1), controlPoint2: CGPoint(x:2,y:17))
            horn.curve(to: CGPoint(x:-9,y:32), controlPoint1: CGPoint(x:-18,y:33), controlPoint2: CGPoint(x:-16,y:36))
            horn.curve(to: CGPoint(x:21,y:-14), controlPoint1: CGPoint(x:10,y:21), controlPoint2: CGPoint(x:21,y:3))
            horn.curve(to: CGPoint(x:-8,y:-34), controlPoint1: CGPoint(x:21,y:-30), controlPoint2: CGPoint(x:8,y:-40)); horn.close()
            gradient(horn, [.systemRed, NSColor(calibratedRed:0.55,green:0.05,blue:0.08,alpha:1)], angle: 32)
            NSColor(calibratedRed:0.42,green:0.04,blue:0.06,alpha:0.9).setStroke(); horn.lineWidth = 1.2; horn.stroke()
            let cap = NSBezierPath(ovalIn:NSRect(x:-10,y:-39,width:20,height:9)); gradient(cap,[.systemYellow,.brown],angle:80)
        }
        drawBead(145, radius: 4.4, light: .systemYellow, dark: .brown)
    }

    private func drawChineseKnotAndCoin() {
        drawBead(40, radius: 4.5, light: .systemRed, dark: .darkGray)
        atChain(72) {
            let cord = NSBezierPath(); cord.lineWidth = 4.5; cord.lineCapStyle = .round; cord.lineJoinStyle = .round
            cord.move(to: CGPoint(x:0,y:-26)); cord.curve(to: CGPoint(x:-10,y:-7),controlPoint1:CGPoint(x:-25,y:-24),controlPoint2:CGPoint(x:-25,y:-5))
            cord.move(to: CGPoint(x:0,y:-26)); cord.curve(to: CGPoint(x:10,y:-7),controlPoint1:CGPoint(x:25,y:-24),controlPoint2:CGPoint(x:25,y:-5))
            for p in [CGPoint(x:0,y:-19),CGPoint(x:16,y:0),CGPoint(x:0,y:19),CGPoint(x:-16,y:0),CGPoint(x:0,y:-19)] { if p == CGPoint(x:0,y:-19) { cord.move(to:p) } else { cord.line(to:p) } }
            NSColor.systemRed.setStroke(); cord.stroke()
        }
        atChain(114) {
            let coin = NSBezierPath(ovalIn:NSRect(x:-18,y:-18,width:36,height:36)); gradient(coin,[.systemYellow,NSColor(calibratedRed:0.62,green:0.35,blue:0.07,alpha:1)],angle:65)
            NSColor.brown.setFill(); NSBezierPath(rect:NSRect(x:-5,y:-5,width:10,height:10)).fill()
        }
        atChain(143) {
            NSColor.systemRed.setFill(); NSBezierPath(rect:NSRect(x:-7,y:-4,width:14,height:8)).fill()
            NSColor.systemRed.setStroke()
            for x in stride(from: CGFloat(-6), through: 6, by: 2) { let p=NSBezierPath(); p.move(to:CGPoint(x:x,y:3)); p.curve(to:CGPoint(x:x*0.85,y:21),controlPoint1:CGPoint(x:x-1,y:9),controlPoint2:CGPoint(x:x+1,y:15)); p.lineWidth=1.5; p.stroke() }
        }
    }
}

private final class AppDelegate: NSObject, NSApplicationDelegate {
    private var panel: NSPanel?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApplication.shared.setActivationPolicy(.accessory)
        let size = NSSize(width: 540, height: 270)
        let screen = NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1440, height: 900)
        let origin = NSPoint(x: screen.maxX - size.width / 2 - 42, y: screen.maxY - size.height)
        let panel = NSPanel(contentRect: NSRect(origin: origin, size: size),
                            styleMask: [.borderless, .nonactivatingPanel],
                            backing: .buffered,
                            defer: false)
        panel.backgroundColor = .clear
        panel.isOpaque = false
        panel.hasShadow = false
        // A non-activating floating panel keeps the charm visible across Spaces
        // without stealing keyboard focus from the user's current app.
        panel.level = .floating
        panel.isFloatingPanel = true
        panel.hidesOnDeactivate = false
        panel.becomesKeyOnlyIfNeeded = true
        panel.isReleasedWhenClosed = false
        panel.restorable = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.contentView = CharmView(frame: NSRect(origin: .zero, size: size))
        panel.orderFrontRegardless()
        self.panel = panel
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
}

let application = NSApplication.shared
let delegate = AppDelegate()
application.delegate = delegate
application.run()
