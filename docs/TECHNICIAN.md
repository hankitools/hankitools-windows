# HANKI-701–703 — technician session and seat foundation

A technician session wraps the same DiagnosticScan and RepairReport used by the
consumer workflow; it does not run a separate scanner or repair engine. Optional
customer/device labels can be anonymous job numbers. No customer identity,
hardware serial number or cloud upload is required. Local saved sessions minimize
evidence using the same history privacy boundary. Customer report generation
reuses existing findings, recommendations and verification without another scan.
Business display name/contact are caller-configurable; exports must be reviewed.
Technical details are optional and pattern redaction is not an anonymity guarantee.

Technician-session and customer-report capabilities are centralized. Community
and consumer Pro tools are unchanged. Production Technician licensing is not
connected; the same Debug-only development profile can exercise this foundation.
No invoicing, remote control or multi-tenant portal is introduced.

A paid seat belongs to a technician subject/organization. Registered workstations
are that technician's operating devices and have provider-enforced limits.
CustomerDeviceSession is temporary job context and never consumes a permanent
workstation seat. Signed provider claims must set expiry and a bounded field-use
grace window; the client must never grant itself unlimited offline time. Revoked
workstations fail closed for paid actions while Community remains available.

Replacing a technician workstation requires provider-authorized revocation of the
old registration and registration of the replacement. Lost-device recovery must
use the future identity provider with audit/abuse limits, not a mutable local flag.
Seat enforcement and revocation belong on the server for hosted services. Offline
replay, copied signed claims, clock rollback, sharing one technician account and
excessive replacement requests need provider controls. These models do not claim
tamper resistance on a compromised or modified open-source client.
