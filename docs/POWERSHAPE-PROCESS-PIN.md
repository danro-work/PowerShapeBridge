# Explicit PowerShape process attachment

When more than one PowerShape process is running, an optional general-v1
`powerShapeProcessPin` identifies the intended existing process. It requires all
four fields: positive `processId`, exact UTC `processStartedUtc`, nonnegative
`windowsSessionId`, and canonical absolute `executablePath` ending in
`powershape.exe`. These are local configuration values; process identities from
a previous run are observations, not reusable authority.

Native attachment compares the registered COM application's `app.pid`, its
documented read-only `MainWindowHWND`, the Windows owner of that window, and the
live process creation time, session and executable. Retained document context
checks repeat this validation. A mismatch stops the operation. There is no
fallback to another instance, process ordering, automatic launch or close. If
the registered COM application belongs to a different process, this mechanism
refuses; it does not locate an arbitrary instance through COM.

Without a pin, the existing single-process requirement remains. Python, SDK and
Native validate the configuration independently. The read-only server remains
separate; a pin grants no document, ownership, selection or mutation authority.

Native 0.66.0-dev / staged SDK 0.72.0-dev were checked with two real PowerShape
instances present. After a backed-up panel deployment, ordinary read-only model
inspection and listing both verified the intended main process and its 28
documents. The other process was left untouched. No activation, geometry,
SaveAs, OPEN or close was performed in this attachment qualification.

Seventeen Native identity/config/default checks, ten SDK configuration tests,
and 31 Python configuration tests passed. `verify-process-pin.ps1` additionally
checks that the isolated fixture refuses before its marker exists; it never
connects to CAD. These tests qualify attachment validation, not multi-instance
discovery or the pending ordered surface-set CAD scenario.

An independent ownership limitation remains before that scenario: the old
successful OPEN verifier checks the original complete window set. Additional
documents opened later cannot be treated as the same OPEN postcondition.
Restoring current access must use a separately qualified observation of the
successful history and current owned document, without replaying OPEN or
relaxing its original verifier. The interrupted-OPEN adoption tool does not
apply to an already successful OPEN.
