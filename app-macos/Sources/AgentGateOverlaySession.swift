import Foundation

/// Auth overlay for the device gates the PAM path never sees.
///
/// Before build 487 only two flows could raise `AuthRequestOverlay`: a PAM
/// `AUTH` whose caller chain looked like an AI agent, and `AGENT_APPROVE`
/// (`imk run --agent`). Every other fingerprint gate — ssh-agent `KEY_SIGN`,
/// `imk get imk://api/...`, `imk get imk://otp/...` — made the device blink
/// with no on-screen prompt at all. From the user's chair that looked like
/// "the agent did something and the LED is asking me to approve it, but no
/// window came up". It bit whenever an agent ran `git push` / `ssh` /
/// `imk get` unwrapped, and also inside a wrap when the gate-hitting step
/// landed more than 10 s after the approval touch (firmware AUTH cooldown
/// is a rolling 10 s).
///
/// This session mirrors the PAM rule: classify the socket peer's parent
/// chain, show the overlay only for agents (manual `ssh` / `imk` from a
/// terminal stays quiet), and show it only once the device actually asks
/// for a touch (`WAIT_FP`) — a gate satisfied by cooldown never prompts.
///
/// Thread-safety: created and driven from the socket handler thread; all
/// overlay calls hop to the main queue exactly like `PAMSocketServer`.
final class AgentGateOverlaySession {
    /// What the overlay prints as the command line.
    private let command: String
    private let service: String
    private let kind: AuthRequestKind
    private let onReject: () -> Void
    private let lock = NSLock()
    private var shown = false
    private var finished = false

    /// Returns nil when the caller is a human at a terminal — nothing to show.
    /// `fallbackCommand` is used when the chain carries no `imk run --agent`
    /// marker (heuristic agent-binary match only), so the user still sees
    /// what the touch is for.
    init?(
        socketFD: Int32,
        service: String,
        fallbackCommand: String,
        kind: AuthRequestKind,
        onReject: @escaping () -> Void
    ) {
        let caller = AuthCallerClassifier.classify(socketFD: socketFD)
        switch caller {
        case .agent(let cmd):
            self.command = cmd ?? fallbackCommand
        case .manual:
            Task { @MainActor in
                LogManager.shared.log("gate overlay: \(service) suppressed (caller=manual)")
            }
            return nil
        }
        self.service = service
        self.kind = kind
        self.onReject = onReject
    }

    /// Device replied WAIT_FP — a real touch is needed. Idempotent.
    func gateRequired() {
        lock.lock()
        let alreadyShown = shown
        shown = true
        lock.unlock()
        guard !alreadyShown else { return }

        let command = self.command
        let service = self.service
        let kind = self.kind
        let onReject = self.onReject
        Task { @MainActor in
            LogManager.shared.log("gate overlay: \(service) shown (caller=agent)")
            AuthRequestOverlay.shared.show(
                user: NSUserName(),
                service: service,
                command: command,
                kind: kind,
                timeout: 30.0,
                onReject: onReject
            )
        }
    }

    func attemptFailed(remaining: Int) {
        guard isShown else { return }
        Task { @MainActor in AuthRequestOverlay.shared.reportRetry(remaining: remaining) }
    }

    /// Terminal state. No-op unless the overlay was actually shown, so a
    /// cooldown-covered gate never flashes a panel. Idempotent.
    func finish(_ status: AuthRequestState.Status) {
        lock.lock()
        let shouldDismiss = shown && !finished
        finished = true
        lock.unlock()
        guard shouldDismiss else { return }
        Task { @MainActor in AuthRequestOverlay.shared.dismiss(status: status) }
    }

    private var isShown: Bool {
        lock.lock(); defer { lock.unlock() }
        return shown
    }
}
