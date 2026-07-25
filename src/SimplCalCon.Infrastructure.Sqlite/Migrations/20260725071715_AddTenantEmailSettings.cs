using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimplCalCon.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantEmailSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantEmailSettings",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Host = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    UseStartTls = table.Column<bool>(type: "INTEGER", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    PasswordEncrypted = table.Column<string>(type: "TEXT", nullable: true),
                    FromAddress = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    FromName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantEmailSettings", x => x.TenantId);
                    table.ForeignKey(
                        name: "FK_TenantEmailSettings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantEmailSettings");
        }
    }
}
