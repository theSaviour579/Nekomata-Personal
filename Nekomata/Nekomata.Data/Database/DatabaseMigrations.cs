namespace Nekomata.Data.Database;

internal sealed record DatabaseMigration(int Version, string Description, string Sql);

internal static class DatabaseMigrations
{
    internal static IReadOnlyList<DatabaseMigration> All { get; } =
    [
        new(1, "Create core workspace tables", """
            CREATE TABLE IF NOT EXISTS assistant.settings
            (
                key text PRIMARY KEY,
                value text,
                updated_at timestamp with time zone NOT NULL DEFAULT now()
            );

            CREATE TABLE IF NOT EXISTS assistant.people
            (
                id bigserial PRIMARY KEY,
                name text NOT NULL,
                role text,
                active boolean NOT NULL DEFAULT true
            );

            CREATE TABLE IF NOT EXISTS assistant.skills
            (
                id bigserial PRIMARY KEY,
                name text NOT NULL UNIQUE
            );

            CREATE TABLE IF NOT EXISTS assistant.person_skills
            (
                person_id bigint REFERENCES assistant.people(id) ON DELETE CASCADE,
                skill_id bigint REFERENCES assistant.skills(id) ON DELETE CASCADE,
                PRIMARY KEY(person_id, skill_id)
            );

            CREATE TABLE IF NOT EXISTS assistant.projects
            (
                id bigserial PRIMARY KEY,
                name text NOT NULL,
                description text,
                status text NOT NULL DEFAULT 'Active',
                priority text NOT NULL DEFAULT 'Normal',
                progress_percent integer NOT NULL DEFAULT 0,
                estimated_remaining_minutes integer NOT NULL DEFAULT 0,
                due_at timestamp,
                at_risk boolean NOT NULL DEFAULT false,
                next_action text,
                estimated_business_value numeric(18,2) NOT NULL DEFAULT 0,
                created_at timestamp with time zone NOT NULL DEFAULT now(),
                updated_at timestamp with time zone NOT NULL DEFAULT now()
            );

            CREATE TABLE IF NOT EXISTS assistant.tasks
            (
                id bigserial PRIMARY KEY,
                project_id bigint REFERENCES assistant.projects(id) ON DELETE SET NULL,
                title text NOT NULL,
                description text,
                source text NOT NULL DEFAULT 'Manual',
                status text NOT NULL DEFAULT 'Open',
                priority text NOT NULL DEFAULT 'Normal',
                owner text,
                suggested_delegate text,
                business_critical boolean NOT NULL DEFAULT false,
                accuracy_sensitive boolean NOT NULL DEFAULT false,
                estimated_minutes integer NOT NULL DEFAULT 30,
                actual_minutes integer NOT NULL DEFAULT 0,
                due_at timestamp,
                completed_at timestamp,
                priority_score integer NOT NULL DEFAULT 0,
                estimated_business_value numeric(18,2) NOT NULL DEFAULT 0,
                revenue_impact integer NOT NULL DEFAULT 0,
                customer_impact integer NOT NULL DEFAULT 0,
                executive_visibility integer NOT NULL DEFAULT 0,
                automation_potential integer NOT NULL DEFAULT 0,
                requires_sql boolean NOT NULL DEFAULT false,
                requires_halo boolean NOT NULL DEFAULT false,
                requires_outlook boolean NOT NULL DEFAULT false,
                requires_focus boolean NOT NULL DEFAULT false,
                interruptible boolean NOT NULL DEFAULT false,
                recurring boolean NOT NULL DEFAULT false,
                category text NOT NULL DEFAULT '',
                tags text NOT NULL DEFAULT '',
                created_at timestamp with time zone NOT NULL DEFAULT now(),
                updated_at timestamp with time zone NOT NULL DEFAULT now()
            );

            CREATE TABLE IF NOT EXISTS assistant.audit_log
            (
                id bigserial PRIMARY KEY,
                event_type text,
                message text,
                created_at timestamp with time zone NOT NULL DEFAULT now()
            );
            """),
        new(2, "Upgrade legacy task and project tables", """
            ALTER TABLE assistant.projects ADD COLUMN IF NOT EXISTS estimated_business_value numeric(18,2) NOT NULL DEFAULT 0;
            ALTER TABLE assistant.tasks ADD COLUMN IF NOT EXISTS project_id bigint REFERENCES assistant.projects(id) ON DELETE SET NULL;
            ALTER TABLE assistant.tasks ADD COLUMN IF NOT EXISTS actual_minutes integer NOT NULL DEFAULT 0;
            ALTER TABLE assistant.tasks ADD COLUMN IF NOT EXISTS completed_at timestamp;
            ALTER TABLE assistant.tasks ADD COLUMN IF NOT EXISTS estimated_business_value numeric(18,2) NOT NULL DEFAULT 0;
            ALTER TABLE assistant.tasks ADD COLUMN IF NOT EXISTS revenue_impact integer NOT NULL DEFAULT 0;
            ALTER TABLE assistant.tasks ADD COLUMN IF NOT EXISTS customer_impact integer NOT NULL DEFAULT 0;
            ALTER TABLE assistant.tasks ADD COLUMN IF NOT EXISTS executive_visibility integer NOT NULL DEFAULT 0;
            ALTER TABLE assistant.tasks ADD COLUMN IF NOT EXISTS automation_potential integer NOT NULL DEFAULT 0;
            ALTER TABLE assistant.tasks ADD COLUMN IF NOT EXISTS requires_sql boolean NOT NULL DEFAULT false;
            ALTER TABLE assistant.tasks ADD COLUMN IF NOT EXISTS requires_halo boolean NOT NULL DEFAULT false;
            ALTER TABLE assistant.tasks ADD COLUMN IF NOT EXISTS requires_outlook boolean NOT NULL DEFAULT false;
            ALTER TABLE assistant.tasks ADD COLUMN IF NOT EXISTS requires_focus boolean NOT NULL DEFAULT false;
            ALTER TABLE assistant.tasks ADD COLUMN IF NOT EXISTS interruptible boolean NOT NULL DEFAULT false;
            ALTER TABLE assistant.tasks ADD COLUMN IF NOT EXISTS recurring boolean NOT NULL DEFAULT false;
            ALTER TABLE assistant.tasks ADD COLUMN IF NOT EXISTS category text NOT NULL DEFAULT '';
            ALTER TABLE assistant.tasks ADD COLUMN IF NOT EXISTS tags text NOT NULL DEFAULT '';
            """),
        new(3, "Create Guardian memory and mission history", """
            CREATE TABLE IF NOT EXISTS assistant.guardian_memory
            (
                id bigserial PRIMARY KEY,
                created_at timestamp with time zone NOT NULL DEFAULT now(),
                category text NOT NULL,
                importance integer NOT NULL DEFAULT 50,
                source text NOT NULL,
                summary text NOT NULL,
                detail text,
                project_id bigint REFERENCES assistant.projects(id) ON DELETE SET NULL,
                task_id bigint REFERENCES assistant.tasks(id) ON DELETE SET NULL,
                metadata jsonb
            );

            CREATE TABLE IF NOT EXISTS assistant.mission_sessions
            (
                id bigserial PRIMARY KEY,
                task_id bigint REFERENCES assistant.tasks(id) ON DELETE SET NULL,
                project_id bigint REFERENCES assistant.projects(id) ON DELETE SET NULL,
                title text NOT NULL,
                source_type text NOT NULL,
                score integer NOT NULL DEFAULT 0,
                business_value numeric(18,2) NOT NULL DEFAULT 0,
                estimated_duration_minutes integer NOT NULL DEFAULT 0,
                actual_duration_minutes integer NOT NULL DEFAULT 0,
                started_at timestamp NOT NULL,
                finished_at timestamp NOT NULL,
                completed boolean NOT NULL DEFAULT false,
                cancelled boolean NOT NULL DEFAULT false,
                guardian_decision text NOT NULL DEFAULT '',
                recommendation_reason text NOT NULL DEFAULT ''
            );

            CREATE INDEX IF NOT EXISTS ix_tasks_status_due_at ON assistant.tasks(status, due_at);
            CREATE INDEX IF NOT EXISTS ix_guardian_memory_created_at ON assistant.guardian_memory(created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_mission_sessions_finished_at ON assistant.mission_sessions(finished_at DESC);
            """)
    ];
}
