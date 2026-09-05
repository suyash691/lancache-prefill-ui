using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LancachePrefill.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPrefillRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "prefill_runs",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    started_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    finished_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    run_trigger = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    apps_cached = table.Column<int>(type: "INTEGER", nullable: false),
                    apps_partial = table.Column<int>(type: "INTEGER", nullable: false),
                    apps_skipped = table.Column<int>(type: "INTEGER", nullable: false),
                    apps_failed = table.Column<int>(type: "INTEGER", nullable: false),
                    bytes = table.Column<long>(type: "INTEGER", nullable: false),
                    results_json = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prefill_runs", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "prefill_runs");
        }
    }
}
