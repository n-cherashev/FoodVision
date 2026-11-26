"""Database operations for tables, occupancy sessions, and live state."""

import json
import logging
from config import PROJECT_ROOT, REGIONS_CONFIG_PATH
from counting.load_regions import load_zones
from database.connection import get_connection

logger = logging.getLogger(__name__)


def load_active_zones() -> list[dict]:
    """Load active zones from PostgreSQL table_zones with fallback to regions.json."""
    try:
        with get_connection() as db:
            table_name = "tables.table_zones" if db.is_pg else "table_zones"
            condition = "is_active = true" if db.is_pg else "is_active = 1"
            db.execute(f"SELECT id, name, capacity, geometry FROM {table_name} WHERE {condition} ORDER BY id")
            rows = db.fetchall()
            if rows:
                zones = []
                for r in rows:
                    geo = r[3]
                    if isinstance(geo, str):
                        try:
                            geo = json.loads(geo)
                        except Exception:
                            geo = {}
                    zones.append({
                        "name": r[1] or r[0],
                        "max": int(r[2]),
                        "counting_polygon": geo.get("counting_polygon", []),
                        "display_polygon": geo.get("display_polygon", []),
                    })
                if zones:
                    return zones
    except Exception as e:
        logger.warning("Could not load zones from DB (%s); using file %s", e, REGIONS_CONFIG_PATH)

    return load_zones(REGIONS_CONFIG_PATH)


def save_active_zones(zones: list[dict], restaurant_id: str = "default") -> None:
    """Save/update table zones in the database."""
    with get_connection() as db:
        table_name = "tables.table_zones" if db.is_pg else "table_zones"
        for z in zones:
            name = z.get("name") or z.get("id", "T-01")
            zid = z.get("id") or name
            cap = int(z.get("max") or z.get("capacity", 4))
            geo_dict = {
                "display_polygon": z.get("display_polygon", []),
                "counting_polygon": z.get("counting_polygon", []),
            }
            geo_json = json.dumps(geo_dict)

            if db.is_pg:
                db.execute(
                    f"""
                    INSERT INTO {table_name} (id, restaurant_id, name, capacity, geometry, is_active, created_at, updated_at)
                    VALUES (%s, %s, %s, %s, %s::jsonb, true, NOW(), NOW())
                    ON CONFLICT (id) DO UPDATE SET
                        name = EXCLUDED.name,
                        capacity = EXCLUDED.capacity,
                        geometry = EXCLUDED.geometry,
                        is_active = true,
                        updated_at = NOW()
                    """,
                    (zid, restaurant_id, name, cap, geo_json),
                )
            else:
                db.execute(
                    f"""
                    INSERT INTO {table_name} (id, restaurant_id, name, capacity, geometry, is_active, created_at, updated_at)
                    VALUES (%s, %s, %s, %s, %s, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
                    ON CONFLICT (id) DO UPDATE SET
                        name = excluded.name,
                        capacity = excluded.capacity,
                        geometry = excluded.geometry,
                        is_active = 1,
                        updated_at = CURRENT_TIMESTAMP
                    """,
                    (zid, restaurant_id, name, cap, geo_json),
                )


def sync_tables(tables: list[tuple[str, int]]) -> None:
    """Ensure table definitions exist."""
    try:
        with get_connection() as db:
            if not db.is_pg:
                for name, capacity in tables:
                    db.execute(
                        """
                        INSERT INTO tables (name, capacity) VALUES (%s, %s)
                        ON CONFLICT (name) DO UPDATE SET capacity = excluded.capacity
                        """,
                        (name, capacity),
                    )
    except Exception as e:
        logger.debug("sync_tables note: %s", e)


def sync_table_state(
    session_id: str,
    table_id: str,
    people_count: int,
    is_occupied: bool,
    video_time: float,
    restaurant_id: str = "default",
) -> None:
    """Update live state in tables.table_session_states and log transition events."""
    try:
        with get_connection() as db:
            if not db.is_pg:
                return  # State sync is primary for PostgreSQL

            # Check previous state to record transition events
            db.execute(
                """
                SELECT people_count, is_occupied FROM tables.table_session_states
                WHERE session_id = %s AND table_id = %s
                """,
                (session_id, table_id),
            )
            row = db.fetchone()
            prev_count = row[0] if row else 0
            prev_occ = row[1] if row else False

            has_people = people_count > 0

            # Upsert state
            db.execute(
                """
                INSERT INTO tables.table_session_states (
                    session_id, table_id, restaurant_id, has_people, people_count,
                    has_belongings, has_open_orders, is_occupied, cleaning_status,
                    video_freshness, orders_freshness, last_video_position_seconds,
                    last_video_observation_time, was_used_in_session, version, updated_at
                ) VALUES (
                    %s, %s, %s, %s, %s,
                    false, false, %s, 'Unknown',
                    'Fresh', 'NoData', %s,
                    NOW(), %s, 1, NOW()
                )
                ON CONFLICT (session_id, table_id) DO UPDATE SET
                    has_people = EXCLUDED.has_people,
                    people_count = EXCLUDED.people_count,
                    is_occupied = EXCLUDED.is_occupied,
                    last_video_position_seconds = EXCLUDED.last_video_position_seconds,
                    last_video_observation_time = NOW(),
                    was_used_in_session = tables.table_session_states.was_used_in_session OR EXCLUDED.was_used_in_session,
                    updated_at = NOW(),
                    version = tables.table_session_states.version + 1
                """,
                (session_id, table_id, restaurant_id, has_people, people_count, is_occupied, video_time, has_people),
            )

            # Insert transition events if changed
            if row is not None:
                if not prev_occ and is_occupied:
                    db.execute(
                        """
                        INSERT INTO tables.table_events (
                            id, session_id, table_id, event_type, source, video_position_seconds,
                            timestamp, description, version, created_at
                        ) VALUES (
                            gen_random_uuid(), %s, %s, 'OccupancyChanged', 'VideoAnalytics', %s,
                            NOW(), 'Стол занят (зафиксированы гости через видеоаналитику)', 1, NOW()
                        )
                        """,
                        (session_id, table_id, video_time),
                    )
                elif prev_occ and not is_occupied:
                    db.execute(
                        """
                        INSERT INTO tables.table_events (
                            id, session_id, table_id, event_type, source, video_position_seconds,
                            timestamp, description, version, created_at
                        ) VALUES (
                            gen_random_uuid(), %s, %s, 'OccupancyChanged', 'VideoAnalytics', %s,
                            NOW(), 'Стол освободился', 1, NOW()
                        )
                        """,
                        (session_id, table_id, video_time),
                    )

                if prev_count == 0 and people_count > 0:
                    db.execute(
                        """
                        INSERT INTO tables.table_events (
                            id, session_id, table_id, event_type, source, video_position_seconds,
                            timestamp, description, version, created_at
                        ) VALUES (
                            gen_random_uuid(), %s, %s, 'PeopleArrived', 'VideoAnalytics', %s,
                            NOW(), %s, 1, NOW()
                        )
                        """,
                        (session_id, table_id, video_time, f"Гости сели за стол (человек: {people_count})"),
                    )
                elif prev_count > 0 and people_count == 0:
                    db.execute(
                        """
                        INSERT INTO tables.table_events (
                            id, session_id, table_id, event_type, source, video_position_seconds,
                            timestamp, description, version, created_at
                        ) VALUES (
                            gen_random_uuid(), %s, %s, 'PeopleDeparted', 'VideoAnalytics', %s,
                            NOW(), 'Гости покинули стол', 1, NOW()
                        )
                        """,
                        (session_id, table_id, video_time),
                    )
    except Exception as e:
        logger.error("Failed to sync table state to PostgreSQL: %s", e)


def _close_session(db, name: str, ended_at: float) -> None:
    try:
        if db.is_pg:
            db.execute(
                """
                UPDATE tables.occupancy_sessions
                SET ended_at = GREATEST(started_at, %s)
                WHERE ended_at IS NULL AND table_id = %s
                """,
                (ended_at, name),
            )
        else:
            db.execute(
                """
                UPDATE occupancy_sessions
                SET ended_at = MAX(started_at, ?)
                WHERE ended_at IS NULL
                  AND table_id = (SELECT id FROM tables WHERE name = ?)
                """,
                (ended_at, name),
            )
    except Exception as e:
        logger.debug("_close_session note: %s", e)


def _close_all(db, ended_at: float) -> None:
    try:
        if db.is_pg:
            db.execute(
                """
                UPDATE tables.occupancy_sessions
                SET ended_at = GREATEST(started_at, %s)
                WHERE ended_at IS NULL
                """,
                (ended_at,),
            )
        else:
            db.execute(
                """
                UPDATE occupancy_sessions
                SET ended_at = MAX(started_at, ?)
                WHERE ended_at IS NULL
                """,
                (ended_at,),
            )
    except Exception as e:
        logger.debug("_close_all note: %s", e)


def start_session(name: str, started_at: float, max_people: int) -> None:
    with get_connection() as db:
        _close_session(db, name, started_at)
        if db.is_pg:
            db.execute(
                """
                INSERT INTO tables.occupancy_sessions (table_id, started_at, max_people)
                VALUES (%s, %s, %s)
                """,
                (name, started_at, max_people),
            )
        else:
            db.execute(
                """
                INSERT INTO occupancy_sessions (table_id, started_at, max_people)
                SELECT id, ?, ? FROM tables WHERE name = ?
                """,
                (started_at, max_people, name),
            )


def update_session_max(name: str, max_people: int) -> None:
    with get_connection() as db:
        if db.is_pg:
            db.execute(
                """
                UPDATE tables.occupancy_sessions
                SET max_people = %s
                WHERE ended_at IS NULL AND max_people < %s AND table_id = %s
                """,
                (max_people, max_people, name),
            )
        else:
            db.execute(
                """
                UPDATE occupancy_sessions
                SET max_people = ?
                WHERE ended_at IS NULL
                  AND max_people < ?
                  AND table_id = (SELECT id FROM tables WHERE name = ?)
                """,
                (max_people, max_people, name),
            )


def close_session(name: str, ended_at: float) -> None:
    with get_connection() as db:
        _close_session(db, name, ended_at)


def close_all_sessions(ended_at: float) -> None:
    with get_connection() as db:
        _close_all(db, ended_at)


def close_leftover_sessions() -> None:
    with get_connection() as db:
        if db.is_pg:
            db.execute(
                """
                UPDATE tables.occupancy_sessions
                SET ended_at = started_at
                WHERE ended_at IS NULL
                """
            )
        else:
            db.execute(
                """
                UPDATE occupancy_sessions
                SET ended_at = started_at
                WHERE ended_at IS NULL
                """
            )
