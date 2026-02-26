using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LancachePrefill.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cache_files",
                columns: table => new
                {
                    file_hash = table.Column<string>(type: "TEXT", nullable: false),
                    depot_id = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cache_files", x => x.file_hash);
                });

            migrationBuilder.CreateTable(
                name: "depot_app_map",
                columns: table => new
                {
                    depot_id = table.Column<long>(type: "INTEGER", nullable: false),
                    app_id = table.Column<long>(type: "INTEGER", nullable: false),
                    app_name = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_depot_app_map", x => x.depot_id);
                });

            migrationBuilder.CreateTable(
                name: "downloaded_depots",
                columns: table => new
                {
                    depot_id = table.Column<long>(type: "INTEGER", nullable: false),
                    manifest_id = table.Column<long>(type: "INTEGER", nullable: false),
                    downloaded_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_downloaded_depots", x => new { x.depot_id, x.manifest_id });
                });

            migrationBuilder.CreateTable(
                name: "scan_results",
                columns: table => new
                {
                    app_id = table.Column<long>(type: "INTEGER", nullable: false),
                    app_name = table.Column<string>(type: "TEXT", nullable: false),
                    cached = table.Column<bool>(type: "INTEGER", nullable: false),
                    error = table.Column<string>(type: "TEXT", nullable: true),
                    scanned_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scan_results", x => x.app_id);
                });

            migrationBuilder.CreateTable(
                name: "selected_apps",
                columns: table => new
                {
                    app_id = table.Column<long>(type: "INTEGER", nullable: false),
                    added_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    evicted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_selected_apps", x => x.app_id);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settings", x => x.key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cache_files_depot_id",
                table: "cache_files",
                column: "depot_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cache_files");

            migrationBuilder.DropTable(
                name: "depot_app_map");

            migrationBuilder.DropTable(
                name: "downloaded_depots");

            migrationBuilder.DropTable(
                name: "scan_results");

            migrationBuilder.DropTable(
                name: "selected_apps");

            migrationBuilder.DropTable(
                name: "settings");
        }
    }
}
