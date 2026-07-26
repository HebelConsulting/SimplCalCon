#!/usr/bin/env bash
#
# DAV wire smoke test (manual-acceptance harness, ADR 0003).
#
# Exercises the CalDAV/CardDAV surface of a *running, deployed* instance exactly as a native client
# does its first connection: well-known discovery -> current-user-principal -> home-sets -> a
# calendar/address-book -> a PUT/GET/REPORT/DELETE round-trip. Confirms the deployment, app-password
# auth, and the TLS proxy work *before* you try a real client (Apple / DAVx5 / Thunderbird / Outlook
# CalDav Synchronizer) — and gives a quick repro if one of those misbehaves.
#
# The integration test suite already asserts the protocol at the XML level against the in-memory
# server; this instead hits a real endpoint over HTTP(S) with Basic auth, which those tests don't.
#
# Usage:
#   scripts/dav-smoke.sh <base-url> <email> <app-password>
#   scripts/dav-smoke.sh https://localhost you@example.com "app-pw"     # demo Caddy (self-signed)
#   scripts/dav-smoke.sh http://localhost:9080 you@example.com "app-pw" # plain HTTP
#
# Notes:
#   * Use an APP PASSWORD (Configuration tab in the web UI), never the account password.
#   * For the demo's self-signed Caddy cert, TLS verification is disabled (curl -k). Against a real
#     cert, export DAV_SMOKE_STRICT_TLS=1 to verify it.
#   * Requires: bash, curl.

set -u

BASE="${1:-}"
EMAIL="${2:-}"
APP_PW="${3:-}"

if [[ -z "$BASE" || -z "$EMAIL" || -z "$APP_PW" ]]; then
  grep '^#' "$0" | sed 's/^# \{0,1\}//' | sed -n '2,30p'
  exit 2
fi

BASE="${BASE%/}"
INSECURE="-k"
[[ "${DAV_SMOKE_STRICT_TLS:-0}" == "1" ]] && INSECURE=""

PASS=0
FAIL=0
SKIP=0

green() { printf '\033[32m%s\033[0m' "$1"; }
red()   { printf '\033[31m%s\033[0m' "$1"; }
yellow(){ printf '\033[33m%s\033[0m' "$1"; }

ok()   { echo "  $(green "PASS")  $1"; PASS=$((PASS+1)); }
bad()  { echo "  $(red   "FAIL")  $1"; FAIL=$((FAIL+1)); }
skip() { echo "  $(yellow "SKIP")  $1"; SKIP=$((SKIP+1)); }

# req METHOD PATH [body] [extra curl args...] -> sets $STATUS and $BODY
req() {
  local method="$1" path="$2" body="${3:-}"; shift; shift; [[ $# -gt 0 ]] && shift
  local tmp; tmp="$(mktemp)"
  STATUS="$(curl $INSECURE -s -o "$tmp" -w '%{http_code}' \
    -u "$EMAIL:$APP_PW" -X "$method" "$@" \
    ${body:+--data-binary "$body"} \
    "$BASE$path")"
  BODY="$(cat "$tmp")"; rm -f "$tmp"
}

propfind() { req PROPFIND "$1" "$3" -H "Depth: $2" -H "Content-Type: application/xml"; }

# First href in an XML multistatus body (namespace-prefix tolerant), optionally after skipping N.
href_at() {
  echo "$BODY" | grep -oiE '<[a-z0-9]*:?href[^>]*>[^<]+' | sed -E 's/.*href[^>]*>//' | sed -n "$(( $1 + 1 ))p"
}

echo "SimplCalCon DAV smoke test → $BASE  (user: $EMAIL)"
echo

echo "Discovery"
req GET /.well-known/caldav "" -I
[[ "$STATUS" =~ ^30[12]$ ]] && ok "/.well-known/caldav redirects ($STATUS)" || bad "/.well-known/caldav expected 301/302, got $STATUS"
req GET /.well-known/carddav "" -I
[[ "$STATUS" =~ ^30[12]$ ]] && ok "/.well-known/carddav redirects ($STATUS)" || bad "/.well-known/carddav expected 301/302, got $STATUS"

propfind /dav/ 0 '<propfind xmlns="DAV:"><prop><current-user-principal/></prop></propfind>'
if [[ "$STATUS" == "401" ]]; then
  bad "PROPFIND /dav/ returned 401 — check the email + app password"
elif [[ "$STATUS" == "207" && "$BODY" == *"current-user-principal"* ]]; then
  ok "authenticated; current-user-principal returned"
else
  bad "PROPFIND /dav/ expected 207 with current-user-principal, got $STATUS"
fi
PRINCIPAL="$(echo "$BODY" | grep -oiE '<[a-z0-9]*:?current-user-principal[^>]*>.*</[a-z0-9]*:?current-user-principal>' | grep -oiE 'href[^>]*>[^<]+' | sed -E 's/.*>//' | head -1)"
[[ -n "$PRINCIPAL" ]] && ok "principal path: $PRINCIPAL" || skip "could not extract the principal href (later steps limited)"

echo
echo "CalDAV"
CAL_HOME=""
if [[ -n "$PRINCIPAL" ]]; then
  propfind "$PRINCIPAL" 0 '<propfind xmlns="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav"><prop><c:calendar-home-set/></prop></propfind>'
  CAL_HOME="$(echo "$BODY" | grep -oiE 'calendar-home-set[^>]*>.*calendar-home-set>' | grep -oiE 'href[^>]*>[^<]+' | sed -E 's/.*>//' | head -1)"
  [[ -n "$CAL_HOME" ]] && ok "calendar-home-set: $CAL_HOME" || bad "no calendar-home-set advertised"
fi

CAL=""
if [[ -n "$CAL_HOME" ]]; then
  propfind "$CAL_HOME" 1 '<propfind xmlns="DAV:"><prop><resourcetype/><displayname/></prop></propfind>'
  [[ "$STATUS" == "207" ]] && ok "calendar home PROPFIND Depth:1 ($STATUS)" || bad "calendar home PROPFIND got $STATUS"
  # First child collection href (skip index 0 = the home itself).
  CAL="$(echo "$BODY" | grep -oiE '<[a-z0-9]*:?href[^>]*>[^<]+' | sed -E 's/.*href[^>]*>//' | grep '/$' | grep -v "^${CAL_HOME}$" | head -1)"
  [[ -n "$CAL" ]] && ok "found a calendar: $CAL" || skip "no calendar collection found (create one in the web UI)"
fi

if [[ -n "$CAL" ]]; then
  NAME="smoke-$$-$(date +%s).ics"
  ICS=$'BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//smoke//EN\r\nBEGIN:VEVENT\r\nUID:smoke-'"$$"$'@t\r\nDTSTAMP:20260715T090000Z\r\nDTSTART:20260715T090000Z\r\nDTEND:20260715T093000Z\r\nSUMMARY:DAV smoke\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n'
  req PUT "${CAL}${NAME}" "$ICS" -H "Content-Type: text/calendar"
  [[ "$STATUS" =~ ^20[01]$ ]] && ok "PUT event ($STATUS)" || bad "PUT event got $STATUS"
  req GET "${CAL}${NAME}"
  [[ "$STATUS" == "200" && "$BODY" == *"DAV smoke"* ]] && ok "GET event round-trips" || bad "GET event got $STATUS"
  req REPORT "$CAL" '<c:calendar-query xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav"><d:prop><d:getetag/></d:prop><c:filter><c:comp-filter name="VCALENDAR"><c:comp-filter name="VEVENT"><c:time-range start="20260715T000000Z" end="20260716T000000Z"/></c:comp-filter></c:comp-filter></c:filter></c:calendar-query>' -H "Depth: 1" -H "Content-Type: application/xml"
  [[ "$STATUS" == "207" && "$BODY" == *"$NAME"* ]] && ok "calendar-query time-range returns the event" || bad "calendar-query got $STATUS"
  req DELETE "${CAL}${NAME}"
  [[ "$STATUS" =~ ^20[04]$ ]] && ok "DELETE event ($STATUS)" || bad "DELETE event got $STATUS"
fi

echo
echo "CardDAV"
if [[ -n "$PRINCIPAL" ]]; then
  propfind "$PRINCIPAL" 0 '<propfind xmlns="DAV:" xmlns:c="urn:ietf:params:xml:ns:carddav"><prop><c:addressbook-home-set/></prop></propfind>'
  AB_HOME="$(echo "$BODY" | grep -oiE 'addressbook-home-set[^>]*>.*addressbook-home-set>' | grep -oiE 'href[^>]*>[^<]+' | sed -E 's/.*>//' | head -1)"
  [[ -n "$AB_HOME" ]] && ok "addressbook-home-set: $AB_HOME" || bad "no addressbook-home-set advertised"
  if [[ -n "${AB_HOME:-}" ]]; then
    propfind "$AB_HOME" 1 '<propfind xmlns="DAV:"><prop><resourcetype/></prop></propfind>'
    AB="$(echo "$BODY" | grep -oiE '<[a-z0-9]*:?href[^>]*>[^<]+' | sed -E 's/.*href[^>]*>//' | grep '/$' | grep -v "^${AB_HOME}$" | head -1)"
    [[ -n "$AB" ]] && ok "found an address book: $AB" || skip "no address book found (the default 'contacts' auto-provisions on first access)"
    if [[ -n "$AB" ]]; then
      VNAME="smoke-$$-$(date +%s).vcf"
      VCF=$'BEGIN:VCARD\r\nVERSION:3.0\r\nUID:smoke-'"$$"$'@t\r\nFN:DAV Smoke\r\nN:Smoke;DAV;;;\r\nEND:VCARD\r\n'
      req PUT "${AB}${VNAME}" "$VCF" -H "Content-Type: text/vcard"
      [[ "$STATUS" =~ ^20[01]$ ]] && ok "PUT contact ($STATUS)" || bad "PUT contact got $STATUS"
      req DELETE "${AB}${VNAME}"
      [[ "$STATUS" =~ ^20[04]$ ]] && ok "DELETE contact ($STATUS)" || bad "DELETE contact got $STATUS"
    fi
  fi
fi

echo
echo "----"
echo "$(green "$PASS passed"), $([[ $FAIL -gt 0 ]] && red "$FAIL failed" || echo "0 failed"), $SKIP skipped"
[[ $FAIL -eq 0 ]] && echo "Wire looks good — a real client should connect with the same URL + credentials." || echo "Fix the failures above before pointing a native client at this instance."
exit $(( FAIL > 0 ? 1 : 0 ))
