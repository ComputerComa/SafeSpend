#!/usr/bin/env bash
set -euo pipefail

if [[ "$EUID" -ne 0 ]]; then
    echo "Run this installer as root." >&2
    exit 1
fi

script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
app_root="${SAFESPEND_APP_ROOT:-/opt/safespend}"
app_user="${SAFESPEND_USER:-safespend}"
app_group="${SAFESPEND_GROUP:-$app_user}"
service_name="${SAFESPEND_SERVICE:-safespend}"

if ! id "$app_user" >/dev/null 2>&1; then
    useradd --system --home-dir /var/lib/safespend \
        --create-home --shell /usr/sbin/nologin "$app_user"
fi

install -d -o root -g root -m 0755 \
    "$app_root" \
    "$app_root/releases" \
    "/etc/safespend"
install -d -o "$app_user" -g "$app_group" -m 0750 \
    "/var/lib/safespend"

install -o root -g root -m 0755 \
    "$script_directory/safespend-update.sh" \
    /usr/local/sbin/safespend-update
install -o root -g root -m 0644 \
    "$script_directory/safespend.service" \
    "/etc/systemd/system/$service_name.service"

if [[ ! -f /etc/safespend/update.env ]]; then
    install -o root -g root -m 0600 \
        "$script_directory/safespend-update.env.example" \
        /etc/safespend/update.env
    echo "Edit /etc/safespend/update.env, then run safespend-update."
fi

if [[ ! -f /etc/safespend/safespend.env ]]; then
    install -o root -g root -m 0600 \
        "$script_directory/safespend.env.example" \
        /etc/safespend/safespend.env
    echo "Edit /etc/safespend/safespend.env with production settings, then run safespend-update."
    exit 0
fi

systemctl daemon-reload
systemctl enable "$service_name.service"
/usr/local/sbin/safespend-update
