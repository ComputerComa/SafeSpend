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
data_directory="${SAFESPEND_DATA_DIRECTORY:-/var/lib/safespend}"
keep_releases="${SAFESPEND_KEEP_RELEASES:-3}"
replace_service="${SAFESPEND_REPLACE_SERVICE:-false}"
requested_version="${1:-latest}"

if [[ "$EUID" -ne 0 ]]; then
    echo "Run this updater as root (for example: sudo safespend-update)." >&2
    exit 1
fi

if [[ "$requested_version" == "--version" ]]; then
    version_file="$app_root/current/VERSION"
    if [[ -f "$version_file" ]]; then
        cat "$version_file"
    else
        echo "version=unknown (legacy release without VERSION metadata)"
        if [[ -L "$app_root/current" ]]; then
            echo "release_path=$(readlink -f "$app_root/current")"
        fi
    fi
    exit 0
fi

if [[ "$requested_version" == "--diagnose" ]]; then
    echo "SafeSpend deployment diagnostics"
    if [[ -f "$app_root/current/VERSION" ]]; then
        cat "$app_root/current/VERSION"
    else
        echo "version=unknown (legacy release without VERSION metadata)"
    fi
    if [[ -L "$app_root/current" ]]; then
        echo "release_path=$(readlink -f "$app_root/current")"
    fi
    systemctl show "$service_name" \
        --property=User \
        --property=Group \
        --property=ActiveState \
        --property=SubState \
        --property=ReadWritePaths \
        --property=ProtectSystem
    echo "data_directory=$data_directory"
    find "$data_directory" "$app_root/releases" \
        -maxdepth 6 \
        -type f \
        -name 'key-*.xml' \
        -printf 'key_file=%m %u:%g %p\n' 2>/dev/null || true
    exit 0
fi

if [[ ! "$repository" =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]]; then
    echo "SAFESPEND_REPOSITORY must look like owner/repository." >&2
    exit 1
fi

if [[ ! "$requested_version" =~ ^(latest|v[0-9]+\.[0-9]+\.[0-9]+([.-][A-Za-z0-9.-]+)?)$ ]]; then
    echo "Version must be latest or a release tag such as v1.2.3." >&2
    exit 1
fi

if [[ "$data_directory" != /* || "$data_directory" == "/" ]]; then
    echo "SAFESPEND_DATA_DIRECTORY must be a dedicated absolute path." >&2
    exit 1
fi

if [[ "$requested_version" == "latest" ]]; then
    asset_stem="SafeSpend-latest"
    download_base="https://github.com/$repository/releases/latest/download/$asset_stem"
else
    asset_stem="SafeSpend-$requested_version"
    download_base="https://github.com/$repository/releases/download/$requested_version/$asset_stem"
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

staging_directory="$releases_directory/.staging-$$"
mkdir -p "$staging_directory"
unzip -q "$archive" -d "$staging_directory"

if [[ ! -f "$staging_directory/SafeSpend.Web.dll" ]]; then
    echo "The release did not contain SafeSpend.Web.dll." >&2
    rm -rf "$staging_directory"
    exit 1
fi

version_file="$staging_directory/VERSION"
if [[ ! -f "$version_file" ]]; then
    echo "The release did not contain VERSION metadata." >&2
    exit 1
fi

release_version="$(sed -n 's/^version=//p' "$version_file")"
release_commit="$(sed -n 's/^commit=//p' "$version_file")"
if [[ ! "$release_version" =~ ^v[0-9]+\.[0-9]+\.[0-9]+([.-][A-Za-z0-9.-]+)?$ ||
      ! "$release_commit" =~ ^[0-9a-f]{40}$ ]]; then
    echo "The release contained invalid VERSION metadata." >&2
    exit 1
fi
if [[ "$requested_version" != "latest" &&
      "$requested_version" != "$release_version" ]]; then
    echo "Requested $requested_version but downloaded $release_version." >&2
    exit 1
fi

release_directory="$releases_directory/$release_version"
if [[ -e "$release_directory" ]]; then
    if ! cmp -s "$version_file" "$release_directory/VERSION"; then
        echo "Release directory has different metadata: $release_directory" >&2
        exit 1
    fi
    rm -rf -- "$staging_directory"
    echo "SafeSpend $release_version is already present; activating it."
else
    mv "$staging_directory" "$release_directory"
    chown -R "$app_user:$app_group" "$release_directory"
fi

# Keep the host-level service and log rotation policy in step with the
# application release. They are outside the versioned application directory.
service_file="/etc/systemd/system/$service_name.service"
if [[ -f "$release_directory/deploy/safespend.service" &&
      (! -f "$service_file" || "$replace_service" == "true") ]]; then
    install -o root -g root -m 0644 \
        "$release_directory/deploy/safespend.service" \
        "$service_file"
elif [[ -f "$service_file" ]]; then
    echo "Preserving existing systemd unit: $service_file"
fi
if [[ -f "$release_directory/deploy/safespend.logrotate" ]]; then
    install -o root -g root -m 0644 \
        "$release_directory/deploy/safespend.logrotate" \
        /etc/logrotate.d/safespend
fi
if [[ -f "$release_directory/deploy/safespend-update.sh" ]]; then
    install -o root -g root -m 0755 \
        "$release_directory/deploy/safespend-update.sh" \
        /usr/local/sbin/safespend-update
fi
systemctl daemon-reload

previous_target=""
if [[ -L "$current_link" ]]; then
    previous_target="$(readlink -f "$current_link")"
fi

# Older releases used the resolved content-root path as the ASP.NET Data
# Protection application discriminator. Preserve every known release path so
# the new application can unlock a legacy Plaid token once and immediately
# re-protect it with the stable SafeSpend discriminator.
legacy_names_file="$data_directory/legacy-data-protection-applications"
legacy_names_staging="$temporary_directory/legacy-data-protection-applications"
install -d -o "$app_user" -g "$app_group" -m 0750 "$data_directory"
if [[ -f "$legacy_names_file" ]]; then
    cp "$legacy_names_file" "$legacy_names_staging"
else
    : > "$legacy_names_staging"
fi
printf '%s\n%s/\n' "$current_link" "$current_link" \
    >> "$legacy_names_staging"
while IFS= read -r release_path; do
    printf '%s\n%s/\n' "$release_path" "$release_path" \
        >> "$legacy_names_staging"
done < <(find "$releases_directory" -mindepth 1 -maxdepth 1 -type d)
sort -u "$legacy_names_staging" -o "$legacy_names_staging"
install -o "$app_user" -g "$app_group" -m 0600 \
    "$legacy_names_staging" "$legacy_names_file"

# Preserve old Data Protection key rings outside release directories before
# retention cleanup. Each release keeps its original key-directory layout so
# independent key rings are not merged.
legacy_key_archive="$data_directory/legacy-data-protection-keys"
while IFS= read -r legacy_key_file; do
    relative_key_path="${legacy_key_file#"$releases_directory"/}"
    archived_key_file="$legacy_key_archive/$relative_key_path"
    install -d -o "$app_user" -g "$app_group" -m 0700 \
        "$(dirname -- "$archived_key_file")"
    if [[ ! -f "$archived_key_file" ]]; then
        install -o "$app_user" -g "$app_group" -m 0600 \
            "$legacy_key_file" "$archived_key_file"
    fi
done < <(find "$releases_directory" \
    -mindepth 2 \
    -type f \
    -name 'key-*.xml')
chown -R -- "$app_user:$app_group" "$data_directory"

new_link="$app_root/.current-$$"
ln -s "$release_directory" "$new_link"
mv -Tf "$new_link" "$current_link"

echo "Restarting $service_name..."
service_healthy=false
if systemctl restart "$service_name"; then
    consecutive_healthy_checks=0
    for _ in {1..15}; do
        sleep 1
        active_state="$(systemctl show \
            "$service_name" --property=ActiveState --value)"
        sub_state="$(systemctl show \
            "$service_name" --property=SubState --value)"
        if [[ "$active_state" == "active" &&
              "$sub_state" == "running" ]]; then
            consecutive_healthy_checks=$((consecutive_healthy_checks + 1))
            if [[ "$consecutive_healthy_checks" -ge 5 ]]; then
                service_healthy=true
                break
            fi
        else
            consecutive_healthy_checks=0
        fi
    done
fi

if [[ "$service_healthy" != "true" ]]; then
    echo "The new release did not remain healthy. Restoring the previous release." >&2
    if [[ -n "$previous_target" && -d "$previous_target" ]]; then
        rollback_link="$app_root/.rollback-$$"
        ln -s "$previous_target" "$rollback_link"
        mv -Tf "$rollback_link" "$current_link"
        systemctl restart "$service_name" || true
    fi
    exit 1
fi

echo "Updated to $release_version ($release_commit)."

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
