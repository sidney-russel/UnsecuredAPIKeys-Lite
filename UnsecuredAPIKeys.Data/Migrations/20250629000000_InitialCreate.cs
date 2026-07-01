using System;
using Microsoft.EntityFrameworkCore.Migrations;
using UnsecuredAPIKeys.Data.Common;

#nullable disable

namespace UnsecuredAPIKeys.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "APIKeys",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApiKey = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ApiType = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: -99),
                    SearchProvider = table.Column<int>(type: "INTEGER", nullable: false),
                    LastCheckedUTC = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FirstFoundUTC = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastFoundUTC = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TimesDisplayed = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    ErrorCount = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_APIKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "SearchProviderTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Token = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    SearchProvider = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: -99),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastUsedUTC = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchProviderTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SearchQueries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Query = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SearchResultsCount = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    LastSearchUTC = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchQueries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RepoReferences",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    APIKeyId = table.Column<long>(type: "INTEGER", nullable: false),
                    RepoURL = table.Column<string>(type: "TEXT", nullable: false),
                    RepoOwner = table.Column<string>(type: "TEXT", nullable: true),
                    RepoName = table.Column<string>(type: "TEXT", nullable: true),
                    RepoDescription = table.Column<string>(type: "TEXT", nullable: true),
                    RepoId = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    FileURL = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: true),
                    FilePath = table.Column<string>(type: "TEXT", nullable: true),
                    FileSHA = table.Column<string>(type: "TEXT", nullable: true),
                    ApiContentUrl = table.Column<string>(type: "TEXT", nullable: true),
                    CodeContext = table.Column<string>(type: "TEXT", nullable: true),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    SearchQueryId = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    FoundUTC = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", nullable: true),
                    Branch = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepoReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepoReferences_APIKeys_APIKeyId",
                        column: x => x.APIKeyId,
                        principalTable: "APIKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_APIKeys_ApiKey",
                table: "APIKeys",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_APIKeys_LastCheckedUTC",
                table: "APIKeys",
                column: "LastCheckedUTC");

            migrationBuilder.CreateIndex(
                name: "IX_APIKeys_Status",
                table: "APIKeys",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_APIKeys_Status_ApiType",
                table: "APIKeys",
                columns: new[] { "Status", "ApiType" });

            migrationBuilder.CreateIndex(
                name: "IX_RepoReferences_ApiKeyId",
                table: "RepoReferences",
                column: "APIKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_SearchProviderTokens_SearchProvider",
                table: "SearchProviderTokens",
                column: "SearchProvider");

            migrationBuilder.CreateIndex(
                name: "IX_SearchQueries_IsEnabled_LastSearchUTC",
                table: "SearchQueries",
                columns: new[] { "IsEnabled", "LastSearchUTC" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ApplicationSettings");
            migrationBuilder.DropTable(name: "RepoReferences");
            migrationBuilder.DropTable(name: "SearchProviderTokens");
            migrationBuilder.DropTable(name: "SearchQueries");
            migrationBuilder.DropTable(name: "APIKeys");
        }
    }
}
