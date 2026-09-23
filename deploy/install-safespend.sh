#!/usr/bin/env bash
set -euo pipefail

if [[ "$EUID" -ne 0 ]]; then
    echo "Run this installer as root." >&2
    exit 1
fi

if ! command -v logrotate >/dev/null 2>&1; then
    echo "logrotate is required. Install it before running this installer." >&2
    exit 1
fi

script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
config_file="${SAFESPEND_UPDATE_CONFIG:-/etc/safespend/update.env}"
if [[ -f "$config_file" ]]; then
    # This file contains host deployment settings, never application secrets.
    # shellcheck disable=SC1090
    source "$config_file"
fi

app_root="${SAFESPEND_APP_ROOT:-/opt/safespend}"
app_user="${SAFESPEND_USER:-safespend}"
app_group="${SAFESPEND_GROUP:-$app_user}"
data_directory="${SAFESPEND_DATA_DIRECTORY:-/var/lib/safespend}"
service_name="${SAFESPEND_SERVICE:-safespend}"
repository="${SAFESPEND_REPOSITORY:-ComputerComa/SafeSpend}"
replace_service="${SAFESPEND_REPLACE_SERVICE:-false}"

if [[ ! "$repository" =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]]; then
    echo "SAFESPEND_REPOSITORY must look like owner/repository." >&2
    exit 1
fi

if [[ "$data_directory" != /* || "$data_directory" == "/" ]]; then
    echo "SAFESPEND_DATA_DIRECTORY must be a dedicated absolute path." >&2
    exit 1
fi

if ! id "$app_user" >/dev/null 2>&1; then
    useradd --system --home-dir /var/lib/safespend \
        --create-home --shell /usr/sbin/nologin "$app_user"
fi

install -d -o root -g root -m 0755 \
    "$app_root" \
    "$app_root/releases" \
    "/etc/safespend"
install -d -o "$app_user" -g "$app_group" -m 0750 \
    "$data_directory"
chown -R -- "$app_user:$app_group" "$data_directory"

install -o root -g root -m 0755 \
    "$script_directory/safespend-update.sh" \
    /usr/local/sbin/safespend-update
service_file="/etc/systemd/system/$service_name.service"
if [[ ! -f "$service_file" || "$replace_service" == "true" ]]; then
    install -o root -g root -m 0644 \
        "$script_directory/safespend.service" \
        "$service_file"
else
    echo "Preserving existing systemd unit: $service_file"
fi
install -o root -g root -m 0644 \
    "$script_directory/safespend.logrotate" \
    /etc/logrotate.d/safespend

if [[ ! -f /etc/safespend/update.env ]]; then
    install -o root -g root -m 0600 \
        "$script_directory/safespend-update.env.example" \
        /etc/safespend/update.env
    sed -i "s#^SAFESPEND_REPOSITORY=.*#SAFESPEND_REPOSITORY=$repository#" \
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
