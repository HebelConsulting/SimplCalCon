using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimplCalCon.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class CollectionFeedToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FeedToken",
                table: "Collections",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Collections_FeedToken",
                table: "Collections",
                column: "FeedToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Collections_FeedToken",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "FeedToken",
                table: "Collections");
        }
    }
}
