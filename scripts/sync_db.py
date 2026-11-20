import sys
import os
import sqlite3
import shutil
import datetime

def sync_database(source_path=None):
    if not source_path:
        candidates = [
            r"c:\MYGTS\MYGTS_website\Data\import.db",
            r"C:\Users\naris\Downloads\mygts_database.db"
        ]
        for c in candidates:
            if os.path.exists(c):
                source_path = c
                break

    if not source_path or not os.path.exists(source_path):
        print("Error: Source database file not found.")
        print("Checked: c:\\MYGTS\\MYGTS_website\\Data\\import.db and C:\\Users\\naris\\Downloads\\mygts_database.db")
        return False

    live_db = r"C:\ProgramData\MyGts_database\mygts_database.db"
    repo_db = r"c:\MYGTS\MYGTS_website\Data\mygts_database.db"

    print("=== MYGTS Database Safe Sync ===")
    print(f"Source file: {source_path} ({os.path.getsize(source_path):,} bytes)")

    # 1. Integrity check on incoming file
    try:
        test_conn = sqlite3.connect(source_path)
        check = test_conn.execute("PRAGMA integrity_check").fetchone()[0]
        test_conn.close()
        if check != "ok":
            print(f"Error: Incoming database failed integrity check: {check}")
            return False
        print("1. Integrity check on incoming DB passed: OK")
    except Exception as e:
        print(f"Error reading incoming DB: {e}")
        return False

    # 2. Safety backups of current live & repo DBs
    ts = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
    backup_live = f"C:\\ProgramData\\MyGts_database\\mygts_database.db.backup_{ts}"
    backup_repo = f"c:\\MYGTS\\MYGTS_website\\Data\\mygts_database.db.backup_{ts}"

    if os.path.exists(live_db):
        shutil.copy2(live_db, backup_live)
        print(f"2. Created backup of live DB -> {backup_live}")
    if os.path.exists(repo_db):
        shutil.copy2(repo_db, backup_repo)
        print(f"   Created backup of repo DB -> {backup_repo}")

    # 3. Create staging copy to perform schema migrations
    staging_db = r"C:\ProgramData\MyGts_database\mygts_sync_staging.db"
    shutil.copy2(source_path, staging_db)

    conn_stage = sqlite3.connect(staging_db)
    cur = conn_stage.cursor()

    print("3. Applying compatibility schema updates to incoming data...")
    migrations = [
        ("Devices", "IsMuted", "INTEGER NOT NULL DEFAULT 0"),
        ("Devices", "AdminCost", "NUMERIC NOT NULL DEFAULT 0"),
        ("Devices", "Paid", "TEXT DEFAULT ''"),
        ("Devices", "DeviceSerialNo", "TEXT DEFAULT ''"),
        ("Transactions", "AdminCost", "NUMERIC NOT NULL DEFAULT 0"),
        ("Transactions", "Imei", "TEXT DEFAULT ''"),
        ("Users", "PermissionsJson", "TEXT NOT NULL DEFAULT '{}'"),
        ("Users", "WhatsAppPreference", "TEXT NOT NULL DEFAULT 'Ask'"),
        ("Vendors", "BatchPrefix", "TEXT NOT NULL DEFAULT ''"),
        ("Vendors", "ExtraPhones", "TEXT NOT NULL DEFAULT ''"),
        ("Vendors", "WhatsAppNumber", "TEXT NOT NULL DEFAULT ''"),
        ("Vendors", "WhatsAppSameAsPhone", "INTEGER NOT NULL DEFAULT 1"),
    ]

    for tbl, col, col_def in migrations:
        try:
            cur.execute(f"ALTER TABLE {tbl} ADD COLUMN {col} {col_def}")
        except sqlite3.OperationalError:
            pass

    cur.execute("""
        CREATE TABLE IF NOT EXISTS CustomDropdownOptions (
            Id TEXT PRIMARY KEY,
            Category TEXT NOT NULL,
            Value TEXT NOT NULL,
            Branch TEXT NOT NULL DEFAULT 'DMG',
            CreatedAt TEXT DEFAULT ''
        );
    """)
    cur.execute("""
        CREATE TABLE IF NOT EXISTS DataShareRecords (
            Id TEXT PRIMARY KEY,
            Branch TEXT NOT NULL DEFAULT 'DMG',
            VehicleNo TEXT DEFAULT '',
            Imei TEXT DEFAULT '',
            Iccid TEXT DEFAULT '',
            DeviceName TEXT DEFAULT '',
            DeviceBranch TEXT DEFAULT '',
            SharedBy TEXT DEFAULT '',
            SharedByEmail TEXT DEFAULT '',
            SharedByRole TEXT DEFAULT '',
            SharedAt TEXT DEFAULT '',
            Recipient TEXT DEFAULT '',
            Notes TEXT DEFAULT ''
        );
    """)

    conn_stage.commit()
    conn_stage.close()

    # 4. Safely apply to live DB and repo DB using SQLite native backup API
    print("4. Applying synchronized database using SQLite native engine...")
    stage_conn = sqlite3.connect(staging_db)
    
    # Update live DB
    live_conn = sqlite3.connect(live_db)
    stage_conn.backup(live_conn)
    live_conn.commit()
    live_conn.execute("PRAGMA wal_checkpoint(TRUNCATE)")
    live_conn.close()

    # Update repo DB
    repo_conn = sqlite3.connect(repo_db)
    stage_conn.backup(repo_conn)
    repo_conn.commit()
    repo_conn.execute("PRAGMA wal_checkpoint(TRUNCATE)")
    repo_conn.close()

    stage_conn.close()

    if os.path.exists(staging_db):
        os.remove(staging_db)

    # 5. Display final summary
    final_conn = sqlite3.connect(live_db)
    print("\n=== Live Database Successfully Updated ===")
    tables = ['Devices', 'Transactions', 'Recharges', 'Vendors', 'Users', 'Invoices', 'ManualReminders', 'CustomDropdownOptions', 'DataShareRecords']
    for t in tables:
        try:
            cnt = final_conn.execute(f'SELECT COUNT(*) FROM "{t}"').fetchone()[0]
            print(f"  {t:25}: {cnt:6} records")
        except Exception:
            pass
    final_conn.close()

    print("\nAll changes live. Site is fully updated!")
    return True

if __name__ == '__main__':
    arg = sys.argv[1] if len(sys.argv) > 1 else None
    sync_database(arg)
