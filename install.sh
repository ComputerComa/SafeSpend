#!/usr/bin/env bash
set -euo pipefail

if [[ "$EUID" -ne 0 ]]; then
    echo "Run this installer as root, for example:"
    echo "  curl -fsSL https://raw.githubusercontent.com/ComputerComa/SafeSpend/master/install.sh | sudo bash"
    exit 1
fi

repository="${SAFESPEND_REPOSITORY:-ComputerComa/SafeSpend}"
if [[ ! "$repository" =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]]; then
    echo "SAFESPEND_REPOSITORY must look like owner/repository." >&2
    exit 1
fi

temporary_directory="$(mktemp -d)"
trap 'rm -rf "$temporary_directory"' EXIT

download_base="https://github.com/$repository/releases/latest/download/SafeSpend-latest"
archive="$temporary_directory/SafeSpend-latest.zip"
checksum="$temporary_directory/SafeSpend-latest.zip.sha256"

echo "Downloading the latest SafeSpend release..."
curl --fail --location --silent --show-error \
    "$download_base.zip" \
    --output "$archive"
curl --fail --location --silent --show-error \
    "$download_base.zip.sha256" \
    --output "$checksum"

(cd "$temporary_directory" && sha256sum --check "$(basename "$checksum")")
unzip -tq "$archive" >/dev/null
unzip -q "$archive" -d "$temporary_directory/release"

if [[ ! -f "$temporary_directory/release/deploy/install-safespend.sh" ]]; then
    echo "The release did not contain the LXC installer." >&2
    exit 1
fi

SAFESPEND_REPOSITORY="$repository" \
    bash "$temporary_directory/release/deploy/install-safespend.sh"
