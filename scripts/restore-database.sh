#!/bin/bash
# Database restore script for FMS
# Usage: ./scripts/restore-database.sh <backup-file> [server] [database]
#
# WARNING: This will overwrite the existing database!

BACKUP_FILE="$1"
SERVER="${2:-localhost}"
DATABASE="${3:-FMS}"

if [ -z "$BACKUP_FILE" ]; then
    echo "Usage: $0 <backup-file> [server] [database]"
    exit 1
fi

if [ ! -f "$BACKUP_FILE" ]; then
    echo "ERROR: Backup file not found: $BACKUP_FILE"
    exit 1
fi

echo "WARNING: This will overwrite database [${DATABASE}] on ${SERVER}"
echo "Backup file: ${BACKUP_FILE}"
read -p "Continue? (yes/no): " CONFIRM
if [ "$CONFIRM" != "yes" ]; then
    echo "Cancelled."
    exit 0
fi

echo "Setting single-user mode..."
sqlcmd -S "$SERVER" -Q "ALTER DATABASE [${DATABASE}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE"

echo "Restoring..."
sqlcmd -S "$SERVER" -Q "
RESTORE DATABASE [${DATABASE}]
FROM DISK = N'${BACKUP_FILE}'
WITH REPLACE,
RECOVERY
"

echo "Setting multi-user mode..."
sqlcmd -S "$SERVER" -Q "ALTER DATABASE [${DATABASE}] SET MULTI_USER"

echo "Restore completed successfully."
