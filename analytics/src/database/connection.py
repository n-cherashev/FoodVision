"""Database connection: PostgreSQL (primary) with fallback to SQLite."""

import json
import logging
import os
import sqlite3
from pathlib import Path

logger = logging.getLogger(__name__)

PROJECT_ROOT = Path(__file__).resolve().parent.parent.parent
SQLITE_PATH = PROJECT_ROOT / "data" / "restaurant.db"

try:
    import psycopg
    HAS_PSYCOPG = True
except ImportError:
    HAS_PSYCOPG = False


def get_db_url() -> str | None:
    from config import settings
    return os.environ.get("DATABASE_URL", getattr(settings, "DATABASE_URL", None))


class DbCursorContext:
    def __init__(self, conn, is_pg: bool):
        self.conn = conn
        self.is_pg = is_pg
        self.cursor = None

    def __enter__(self):
        self.cursor = self.conn.cursor()
        return self

    def __exit__(self, exc_type, exc_val, exc_tb):
        try:
            if exc_type is not None:
                self.conn.rollback()
            else:
                self.conn.commit()
        finally:
            if self.cursor is not None:
                try:
                    self.cursor.close()
                except Exception:
                    pass
            try:
                self.conn.close()
            except Exception:
                pass

    def execute(self, query: str, params=()):
        if not self.is_pg:
            # Convert %s to ? for SQLite
            query = query.replace("%s", "?")
            query = query.replace("::jsonb", "")
            query = query.replace("gen_random_uuid()", "lower(hex(randomblob(16)))")
            query = query.replace("NOW()", "CURRENT_TIMESTAMP")
        return self.cursor.execute(query, params)

    def fetchall(self):
        return self.cursor.fetchall()

    def fetchone(self):
        return self.cursor.fetchone()


def get_connection():
    """Returns a context manager wrapping a connection and active cursor."""
    url = get_db_url()
    if HAS_PSYCOPG and url and ("postgres" in url):
        try:
            conn = psycopg.connect(url, autocommit=False)
            return DbCursorContext(conn, is_pg=True)
        except Exception as e:
            logger.warning("Could not connect to PostgreSQL (%s), using SQLite.", e)

    SQLITE_PATH.parent.mkdir(parents=True, exist_ok=True)
    conn = sqlite3.connect(SQLITE_PATH)
    conn.execute("PRAGMA foreign_keys = ON")
    return DbCursorContext(conn, is_pg=False)


def init_database() -> None:
    """Ensure basic tables exist for standalone or fallback operation."""
    with get_connection() as db:
        if not db.is_pg:
            db.execute("""
            CREATE TABLE IF NOT EXISTS tables (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                capacity INTEGER NOT NULL
            );
            """)
            db.execute("""
            CREATE TABLE IF NOT EXISTS occupancy_sessions (
                id INTEGER PRIMARY KEY,
                table_id INTEGER NOT NULL REFERENCES tables (id),
                started_at REAL NOT NULL,
                ended_at REAL,
                max_people INTEGER NOT NULL
            );
            """)
            db.execute("""
            CREATE TABLE IF NOT EXISTS table_zones (
                id TEXT PRIMARY KEY,
                restaurant_id TEXT NOT NULL DEFAULT 'default',
                name TEXT NOT NULL,
                capacity INTEGER NOT NULL DEFAULT 4,
                geometry TEXT NOT NULL,
                is_active INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """)
        else:
            db.execute("""
            CREATE SCHEMA IF NOT EXISTS tables;
            """)
            db.execute("""
            CREATE TABLE IF NOT EXISTS tables.occupancy_sessions (
                id SERIAL PRIMARY KEY,
                table_id VARCHAR(100) NOT NULL,
                started_at REAL NOT NULL,
                ended_at REAL,
                max_people INTEGER NOT NULL
            );
            """)
