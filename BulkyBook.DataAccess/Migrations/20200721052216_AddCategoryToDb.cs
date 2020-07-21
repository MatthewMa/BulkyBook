using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BulkyBook.DataAccess.Migrations
{
    public partial class AddCategoryToDb : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<int>(maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

        }
    }
}
