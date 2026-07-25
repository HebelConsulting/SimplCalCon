using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimplCalCon.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class OccurrenceWindowIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OccurrencesComplete",
                table: "Objects",
                type: "boolean",
                nullable: true,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "OccurrencesFromUtc",
                table: "Objects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OccurrencesUntilUtc",
                table: "Objects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EventOccurrences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventOccurrences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventOccurrences_Objects_ObjectId",
                        column: x => x.ObjectId,
                        principalTable: "Objects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventOccurrences_CollectionId_StartUtc",
                table: "EventOccurrences",
                columns: new[] { "CollectionId", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EventOccurrences_ObjectId",
                table: "EventOccurrences",
                column: "ObjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventOccurrences");

            migrationBuilder.DropColumn(
                name: "OccurrencesComplete",
                table: "Objects");

            migrationBuilder.DropColumn(
                name: "OccurrencesFromUtc",
                table: "Objects");

            migrationBuilder.DropColumn(
                name: "OccurrencesUntilUtc",
                table: "Objects");
        }
    }
}
