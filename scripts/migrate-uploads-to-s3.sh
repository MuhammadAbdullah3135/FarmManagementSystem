#!/bin/bash
# Copies files from the local uploads folder into S3-compatible object storage.
#
#   Usage: ./scripts/migrate-uploads-to-s3.sh [uploads-root]
#   Default uploads-root: src/FMS.API/uploads
#
# READ THIS BEFORE RUNNING — the honest caveat:
#
#   File storage previously wrote to the API host's own disk. On Heroku that disk is
#   EPHEMERAL: every release and every dyno restart wipes it. If this app has been
#   deployed, any file uploaded in production is very likely ALREADY GONE and this
#   script cannot get it back. It is only useful for files that still exist — local
#   development folders and any docker-compose volume.
#
# Why a plain copy is correct:
#
#   Stored objects are keyed by the relative path already used on disk
#   (farms/{farmId}/animals/{animalId}/images/{guid}.ext), and the database stores that
#   same string in AnimalImages.StoragePath / AnimalDocuments.StoragePath. Copying the
#   folder tree verbatim therefore reproduces exactly the keys the API will look up —
#   no database changes are needed.
#
# Requires either rclone or the AWS CLI, plus the Storage__S3__* values from .env.

set -euo pipefail

UPLOADS_ROOT="${1:-src/FMS.API/uploads}"

: "${Storage__S3__Bucket:?set Storage__S3__Bucket}"
: "${Storage__S3__AccessKeyId:?set Storage__S3__AccessKeyId}"
: "${Storage__S3__SecretAccessKey:?set Storage__S3__SecretAccessKey}"
SERVICE_URL="${Storage__S3__ServiceUrl:-}"
REGION="${Storage__S3__Region:-auto}"

if [ ! -d "$UPLOADS_ROOT" ]; then
    echo "Nothing to migrate: '$UPLOADS_ROOT' does not exist."
    echo "That is expected on a hosted environment — its disk is ephemeral."
    exit 0
fi

FILE_COUNT=$(find "$UPLOADS_ROOT" -type f | wc -l | tr -d ' ')
echo "Found ${FILE_COUNT} file(s) under ${UPLOADS_ROOT}"

if [ "$FILE_COUNT" -eq 0 ]; then
    echo "Nothing to migrate."
    exit 0
fi

# The uploads root on disk IS the object-key prefix, so syncing '<uploads-root>/' to
# '<bucket>/' maps every file to exactly the key stored in the database.
if command -v rclone >/dev/null 2>&1; then
    echo "Using rclone..."
    export RCLONE_CONFIG_FMS_TYPE=s3
    export RCLONE_CONFIG_FMS_PROVIDER=Other
    export RCLONE_CONFIG_FMS_ACCESS_KEY_ID="$Storage__S3__AccessKeyId"
    export RCLONE_CONFIG_FMS_SECRET_ACCESS_KEY="$Storage__S3__SecretAccessKey"
    export RCLONE_CONFIG_FMS_REGION="$REGION"
    if [ -n "$SERVICE_URL" ]; then
        export RCLONE_CONFIG_FMS_ENDPOINT="$SERVICE_URL"
    fi
    rclone sync "$UPLOADS_ROOT" "FMS:${Storage__S3__Bucket}" --progress
elif command -v aws >/dev/null 2>&1; then
    echo "Using the AWS CLI..."
    AWS_ACCESS_KEY_ID="$Storage__S3__AccessKeyId" \
    AWS_SECRET_ACCESS_KEY="$Storage__S3__SecretAccessKey" \
    AWS_DEFAULT_REGION="$REGION" \
        aws ${SERVICE_URL:+--endpoint-url "$SERVICE_URL"} \
        s3 sync "$UPLOADS_ROOT" "s3://${Storage__S3__Bucket}"
else
    echo "ERROR: neither rclone nor the AWS CLI is installed." >&2
    echo "Install one of them, then re-run this script." >&2
    exit 1
fi

echo
echo "Done. Verify by opening an animal document download link in the app."
echo "Remember to set Storage__Provider=S3 for the API before relying on object storage."
