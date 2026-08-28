#!/bin/bash
# Database backup script for FMS
# Usage: ./scripts/backup-database.sh [server] [database]
#
# Requires sqlcmd (SQL Server command-line tool)

SERVER="${1:-localhost}"
DATABASE="${2:-FMS}"
BACKUP_DIR="./backups"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
BACKUP_FILE="${BACKUP_DIR}/${DATABASE}_backup_${TIMESTAMP}.bak"

mkdir -p "$BACKUP_DIR"

echo "Backing up ${DATABASE} on ${SERVER}..."
echo "Output: ${BACKUP_FILE}"

sqlcmd -S "$SERVER" -Q "
BACKUP DATABASE [${DATABASE}]
TO DISK = N'${BACKUP_FILE}'
WITH FORMAT, COMPRESSION,
INIT,
NAME = N'${DATABASE} - Full Backup',
DESCRIPTION = N'Automated backup at ${TIMESTAMP}'
"

if [ $? -eq 0 ]; then
    echo "Backup completed successfully: ${BACKUP_FILE}"
    echo "Size: $(du -h "$BACKUP_FILE" | cut -f1)"
else
    echo "ERROR: Backup failed!"
    exit 1
fi
