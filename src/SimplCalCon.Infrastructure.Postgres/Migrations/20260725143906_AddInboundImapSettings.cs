using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimplCalCon.Infrastructure.Postgres.Migrations
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
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImapHost",
                table: "TenantEmailSettings",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImapPasswordEncrypted",
                table: "TenantEmailSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImapPort",
                table: "TenantEmailSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ImapUseSsl",
                table: "TenantEmailSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ImapUsername",
                table: "TenantEmailSettings",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "InboundEnabled",
                table: "TenantEmailSettings",
                type: "boolean",
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
