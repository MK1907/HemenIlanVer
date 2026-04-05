using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemenIlanVer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddListingEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");

            migrationBuilder.CreateTable(
                name: "listing_embeddings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ListingId = table.Column<Guid>(type: "uuid", nullable: false),
                    SearchableText = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_listing_embeddings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_listing_embeddings_listings_ListingId",
                        column: x => x.ListingId,
                        principalTable: "listings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_listing_embeddings_ListingId",
                table: "listing_embeddings",
                column: "ListingId",
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE listing_embeddings ADD COLUMN "Embedding" vector(1536);
                CREATE INDEX ix_listing_embeddings_hnsw ON listing_embeddings
                    USING hnsw ("Embedding" vector_cosine_ops)
                    WITH (m = 16, ef_construction = 64);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS ix_listing_embeddings_hnsw;""");
            migrationBuilder.DropTable(
                name: "listing_embeddings");
        }
    }
}
