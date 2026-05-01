import SwiftUI

extension View {
    @ViewBuilder
    func vaultGlass(cornerRadius: CGFloat = 18) -> some View {
        if #available(macOS 26.0, *) {
            self.glassEffect(.regular, in: .rect(cornerRadius: cornerRadius))
        } else {
            self
                .background(.regularMaterial, in: RoundedRectangle(cornerRadius: cornerRadius, style: .continuous))
                .overlay(
                    RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
                        .strokeBorder(.white.opacity(0.08), lineWidth: 1)
                )
        }
    }

    @ViewBuilder
    func vaultGlassChrome(cornerRadius: CGFloat = 14) -> some View {
        if #available(macOS 26.0, *) {
            self.glassEffect(.regular.tint(.white.opacity(0.04)), in: .rect(cornerRadius: cornerRadius))
        } else {
            self
                .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: cornerRadius, style: .continuous))
                .overlay(
                    RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
                        .strokeBorder(.white.opacity(0.06), lineWidth: 1)
                )
        }
    }
}

struct VaultBackground: View {
    var body: some View {
        ZStack {
            LinearGradient(
                colors: [
                    Color(red: 0.05, green: 0.06, blue: 0.10),
                    Color(red: 0.10, green: 0.08, blue: 0.16),
                    Color(red: 0.04, green: 0.05, blue: 0.09)
                ],
                startPoint: .topLeading,
                endPoint: .bottomTrailing
            )

            RadialGradient(
                colors: [Color.purple.opacity(0.25), .clear],
                center: .topLeading,
                startRadius: 30,
                endRadius: 520
            )
            .blendMode(.plusLighter)

            RadialGradient(
                colors: [Color.blue.opacity(0.22), .clear],
                center: .bottomTrailing,
                startRadius: 40,
                endRadius: 600
            )
            .blendMode(.plusLighter)
        }
        .ignoresSafeArea()
    }
}

struct PillTag: View {
    let text: String
    var tone: Tone = .neutral

    enum Tone {
        case neutral, accent, warning, success
    }

    private var tint: Color {
        switch tone {
        case .neutral: return .secondary
        case .accent: return .blue
        case .warning: return .orange
        case .success: return .green
        }
    }

    var body: some View {
        Text(text)
            .font(.caption.weight(.medium))
            .padding(.horizontal, 8)
            .padding(.vertical, 3)
            .foregroundStyle(tint)
            .background(tint.opacity(0.14), in: Capsule())
            .overlay(Capsule().strokeBorder(tint.opacity(0.25), lineWidth: 0.5))
    }
}
