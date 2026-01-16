#!/bin/bash
# set -e  # Remove this to prevent exit on psql error

MIGRATION_ID=$1
DB_NAME=$2
EF_PRODUCT_VERSION="9.0.1"

echo "Checking migration state for $DB_NAME..."
export PGPASSWORD=$PASSWORD

# Check if database exists first by connecting to 'postgres'
DB_EXISTS=$(psql -h $HOST -p $PORT -U $USER -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$DB_NAME';")

if [ "$DB_EXISTS" != "1" ]; then
    echo "Database $DB_NAME does not exist. Migration bundle will create it."
    exit 0
fi

# Check if migration exists in the target database
EXISTS=$(psql -h $HOST -p $PORT -U $USER -d $DB_NAME -tAc "SELECT 1 FROM information_schema.tables WHERE table_name = '__EFMigrationsHistory';")

if [ "$EXISTS" == "1" ]; then
    ALREADY_APPLIED=$(psql -h $HOST -p $PORT -U $USER -d $DB_NAME -tAc "SELECT 1 FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '$MIGRATION_ID';")
    if [ "$ALREADY_APPLIED" == "1" ]; then
        echo "Migration $MIGRATION_ID already applied to $DB_NAME. Skipping reset."
        exit 0
    fi
fi

echo "Migration $MIGRATION_ID not found in $DB_NAME. Resetting history..."
# Ensure the table exists or ignore error on truncate
psql -h $HOST -p $PORT -U $USER -d $DB_NAME -c "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (\"MigrationId\" varchar(150) NOT NULL, \"ProductVersion\" varchar(32) NOT NULL, CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY (\"MigrationId\"));"
psql -h $HOST -p $PORT -U $USER -d $DB_NAME -c "TRUNCATE TABLE \"__EFMigrationsHistory\";"
psql -h $HOST -p $PORT -U $USER -d $DB_NAME -c "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('$MIGRATION_ID', '$EF_PRODUCT_VERSION');"
echo "History reset for $DB_NAME."
