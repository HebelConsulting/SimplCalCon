# ADR 0081 — Self-hosted ntfy for LAN push testing

## Status

Accepted — implemented. A demo/test-harness aid for WebDAV-Push (ADR 0052), not a product feature.

## Context

WebDAV-Push is verified server-side end-to-end (ADR 0052 notes + the acceptance matrix): SimplCalCon
encrypts and delivers the push to the push service. The last hop — push service → ntfy app → DAVx⁵ —
stalled during on-device testing because the **ntfy app couldn't hold its WebSocket to the public
`ntfy.sh`** on the test device/network ("Websocket not supported"). To verify the on-device wake we want
the whole push path on the LAN, off the public relay.

## Decision

Add an **opt-in self-hosted ntfy** to the LAN test stack (`docker-compose.lan.yaml`), gated behind a
`push` compose **profile** (like pgAdmin's `tools` profile) so it never runs unless asked:

```
LAN_HOST=… docker compose -f docker-compose.yaml -f docker-compose.lan.yaml --profile push up -d
```

- **ntfy** (`binwiederhier/ntfy`, Apache-2.0) listens only on the internal compose network; **Caddy
  fronts it over HTTPS at `https://<LAN_HOST>:8443`** with the same `tls internal` CA the phone already
  trusts from the DAV cert step (`deploy/Caddyfile.lan` gains a `:8443 → ntfy:80` site; the proxy
  publishes `8443`). `NTFY_BASE_URL=https://<LAN_HOST>:8443` so the endpoints ntfy hands DAVx⁵ point back
  through Caddy. Point the ntfy Android app's **default server** at that URL. Ephemeral (no volume).
- **Server-side TLS trust:** the api sends the Web Push to `https://<LAN_HOST>:8443/…`, so it must trust
  Caddy's internal CA. Rather than plumbing the CA into the container, a **Development/demo-only flag
  `SimplCalCon:WebPush:AllowUntrustedPushEndpointTls`** (default **false**) makes **only the WebPush
  sender's `HttpClient`** skip cert validation (`DangerousAcceptAnyServerCertificateValidator`). It's set
  `true` in the LAN override and logs a startup warning; it is off everywhere else and never affects
  production. (An image chosen because it isn't a NuGet/npm package, ntfy sits outside the license gate;
  its Apache-2.0 license fits the allowlist regardless.)

## Consequences

- The full push path (SimplCalCon → self-hosted ntfy → ntfy app → DAVx⁵) can run entirely on the LAN,
  removing the public-relay WebSocket dependency that blocked on-device verification.
- A TLS-validation-skip flag now exists. It is narrowly scoped (WebPush outbound only), off by default,
  and loudly logged when on — but it is a real footgun if misconfigured in production; hence the naming
  (`AllowUntrusted…`) and the warning. Weighed against mounting the CA into the container, this is the
  lower-friction demo choice the maintainer selected.
- Nothing changes for the default stack or production: no ntfy, no open 8443, flag off.

## Deferred

- Real TLS trust (mount + trust the CA) instead of the skip flag, if a self-hosted ntfy ever becomes a
  supported production topology rather than a test aid.
- Automated verification of the on-device wake (still manual — needs a real DAVx⁵ device).
