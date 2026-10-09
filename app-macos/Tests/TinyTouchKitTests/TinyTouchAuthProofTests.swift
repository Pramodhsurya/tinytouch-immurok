import XCTest
@testable import TinyTouchKit

final class TinyTouchAuthProofTests: XCTestCase {
    func testDeviceErrorsDistinguishStartHandshakeAndMatchWithoutReflectingWireText() {
        if case .deviceUnavailable = TinyTouchAuthError.deviceError("ERR AUTH2 unavailable") {} else { XCTFail("start stage") }
        if case .handshakeRejected = TinyTouchAuthError.deviceError("ERR AUTH2 rejected") {} else { XCTFail("handshake stage") }
        if case .matchUnverified = TinyTouchAuthError.deviceError("ERR AUTH2 no_match_or_expired") {} else { XCTFail("match stage") }
        for line in ["ERR AUTH2 unavailable secret=private", "ERR AUTH2 custom private", "ERR OTHER private"] {
            let error = TinyTouchAuthError.deviceError(line)
            if case .rejected = error {} else { XCTFail("unknown error") }
            XCTAssertFalse(error.localizedDescription.contains("private"))
            XCTAssertFalse(error.localizedDescription.contains("secret="))
        }
    }
    private let challengeTag = "85e84657b21e5feb755486598187d8d83972bacacba422a4518f2c6c70962cb8"
    private let matchTag = "0d2c9f05bc9dfd1d62f8e56837733dc896c850dff35711f8cfe3f68d6ecd5508"
    private var client: String { String(repeating: "a1", count: 32) }
    private var device: String { String(repeating: "b2", count: 32) }
    private var context: String { String(repeating: "c3", count: 32) }
    private var challenge: String { "OK AUTH2 CHALLENGE nonce=\(device) mac=\(challengeTag) ttl_ms=30000" }
    private var match: String { "OK AUTH2 MATCH nonce=\(client) context=\(context) mac=\(matchTag)" }
    private func request(nonce: UInt8 = 0xa1, ctx: UInt8 = 0xc3, serial: String = "TT-001122334455",
                         clock: @escaping () -> UInt64 = { 100 }) throws -> TinyTouchAuthRequest {
        try TinyTouchAuthRequest(key: Data(0..<32), serial: serial,
            contextHash: Data(repeating: ctx, count: 32), nonce: Data(repeating: nonce, count: 32), now: clock)
    }
    func testCrossLanguageKnownAnswersAndSingleUse() throws {
        let request = try request()
        XCTAssertEqual(request.hostID, "630dcd2966c43366")
        XCTAssertEqual(try request.proveCommand(challenge: challenge), "AUTH2 PROVE \(client) 997d2d06c12ebfcda1296aef4fbe15159bcb4a5500465639ac323473bb0151e4")
        try request.verifyMatch(match)
        XCTAssertThrowsError(try request.verifyMatch(match))
        XCTAssertThrowsError(try request.proveCommand(challenge: challenge))
    }
    func testNeverAcceptsAResultWithoutHostChallenge() throws {
        XCTAssertThrowsError(try request().verifyMatch(match))
    }
    func testLegacyDomainCannotBeEnabledBySpoofedCapabilities() throws {
        let oldChallenge = challenge.replacingOccurrences(of: challengeTag,
            with: "c1ef24a244a5abc8e5db050bd91af1614c1a9dac7be37288f2daec51209fed97")
        let request = try request()
        XCTAssertThrowsError(try request.proveCommand(challenge: oldChallenge))
        XCTAssertThrowsError(try request.proveCommand(challenge: challenge))
    }
    func testRejectsReplayedChallengeOnAnotherRequestContextOrDevice() throws {
        for request in [try request(nonce: 0xa2), try request(ctx: 0xc4), try request(serial: "TT-001122334456")] {
            XCTAssertThrowsError(try request.proveCommand(challenge: challenge))
        }
    }
    func testAllTagByteTamperingConsumesRequest() throws {
        for index in 0..<64 {
            var tag = Array(challengeTag); tag[index] = tag[index] == "0" ? "1" : "0"
            let request = try request()
            XCTAssertThrowsError(try request.proveCommand(challenge: challenge.replacingOccurrences(of: challengeTag, with: String(tag))))
            XCTAssertThrowsError(try request.proveCommand(challenge: challenge))
        }
    }
    func testMalformedAndReflectedProofsFailClosed() throws {
        for bad in [challenge + " mac=\(challengeTag)", challenge + " junk=1",
                    challenge.replacingOccurrences(of: "30000", with: "300000"),
                    challenge.replacingOccurrences(of: challengeTag, with: matchTag),
                    challenge.replacingOccurrences(of: " mac=", with: "  mac="), String(repeating: "x", count: 513)] {
            XCTAssertThrowsError(try request().proveCommand(challenge: bad))
        }
        for bad in [match.replacingOccurrences(of: matchTag, with: challengeTag),
                    match.replacingOccurrences(of: client, with: String(repeating: "a2", count: 32)),
                    match.replacingOccurrences(of: context, with: String(repeating: "c4", count: 32))] {
            let request = try request(); _ = try request.proveCommand(challenge: challenge)
            XCTAssertThrowsError(try request.verifyMatch(bad))
            XCTAssertThrowsError(try request.verifyMatch(match))
        }
    }
    func testExpiryBackwardClockAndCancellation() throws {
        var now: UInt64 = 100
        let request = try request(clock: { now })
        _ = try request.proveCommand(challenge: challenge)
        now += 30_000_000_000
        XCTAssertThrowsError(try request.verifyMatch(match))
        now = 100
        let backwards = try self.request(clock: { now }); now = 99
        XCTAssertThrowsError(try backwards.proveCommand(challenge: challenge))
        let cancelled = try self.request(); cancelled.invalidate()
        XCTAssertThrowsError(try cancelled.proveCommand(challenge: challenge))
    }
    func testCapabilityDoesNotFollowVersionLabel() throws {
        var fields = ["protocol": "6", "firmware": "0.1.36", "build": "auth-proof", "sensor": "ready", "fingerprints": "1", "hosts": "1", "mode": "hid"]
        XCTAssertFalse(try TinyTouchStatus(data: JSONEncoder().encode(fields)).authProofSupported)
        fields["auth_proof"] = "1"
        XCTAssertTrue(try TinyTouchStatus(data: JSONEncoder().encode(fields)).authProofSupported)
        fields["auth_proof"] = "true"
        XCTAssertThrowsError(try TinyTouchStatus(data: JSONEncoder().encode(fields)))
    }
    func testExtendedRequestAcceptsLateResultOnlyWithinSixtySeconds() throws {
        var now: UInt64 = 100
        let request = try request(clock: { now })
        _ = try request.proveCommand(challenge: challenge.replacingOccurrences(of: "30000", with: "60000"))
        now += 55_000_000_000
        try request.verifyMatch(match)
        XCTAssertThrowsError(try request.verifyMatch(match))
        now = 100
        let expired = try self.request(clock: { now })
        _ = try expired.proveCommand(challenge: challenge.replacingOccurrences(of: "30000", with: "60000"))
        now += 60_000_000_000
        XCTAssertThrowsError(try expired.verifyMatch(match))
    }
    func testLegacyLifetimeCannotBeExtendedByNewClient() throws {
        var now: UInt64 = 100
        let request = try request(clock: { now })
        now += 35_000_000_000
        XCTAssertThrowsError(try request.proveCommand(challenge: challenge))
        XCTAssertThrowsError(try request.proveCommand(challenge: challenge.replacingOccurrences(of: "30000", with: "60000")))
    }
    func testUnsupportedLifetimesAlwaysConsumeRequest() throws {
        for lifetime in ["45000", "60001", "060000", "-1", "0"] {
            let request = try request()
            XCTAssertThrowsError(try request.proveCommand(challenge: challenge.replacingOccurrences(of: "30000", with: lifetime)))
            XCTAssertThrowsError(try request.proveCommand(challenge: challenge))
        }
    }
    func testCryptographicProofAloneCannotEnableFreshAuthentication() throws {
        var fields = ["protocol": "6", "firmware": "0.1.36", "build": "auth-proof", "sensor": "ready", "fingerprints": "1", "hosts": "1", "mode": "hid", "auth_proof": "1"]
        XCTAssertFalse(try TinyTouchStatus(data: JSONEncoder().encode(fields)).authenticationSupported)
        fields["auth_fresh"] = "1"
        XCTAssertTrue(try TinyTouchStatus(data: JSONEncoder().encode(fields)).authenticationSupported)
        fields["auth_proof"] = "0"
        XCTAssertFalse(try TinyTouchStatus(data: JSONEncoder().encode(fields)).authenticationSupported)
        fields["auth_fresh"] = "true"
        XCTAssertThrowsError(try TinyTouchStatus(data: JSONEncoder().encode(fields)))
    }
    func testPromptWindowMetadataIsOptionalAndBounded() throws {
        var fields = ["protocol": "6", "firmware": "0.1.38", "build": "touch-window", "sensor": "ready", "fingerprints": "1", "hosts": "1", "mode": "hid", "auth_proof": "1", "auth_fresh": "1"]
        XCTAssertNil(try TinyTouchStatus(data: JSONEncoder().encode(fields)).authTouchWaitMilliseconds)
        fields["auth_touch_ms"] = "30000"
        XCTAssertEqual(try TinyTouchStatus(data: JSONEncoder().encode(fields)).authTouchWaitMilliseconds, 30000)
        for value in ["0", "30001", "030000", "-1", "wait"] {
            fields["auth_touch_ms"] = value
            XCTAssertThrowsError(try TinyTouchStatus(data: JSONEncoder().encode(fields)))
        }
    }
}
