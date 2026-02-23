using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Household.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskInstanceDueDateSlotIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TaskInstances_DueDate_TimeOfDaySlot",
                table: "TaskInstances",
                columns: new[] { "DueDate", "TimeOfDaySlot" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_TaskInstances_DueDate_TimeOfDaySlot", table: "TaskInstances");
        }
    }
}
