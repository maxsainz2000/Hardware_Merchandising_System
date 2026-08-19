# P1-09 evidence — client certificate trust procedure

This is the procedure documented at `docs/installation-guide.md` section 4, captured here as
standalone evidence per the task card. Run once per client machine, after `MERCH-HOST` name
resolution (installation guide section 1) is already working on that client.

## What you need

- `merch-host.cer` — the **public** certificate only. Get it from the host administrator (it
  lives at `C:\ProgramData\MerchandisingSystem\certs\merch-host.cer` on the host). Never accept
  a `.pfx` file for this step — that file carries the private key and has no reason to leave
  the host at all.
- The certificate's expected thumbprint, out of band from the host administrator — this run's
  value is recorded in `evidence/phase-1/p1-09-cert-details.txt`:
  `759021AA351D8572EF19DD24ADDEF1A94A4849C9`. **Confirm the thumbprint before trusting it.** That
  confirmation, done by a human comparing two strings, is the entire security value of a manual
  trust step — automating it away would defeat the point.
- Administrator rights on the client, if trusting for every user on that machine
  (`Cert:\LocalMachine\Root`). A single-user trust (`Cert:\CurrentUser\Root`) does not need
  admin rights and is what this evidence run used, since the "client" and "host" are the same
  lab machine in this capture.

## Procedure — PowerShell

```powershell
$certPath = "<path to the merch-host.cer file you received>"
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certPath)

# Compare against the thumbprint the host administrator gave you before continuing.
$cert.Thumbprint

$store = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root", "LocalMachine")
$store.Open("ReadWrite")
$store.Add($cert)
$store.Close()
```

Use `"CurrentUser"` in place of `"LocalMachine"` if you don't have admin rights on this client
and only need the API trusted for your own Windows account.

## Procedure — GUI (equivalent, no PowerShell)

1. Double-click `merch-host.cer`.
2. **Install Certificate** → **Local Machine** (or **Current User**) → **Next**.
3. **Place all certificates in the following store** → **Browse** → **Trusted Root Certification
   Authorities** → **OK** → **Next** → **Finish**.
4. Windows shows a security warning with the certificate's thumbprint. **Compare it against the
   value the host administrator gave you before clicking Yes.**

## Verify

```powershell
Invoke-WebRequest https://MERCH-HOST:8443/health
```

Expected: `200 OK`, no certificate warning, no `-SkipCertificateCheck` needed.

## What this evidence run actually proved

Captured live against this task's real Kestrel process and real self-signed certificate —
see `evidence/phase-1/p1-09-invalid-cert-behaviour.txt` for the full transcript:

1. **Before** trust is installed: TLS handshake fails with `RemoteCertificateChainErrors` —
   Windows correctly refuses a self-signed certificate nobody has told it to trust.
2. **After** running the trust procedure above: the identical connection, to the identical
   certificate, succeeds (`TLS 1.3`, `SslPolicyErrors: None`) and `/health` returns `200 OK`.
3. Connecting by **IP address** instead of the name `MERCH-HOST` still fails even after trust is
   installed (`RemoteCertificateNameMismatch`) — proving the name-only SAN decision (ADR-011) is
   actually enforced, not just documented.

**What this does not prove.** All three steps above ran on one machine (`LAPTOP-3HH6OHHE`) using
a raw TLS client to simulate "before" and "after" trust, because the lab test workstation is not
currently on this network (`docs/installation-guide.md` section 1.5). The trust *mechanism* is
proven; a literal second physical machine following this exact procedure is not yet proven and
stays an open item on the P1-09 task card.
