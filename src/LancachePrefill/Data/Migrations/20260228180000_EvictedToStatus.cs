using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LancachePrefill.Data.Migrations
{
    /// <inheritdoc />
    public partial class EvictedToStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Add status column with default 'active'
            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "selected_apps",
                type: "TEXT",
                nullable: false,
                defaultValue: "active");

            // Step 2: Migrate evicted=1 → status='evicted'
            migrationBuilder.Sql("UPDATE selected_apps SET status = 'evicted' WHERE evicted = 1");

            // Step 3: SQLite doesn't support DROP COLUMN before 3.35.0, so rebuild the table
            migrationBuilder.Sql(@"
                CREATE TABLE selected_apps_new (
                    app_id INTEGER NOT NULL CONSTRAINT PK_selected_apps PRIMARY KEY,
                    added_at TEXT NOT NULL DEFAULT (datetime('now')),
                    status TEXT NOT NULL DEFAULT 'active'
                );
                INSERT INTO selected_apps_new (app_id, added_at, status)
                    SELECT app_id, added_at, status FROM selected_apps;
                DROP TABLE selected_apps;
                ALTER TABLE selected_apps_new RENAME TO selected_apps;
            ");

            // Step 4: Add index on status
            migrationBuilder.CreateIndex(
                name: "IX_selected_apps_status",
                table: "selected_apps",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse: rebuild with evicted bool
            migrationBuilder.Sql(@"
                CREATE TABLE selected_apps_old (
                    app_id INTEGER NOT NULL CONSTRAINT PK_selected_apps PRIMARY KEY,
                    added_at TEXT NOT NULL DEFAULT (datetime('now')),
                    evicted INTEGER NOT NULL DEFAULT 0
                );
                INSERT INTO selected_apps_old (app_id, added_at, evicted)
                    SELECT app_id, added_at, CASE WHEN status = 'evicted' THEN 1 ELSE 0 END FROM selected_apps;
                DROP TABLE selected_apps;
                ALTER TABLE selected_apps_old RENAME TO selected_apps;
            ");
        }
    }
}