﻿using Microsoft.EntityFrameworkCore.Migrations;

namespace Mewdeko.Database.Migrations;

public partial class Highlights : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("Highlights",
            columns => new
            {
                Id = columns.Column<int>().Annotation("Sqlite:Autoincrement", true),
                GuildId = columns.Column<ulong>(),
                UserId = columns.Column<ulong>(),
                Word = columns.Column<string>(),
                DateAdded = columns.Column<DateTime>()
            });

        migrationBuilder.CreateTable("HighlightSettings",
            columns => new
            {
                Id = columns.Column<int>().Annotation("Sqlite:Autoincrement", true),
                GuildId = columns.Column<ulong>(),
                UserId = columns.Column<ulong>(),
                IgnoredUsers = columns.Column<string>(),
                IgnoredChannels = columns.Column<string>(),
                HighlightsOn = columns.Column<bool>(),
                DateAdded = columns.Column<DateTime>()
            });
    }
}