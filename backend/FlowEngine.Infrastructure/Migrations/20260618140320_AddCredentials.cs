using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false, comment: "主键"),
                    name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false, comment: "凭据名称"),
                    type = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false, comment: "凭据类型"),
                    data = table.Column<string>(type: "TEXT", nullable: false, comment: "加密字段数据 JSON"),
                    key_version = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false, comment: "密钥版本"),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, comment: "创建时间"),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: true, comment: "更新时间")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credentials", x => x.id);
                },
                comment: "凭据定义");

            migrationBuilder.CreateIndex(
                name: "IX_credentials_name",
                table: "credentials",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "IX_credentials_type",
                table: "credentials",
                column: "type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credentials");
        }
    }
}
