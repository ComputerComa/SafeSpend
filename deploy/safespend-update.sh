#!/usr/bin/env bash
set -euo pipefail

config_file="${SAFESPEND_UPDATE_CONFIG:-/etc/safespend/update.env}"
if [[ -f "$config_file" ]]; then
    # This file contains deployment settings only. Keep Plaid credentials in
    # the separate systemd EnvironmentFile.
    # shellcheck disable=SC1090
    source "$config_file"
fi

repository="${SAFESPEND_REPOSITORY:-ComputerComa/SafeSpend}"
app_root="${SAFESPEND_APP_ROOT:-/opt/safespend}"
service_name="${SAFESPEND_SERVICE:-safespend}"
app_user="${SAFESPEND_USER:-safespend}"
app_group="${SAFESPEND_GROUP:-$app_user}"
keep_releases="${SAFESPEND_KEEP_RELEASES:-3}"
requested_version="${1:-latest}"

if [[ "$EUID" -ne 0 ]]; then
    echo "Run this updater as root (for example: sudo safespend-update)." >&2
    exit 1
fi

if [[ ! "$repository" =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]]; then
    echo "SAFESPEND_REPOSITORY must look like owner/repository." >&2
    exit 1
fi

if [[ ! "$requested_version" =~ ^(latest|v?[A-Za-z0-9][A-Za-z0-9_.-]*)$ ]]; then
    echo "Version must be latest or a release tag such as v1.2.3." >&2
    exit 1
fi

if [[ "$requested_version" == "latest" ]]; then
    asset_stem="SafeSpend-latest"
    download_base="https://github.com/$repository/releases/latest/download/$asset_stem"
    release_directory="$app_root/releases/latest-$(date -u +%Y%m%d%H%M%S)-$$"
else
    asset_stem="SafeSpend-$requested_version"
    download_base="https://github.com/$repository/releases/download/$requested_version/$asset_stem"
    release_directory="$app_root/releases/$requested_version"
fi

releases_directory="$app_root/releases"
current_link="$app_root/current"
mkdir -p "$releases_directory"

temporary_directory="$(mktemp -d)"
trap 'rm -rf "$temporary_directory"' EXIT

archive="$temporary_directory/$asset_stem.zip"
checksum="$temporary_directory/$asset_stem.zip.sha256"

echo "Downloading $asset_stem from $repository..."
curl --fail --location --silent --show-error \
    "$download_base.zip" \
    --output "$archive"
curl --fail --location --silent --show-error \
    "$download_base.zip.sha256" \
    --output "$checksum"

(cd "$temporary_directory" && sha256sum --check "$(basename "$checksum")")
unzip -tq "$archive" >/dev/null

if [[ -e "$release_directory" ]]; then
    echo "Release directory already exists: $release_directory" >&2
    echo "Remove it only if you intentionally want to reinstall that version." >&2
    exit 1
fi

staging_directory="$releases_directory/.staging-$$"
mkdir -p "$staging_directory"
unzip -q "$archive" -d "$staging_directory"

if [[ ! -f "$staging_directory/SafeSpend.Web.dll" ]]; then
    echo "The release did not contain SafeSpend.Web.dll." >&2
    rm -rf "$staging_directory"
    exit 1
fi

mv "$staging_directory" "$release_directory"
chown -R "$app_user:$app_group" "$release_directory"

previous_target=""
if [[ -L "$current_link" ]]; then
    previous_target="$(readlink -f "$current_link")"
fi

new_link="$app_root/.current-$$"
ln -s "$release_directory" "$new_link"
mv -Tf "$new_link" "$current_link"

echo "Restarting $service_name..."
if ! systemctl restart "$service_name" ||
   ! systemctl is-active --quiet "$service_name"; then
    echo "The new release did not start. Restoring the previous release." >&2
    if [[ -n "$previous_target" && -d "$previous_target" ]]; then
        rollback_link="$app_root/.rollback-$$"
        ln -s "$previous_target" "$rollback_link"
        mv -Tf "$rollback_link" "$current_link"
        systemctl restart "$service_name" || true
    fi
    exit 1
fi

echo "Updated to $requested_version."

if [[ "$keep_releases" =~ ^[1-9][0-9]*$ ]]; then
    mapfile -t old_releases < <(
        find "$releases_directory" \
            -mindepth 1 \
            -maxdepth 1 \
            -type d \
            ! -name ".staging-*" \
            -printf '%T@ %p\n' |
            sort -nr |
            tail -n +"$((keep_releases + 1))" |
            cut -d' ' -f2-
    )

    current_target="$(readlink -f "$current_link")"
    for old_release in "${old_releases[@]}"; do
        if [[ "$current_target" != "$old_release" ]]; then
            rm -rf -- "$old_release"
        fi
    done
fi
