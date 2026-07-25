using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimplCalCon.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddInboundImapSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImapFolder",
                table: "TenantEmailSettings",
                type: "TEXT",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImapHost",
                table: "TenantEmailSettings",
                type: "TEXT",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImapPasswordEncrypted",
                table: "TenantEmailSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImapPort",
                table: "TenantEmailSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ImapUseSsl",
                table: "TenantEmailSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ImapUsername",
                table: "TenantEmailSettings",
                type: "TEXT",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "InboundEnabled",
                table: "TenantEmailSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImapFolder",
                table: "TenantEmailSettings");

            migrationBuilder.DropColumn(
                name: "ImapHost",
                table: "TenantEmailSettings");

            migrationBuilder.DropColumn(
                name: "ImapPasswordEncrypted",
                table: "TenantEmailSettings");

            migrationBuilder.DropColumn(
                name: "ImapPort",
                table: "TenantEmailSettings");

            migrationBuilder.DropColumn(
                name: "ImapUseSsl",
                table: "TenantEmailSettings");

            migrationBuilder.DropColumn(
                name: "ImapUsername",
                table: "TenantEmailSettings");

            migrationBuilder.DropColumn(
                name: "InboundEnabled",
                table: "TenantEmailSettings");
        }
    }
}
