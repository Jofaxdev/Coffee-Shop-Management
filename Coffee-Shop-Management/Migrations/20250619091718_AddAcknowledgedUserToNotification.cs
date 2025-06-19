using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coffee_Shop_Management.Migrations
{
    /// <inheritdoc />
    public partial class AddAcknowledgedUserToNotification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AcknowledgedByUserId",
                table: "Notifications",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcknowledgedByUserName",
                table: "Notifications",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_AcknowledgedByUserId",
                table: "Notifications",
                column: "AcknowledgedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Users_AcknowledgedByUserId",
                table: "Notifications",
                column: "AcknowledgedByUserId",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_Users_AcknowledgedByUserId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_AcknowledgedByUserId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "AcknowledgedByUserId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "AcknowledgedByUserName",
                table: "Notifications");
        }
    }
}
