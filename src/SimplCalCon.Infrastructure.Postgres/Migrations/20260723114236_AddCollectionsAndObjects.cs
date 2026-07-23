using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimplCalCon.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectionsAndObjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ResourceName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ChangeSequence = table.Column<long>(type: "bigint", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionType = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    Color = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SupportsEvents = table.Column<bool>(type: "boolean", nullable: true),
                    SupportsTasks = table.Column<bool>(type: "boolean", nullable: true),
                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Collections_Principals_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Principals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Collections_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Objects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Uid = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ResourceName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Blob = table.Column<string>(type: "text", nullable: false),
                    RevisionNumber = table.Column<long>(type: "bigint", nullable: false),
                    ChangeNumber = table.Column<long>(type: "bigint", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectType = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: false),
                    ComponentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Summary = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    DtStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DtEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsAllDay = table.Column<bool>(type: "boolean", nullable: true),
                    IsRecurring = table.Column<bool>(type: "boolean", nullable: true),
                    FormattedName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    FamilyName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    GivenName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Organization = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Emails = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Phones = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Objects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Objects_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ObjectRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<long>(type: "bigint", nullable: false),
                    Blob = table.Column<string>(type: "text", nullable: false),
                    ETag = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AuthorPrincipalId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObjectRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ObjectRevisions_Objects_ObjectId",
                        column: x => x.ObjectId,
                        principalTable: "Objects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Collections_OwnerId_ResourceName",
                table: "Collections",
                columns: new[] { "OwnerId", "ResourceName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Collections_TenantId",
                table: "Collections",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ObjectRevisions_ObjectId_RevisionNumber",
                table: "ObjectRevisions",
                columns: new[] { "ObjectId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Objects_CollectionId_ChangeNumber",
                table: "Objects",
                columns: new[] { "CollectionId", "ChangeNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Objects_CollectionId_DtStartUtc",
                table: "Objects",
                columns: new[] { "CollectionId", "DtStartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Objects_CollectionId_FamilyName",
                table: "Objects",
                columns: new[] { "CollectionId", "FamilyName" });

            migrationBuilder.CreateIndex(
                name: "IX_Objects_CollectionId_ResourceName",
                table: "Objects",
                columns: new[] { "CollectionId", "ResourceName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Objects_CollectionId_Uid",
                table: "Objects",
                columns: new[] { "CollectionId", "Uid" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ObjectRevisions");

            migrationBuilder.DropTable(
                name: "Objects");

            migrationBuilder.DropTable(
                name: "Collections");
        }
    }
}
