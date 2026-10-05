using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CartService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGuestTokenIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_carts_guest_token_hash",
                table: "carts",
                column: "guest_token_hash",
                unique: true,
                filter: "guest_token_hash IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_carts_guest_token_hash",
                table: "carts");
        }
    }
}
