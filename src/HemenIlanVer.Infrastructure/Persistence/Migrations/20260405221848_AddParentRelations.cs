using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemenIlanVer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParentRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentAttributeId",
                table: "category_attributes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentOptionId",
                table: "category_attribute_options",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_category_attributes_ParentAttributeId",
                table: "category_attributes",
                column: "ParentAttributeId");

            migrationBuilder.CreateIndex(
                name: "IX_category_attribute_options_ParentOptionId",
                table: "category_attribute_options",
                column: "ParentOptionId");

            migrationBuilder.AddForeignKey(
                name: "FK_category_attribute_options_category_attribute_options_Paren~",
                table: "category_attribute_options",
                column: "ParentOptionId",
                principalTable: "category_attribute_options",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_category_attributes_category_attributes_ParentAttributeId",
                table: "category_attributes",
                column: "ParentAttributeId",
                principalTable: "category_attributes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_category_attribute_options_category_attribute_options_Paren~",
                table: "category_attribute_options");

            migrationBuilder.DropForeignKey(
                name: "FK_category_attributes_category_attributes_ParentAttributeId",
                table: "category_attributes");

            migrationBuilder.DropIndex(
                name: "IX_category_attributes_ParentAttributeId",
                table: "category_attributes");

            migrationBuilder.DropIndex(
                name: "IX_category_attribute_options_ParentOptionId",
                table: "category_attribute_options");

            migrationBuilder.DropColumn(
                name: "ParentAttributeId",
                table: "category_attributes");

            migrationBuilder.DropColumn(
                name: "ParentOptionId",
                table: "category_attribute_options");
        }
    }
}
