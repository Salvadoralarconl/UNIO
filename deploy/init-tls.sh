#!/bin/sh
set -eu
umask 077
if [ -f /tls/origin.crt ] && [ -f /tls/origin.key ]; then
  openssl x509 -checkend 86400 -noout -in /tls/origin.crt >/dev/null
  echo 'Existing private origin certificate retained.'
  exit 0
fi
if [ -e /tls/origin.crt ] || [ -e /tls/origin.key ]; then
  echo 'Incomplete origin certificate pair; inspect the private TLS volume.' >&2
  exit 1
fi
openssl req -x509 -newkey rsa:3072 -sha256 -nodes -days 365 \
  -subj '/CN=api' -addext 'subjectAltName=DNS:api' \
  -keyout /tls/origin.key -out /tls/origin.crt 2>/dev/null
chown 1654:1654 /tls/origin.key
chmod 600 /tls/origin.key
chmod 644 /tls/origin.crt
echo 'Created private API origin certificate. Never publish the TLS volume.'
